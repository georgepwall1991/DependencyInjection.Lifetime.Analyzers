using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects non-thread-safe services (DbContext, DbConnection, HttpContext, ...)
/// captured into handlers that frameworks invoke concurrently: Service Bus / Event Hubs
/// processors, overlapping timers, and Parallel.* bodies. This is the deferred form of the
/// captive dependency: the lifetimes can look correct, but one instance is shared across
/// concurrent invocations and fails at runtime ("A second operation was started on this
/// context"). DI021 fires when concurrency is proven; DI022 (Info) fires when the sink's
/// concurrency is controlled by a configuration knob that cannot be proven at compile time.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI021_ConcurrentHandlerSharedStateAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.ConcurrentHandlerSharedState,
            DiagnosticDescriptors.ConcurrentHandlerConfigGatedSharedState);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeEventAssignment, OperationKind.EventAssignment);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzeMethodBody, OperationKind.MethodBody);
    }

    private enum SinkConcurrency
    {
        /// <summary>Concurrency is proven (or is the sink's documented default behavior).</summary>
        Concurrent,

        /// <summary>The sink's concurrency knob exists but its value cannot be proven.</summary>
        Unprovable,

        /// <summary>The sink is proven sequential; no diagnostic.</summary>
        Sequential
    }

    private readonly struct SinkContext
    {
        public SinkContext(
            SinkConcurrency concurrency,
            string description,
            string sinkDisplay,
            string knobName,
            ISymbol? timerInstance = null)
        {
            Concurrency = concurrency;
            Description = description;
            SinkDisplay = sinkDisplay;
            KnobName = knobName;
            TimerInstance = timerInstance;
        }

        public SinkConcurrency Concurrency { get; }

        /// <summary>DI021 message argument: sink plus why it is concurrent.</summary>
        public string Description { get; }

        /// <summary>DI022 message argument: the sink member.</summary>
        public string SinkDisplay { get; }

        /// <summary>DI022 message argument: the configuration knob controlling concurrency.</summary>
        public string KnobName { get; }

        /// <summary>For timer sinks: the local/field holding this timer, used to correlate re-arm guards.</summary>
        public ISymbol? TimerInstance { get; }
    }

    // ---------------------------------------------------------------------
    // Sink detection
    // ---------------------------------------------------------------------

    private static void AnalyzeEventAssignment(OperationAnalysisContext context)
    {
        var assignment = (IEventAssignmentOperation)context.Operation;
        if (!assignment.Adds)
        {
            return;
        }

        if (assignment.EventReference is not IEventReferenceOperation eventReference)
        {
            return;
        }

        var declaringType = eventReference.Event.ContainingType?.ToDisplayString();
        var eventName = eventReference.Event.Name;
        SinkContext sink;
        switch (declaringType)
        {
            case "Azure.Messaging.ServiceBus.ServiceBusProcessor"
                when eventName is "ProcessMessageAsync" or "ProcessErrorAsync":
            {
                var trace = TraceEventSinkOptions(
                    context, eventReference, "Azure.Messaging.ServiceBus.ServiceBusProcessorOptions");
                var knob = EvaluateTracedKnob(context, assignment.Syntax, trace, "MaxConcurrentCalls");
                var concurrency = knob switch
                {
                    KnobProof.ProvenConcurrent => SinkConcurrency.Concurrent,
                    // Only an options instance tied to THIS processor proves sequential dispatch;
                    // a MaxConcurrentCalls = 1 elsewhere in the type must not silence other sinks.
                    KnobProof.ProvenSequential when trace.Traced => SinkConcurrency.Sequential,
                    _ => SinkConcurrency.Unprovable
                };
                sink = new SinkContext(
                    concurrency,
                    $"ServiceBusProcessor.{eventName} (MaxConcurrentCalls is set above 1)",
                    $"ServiceBusProcessor.{eventName}",
                    "MaxConcurrentCalls");
                break;
            }

            case "Azure.Messaging.ServiceBus.ServiceBusSessionProcessor"
                when eventName is "ProcessMessageAsync" or "ProcessErrorAsync":
            {
                // Sessions are pumped concurrently by default; only a proven single-session,
                // single-call configuration on this processor's own options makes dispatch sequential.
                var trace = TraceEventSinkOptions(
                    context, eventReference, "Azure.Messaging.ServiceBus.ServiceBusSessionProcessorOptions");
                var sessions = EvaluateTracedKnob(context, assignment.Syntax, trace, "MaxConcurrentSessions");
                var callsPerSession = EvaluateTracedKnob(
                    context, assignment.Syntax, trace, "MaxConcurrentCallsPerSession");
                var sequential = trace.Traced &&
                                 sessions == KnobProof.ProvenSequential &&
                                 callsPerSession is KnobProof.ProvenSequential or KnobProof.NotFound;
                sink = new SinkContext(
                    sequential ? SinkConcurrency.Sequential : SinkConcurrency.Concurrent,
                    $"ServiceBusSessionProcessor.{eventName} (sessions are processed concurrently)",
                    $"ServiceBusSessionProcessor.{eventName}",
                    "MaxConcurrentSessions");
                break;
            }

            // The shipping SDK declares EventProcessorClient in Azure.Messaging.EventHubs (the
            // PACKAGE is named .Processor); both namespaces are accepted to be drift-proof.
            case "Azure.Messaging.EventHubs.EventProcessorClient"
                or "Azure.Messaging.EventHubs.Processor.EventProcessorClient"
                when eventName is "ProcessEventAsync" or "ProcessErrorAsync":
                sink = new SinkContext(
                    SinkConcurrency.Concurrent,
                    $"EventProcessorClient.{eventName} (partitions are processed concurrently)",
                    $"EventProcessorClient.{eventName}",
                    "partition count");
                break;

            // RabbitMQ.Client across the v6/v7 surface drift: v6 ships EventingBasicConsumer.Received
            // (sync) and AsyncEventingBasicConsumer.Received (async); v7 renames the async event to
            // ReceivedAsync. EventingBasicConsumer has no ReceivedAsync member, so the combined name
            // set cannot over-match.
            case "RabbitMQ.Client.Events.EventingBasicConsumer"
                or "RabbitMQ.Client.Events.AsyncEventingBasicConsumer"
                when eventName is "Received" or "ReceivedAsync":
            {
                // The dispatch pump is sequential by default (ConsumerDispatchConcurrency = 1).
                // When the consumer's own factory -> connection -> channel chain is traceable in
                // the same tree, the knob is proven instance-correlated: a different factory's
                // setting never contaminates this consumer, and a fresh factory that never sets
                // the knob keeps the sequential default. Untraceable chains fall back to the
                // strengthen-only containing-type scan (a constant above 1 anywhere is still
                // evidence of concurrency) with the config-gated DI022 tier.
                SinkConcurrency concurrency;
                if (TryTraceRabbitConsumerChain(
                        context, eventReference, out var chainTrace, out var freshFactory, out var channelOptionsOverride) &&
                    !channelOptionsOverride)
                {
                    var chainKnob = EvaluateTracedKnob(
                        context, assignment.Syntax, chainTrace, "ConsumerDispatchConcurrency");
                    concurrency = chainKnob switch
                    {
                        KnobProof.ProvenConcurrent => SinkConcurrency.Concurrent,
                        KnobProof.ProvenSequential => SinkConcurrency.Sequential,
                        KnobProof.NotFound when freshFactory => SinkConcurrency.Sequential,
                        _ => SinkConcurrency.Unprovable
                    };
                }
                else if (channelOptionsOverride)
                {
                    // Per-channel options can pin THIS channel back to sequential regardless of
                    // the factory knob, so nothing in the type may upgrade past the config-gated
                    // tier.
                    concurrency = SinkConcurrency.Unprovable;
                }
                else
                {
                    var knob = EvaluateTracedKnob(
                        context, assignment.Syntax, OptionsTrace.Untraceable, "ConsumerDispatchConcurrency");
                    concurrency = knob == KnobProof.ProvenConcurrent
                        ? SinkConcurrency.Concurrent
                        : SinkConcurrency.Unprovable;
                }

                var consumerName = eventReference.Event.ContainingType!.Name;
                sink = new SinkContext(
                    concurrency,
                    $"{consumerName}.{eventName} (ConsumerDispatchConcurrency is set above 1)",
                    $"{consumerName}.{eventName}",
                    "ConsumerDispatchConcurrency");
                break;
            }

            case "System.Timers.Timer" when eventName == "Elapsed":
            {
                if (IsTimersTimerSequential(context, assignment.Syntax, eventReference))
                {
                    return;
                }

                sink = new SinkContext(
                    SinkConcurrency.Concurrent,
                    "System.Timers.Timer.Elapsed (elapsed events can overlap)",
                    "System.Timers.Timer.Elapsed",
                    "AutoReset");
                break;
            }

            default:
                return;
        }

        AnalyzeHandlerValue(context, assignment.HandlerValue, sink, assignment.Syntax);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name == "ForAll" &&
            method.ContainingType?.ToDisplayString() == "System.Linq.ParallelEnumerable")
        {
            AnalyzePlinqForAll(context, invocation);
            return;
        }

        if (method.Name is not ("For" or "ForEach" or "ForEachAsync" or "Invoke"))
        {
            return;
        }

        if (method.ContainingType?.ToDisplayString() != "System.Threading.Tasks.Parallel")
        {
            return;
        }

        // Only the ParallelOptions instance passed to THIS call can prove sequential execution.
        var optionsTrace = TraceInvocationOptionsArgument(
            invocation, "System.Threading.Tasks.ParallelOptions");
        var knob = EvaluateTracedKnob(context, invocation.Syntax, optionsTrace, "MaxDegreeOfParallelism");
        if (optionsTrace.Traced && knob == KnobProof.ProvenSequential)
        {
            return;
        }

        var sink = new SinkContext(
            SinkConcurrency.Concurrent,
            $"Parallel.{method.Name} (iterations run concurrently)",
            $"Parallel.{method.Name}",
            "MaxDegreeOfParallelism");

        foreach (var argument in invocation.Arguments)
        {
            var value = Unwrap(argument.Value);
            if (value is IArrayCreationOperation { Initializer: { } initializer })
            {
                // Parallel.Invoke(params Action[]) surfaces as a single array-creation argument.
                foreach (var element in initializer.ElementValues)
                {
                    AnalyzeHandlerValue(context, element, sink, invocation.Syntax);
                }
            }
            else if (value is IDelegateCreationOperation)
            {
                AnalyzeHandlerValue(context, value, sink, invocation.Syntax);
            }
        }
    }

    /// <summary>
    /// PLINQ ForAll runs partitions concurrently by default. Only a proven
    /// WithDegreeOfParallelism(1) on this query's own chain makes execution sequential —
    /// an unprovable degree or an untraceable query keeps the truthful default (concurrent).
    /// </summary>
    private static void AnalyzePlinqForAll(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        var sourceArgument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "source");
        if (sourceArgument is not null && QueryChainProvesSequential(sourceArgument.Value))
        {
            return;
        }

        var sink = new SinkContext(
            SinkConcurrency.Concurrent,
            "PLINQ ForAll (partitions run concurrently)",
            "PLINQ ForAll",
            "WithDegreeOfParallelism");

        var actionArgument = invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "action");
        if (actionArgument is not null)
        {
            AnalyzeHandlerValue(context, actionArgument.Value, sink, invocation.Syntax);
        }
    }

    private static bool QueryChainProvesSequential(IOperation source)
    {
        var current = Unwrap(source);
        while (current is IInvocationOperation chain)
        {
            if (chain.TargetMethod.Name == "WithDegreeOfParallelism" &&
                chain.TargetMethod.ContainingType?.ToDisplayString() == "System.Linq.ParallelEnumerable")
            {
                // The nearest degree setting wins; constant 1 proves sequential, anything else
                // (a higher constant, an unprovable expression) does not.
                var degreeArgument = chain.Arguments.FirstOrDefault(
                    a => a.Parameter?.Name == "degreeOfParallelism");
                return degreeArgument is not null &&
                       degreeArgument.Value.ConstantValue is { HasValue: true } constant &&
                       TryGetIntegralConstant(constant.Value, out var degree) &&
                       degree == 1;
            }

            // Walk the receiver-position argument of the fluent chain. Binary operators
            // (Concat, Zip, Join) name it "first"/"outer" rather than "source"; only
            // ParallelEnumerable links are followed.
            if (chain.TargetMethod.ContainingType?.ToDisplayString() != "System.Linq.ParallelEnumerable")
            {
                return false;
            }

            var inner = chain.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0)?.Value;
            if (inner is null)
            {
                return false;
            }

            current = Unwrap(inner);
        }

        return false;
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is INamedTypeSymbol blockType &&
            blockType.Name is "ActionBlock" or "TransformBlock" or "TransformManyBlock" &&
            blockType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks.Dataflow")
        {
            AnalyzeDataflowBlockCreation(context, creation, blockType.Name);
            return;
        }

        if (creation.Type is not INamedTypeSymbol { Name: "Timer" } timerType ||
            timerType.ToDisplayString() != "System.Threading.Timer")
        {
            return;
        }

        var periodArgument = creation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "period");
        var dueTimeArgument = creation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "dueTime");
        if (periodArgument is not null)
        {
            // Infinite and zero periods are both non-recurring; a single invocation cannot overlap.
            if (IsProvablyNonRecurringPeriod(periodArgument.Value))
            {
                return;
            }

            // An infinite due time creates the timer disabled: even with a finite period it never
            // fires unless a later Change(...) on this same timer starts it.
            if (dueTimeArgument is not null &&
                IsProvablyInfinitePeriod(dueTimeArgument.Value) &&
                !TimerInstanceIsStartedByChange(context, creation))
            {
                return;
            }
        }
        else if (!TimerInstanceIsStartedByChange(context, creation))
        {
            // new Timer(callback) without a later finite Change(...) never fires periodically.
            return;
        }

        var callbackArgument = creation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "callback");
        if (callbackArgument is null)
        {
            return;
        }

        var sink = new SinkContext(
            SinkConcurrency.Concurrent,
            "System.Threading.Timer callbacks (timer callbacks can overlap)",
            "System.Threading.Timer callbacks",
            "period",
            timerInstance: GetCreationTargetSymbol(creation));
        AnalyzeHandlerValue(context, callbackArgument.Value, sink, creation.Syntax);
    }

    /// <summary>
    /// TPL Dataflow execution blocks run their delegate sequentially by default
    /// (MaxDegreeOfParallelism = 1). A block without options is silent; a traced options object
    /// proving the knob above 1 (or Unbounded = -1) reports DI021; a traced object that never
    /// sets the knob keeps the sequential default; anything unprovable is config-gated (DI022).
    /// </summary>
    private static void AnalyzeDataflowBlockCreation(
        OperationAnalysisContext context,
        IObjectCreationOperation creation,
        string blockTypeName)
    {
        var optionsArgument = creation.Arguments.FirstOrDefault(a =>
            a.Parameter?.Type?.ToDisplayString() == "System.Threading.Tasks.Dataflow.ExecutionDataflowBlockOptions");
        if (optionsArgument is null)
        {
            return;
        }

        var trace = Unwrap(optionsArgument.Value) switch
        {
            IObjectCreationOperation { Syntax: BaseObjectCreationExpressionSyntax creationExpression } =>
                OptionsTrace.ForInlineCreation(creationExpression),
            ILocalReferenceOperation local => OptionsTrace.ForSymbol(local.Local, creation.Syntax.SpanStart),
            IFieldReferenceOperation field => OptionsTrace.ForSymbol(field.Field),
            IParameterReferenceOperation parameter =>
                OptionsTrace.ForSymbol(parameter.Parameter, creation.Syntax.SpanStart),
            IInvocationOperation helperCall when
                TryGetSameTreeHelperDeclaration(helperCall.TargetMethod, creation.Syntax) is { } helper =>
                OptionsTrace.ForHelper(helper),
            _ => OptionsTrace.WithoutKnownOptions
        };

        // Only an options object whose construction we actually saw (inline creation or a
        // same-tree helper's fresh creation) can keep the sequential default of 1 when the knob
        // is never written. A parameter or field with no observed writes proves nothing — the
        // caller may have set the knob.
        var tracedToFreshCreation = trace.InlineCreation is not null || trace.HelperDeclaration is not null;
        var knob = EvaluateTracedKnob(context, creation.Syntax, trace, "MaxDegreeOfParallelism");
        var concurrency = knob switch
        {
            KnobProof.ProvenConcurrent => SinkConcurrency.Concurrent,
            KnobProof.ProvenSequential when trace.Traced => SinkConcurrency.Sequential,
            KnobProof.NotFound when tracedToFreshCreation => SinkConcurrency.Sequential,
            _ => SinkConcurrency.Unprovable
        };

        if (concurrency == SinkConcurrency.Sequential)
        {
            return;
        }

        var sink = new SinkContext(
            concurrency,
            $"{blockTypeName} (MaxDegreeOfParallelism is set above 1)",
            blockTypeName,
            "MaxDegreeOfParallelism");

        var handlerArgument = creation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0);
        if (handlerArgument is not null)
        {
            AnalyzeHandlerValue(context, handlerArgument.Value, sink, creation.Syntax);
        }
    }

    /// <summary>
    /// EventHubs batch processing: user subclasses of
    /// Azure.Messaging.EventHubs.Primitives.EventProcessor&lt;TPartition&gt; have their batch and
    /// error overrides invoked concurrently across partitions — the override body IS the
    /// handler, and instance fields are the capture channel.
    /// </summary>
    private static void AnalyzeMethodBody(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is not IMethodSymbol method || !method.IsOverride)
        {
            return;
        }

        var overridden = method.OverriddenMethod;
        while (overridden?.OverriddenMethod is not null)
        {
            overridden = overridden.OverriddenMethod;
        }

        if (overridden is null ||
            overridden.Name is not ("OnProcessingEventBatchAsync" or "OnProcessingErrorAsync"))
        {
            return;
        }

        var declaringType = overridden.ContainingType?.OriginalDefinition;
        if (declaringType is null ||
            declaringType.Name != "EventProcessor" ||
            declaringType.ContainingNamespace?.ToDisplayString() != "Azure.Messaging.EventHubs.Primitives")
        {
            return;
        }

        var methodBody = (IMethodBodyOperation)context.Operation;
        var body = methodBody.BlockBody ?? methodBody.ExpressionBody;
        if (body is null)
        {
            return;
        }

        var sink = new SinkContext(
            SinkConcurrency.Concurrent,
            $"EventProcessor<TPartition>.{overridden.Name} (partitions are processed concurrently)",
            $"EventProcessor<TPartition>.{overridden.Name}",
            "partition count");
        AnalyzeHandlerBody(context, body, methodBody.Syntax, method, sink, methodBody.Syntax);
    }

    /// <summary>
    /// Traces a RabbitMQ consumer's event receiver back through its own creation chain —
    /// consumer ctor's channel argument, the channel's CreateModel/CreateChannel(Async) call,
    /// the connection's CreateConnection(Async) call — to the ConnectionFactory the chain
    /// actually used. Every link must be unique in the containing type (one initializer or
    /// assignment) or the chain is untraceable. A CreateChannelOptions argument bails: it can
    /// override the factory knob per channel.
    /// </summary>
    private static bool TryTraceRabbitConsumerChain(
        OperationAnalysisContext context,
        IEventReferenceOperation eventReference,
        out OptionsTrace trace,
        out bool freshFactory,
        out bool channelOptionsOverride)
    {
        trace = OptionsTrace.Untraceable;
        freshFactory = false;
        channelOptionsOverride = false;
        var anchor = eventReference.Syntax;
        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel is null)
        {
            return false;
        }

        // 1. The consumer's creation expression.
        ExpressionSyntax? consumerCreation;
        var receiver = eventReference.Instance is null ? null : Unwrap(eventReference.Instance);
        if (receiver is IObjectCreationOperation inlineConsumer)
        {
            consumerCreation = inlineConsumer.Syntax as ExpressionSyntax;
        }
        else
        {
            var consumerSymbol = receiver switch
            {
                ILocalReferenceOperation local => (ISymbol)local.Local,
                IFieldReferenceOperation field => field.Field,
                _ => null
            };
            if (consumerSymbol is null)
            {
                return false;
            }

            consumerCreation = FindSingleInitializerValue(context, anchor, consumerSymbol);
        }

        if (consumerCreation is not BaseObjectCreationExpressionSyntax consumerNew ||
            consumerNew.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not { } channelExpression)
        {
            return false;
        }

        // 2. The channel's creation invocation.
        var channelCreation = ResolveToInvocation(context, anchor, channelExpression, semanticModel);
        if (channelCreation is null)
        {
            return false;
        }

        var channelMethodName = channelCreation.Expression switch
        {
            MemberAccessExpressionSyntax channelAccess => channelAccess.Name.Identifier.ValueText,
            _ => null
        };
        if (channelMethodName is not ("CreateModel" or "CreateChannel" or "CreateChannelAsync"))
        {
            return false;
        }

        // Per-channel options can override the factory knob — bail conservatively when an
        // actual CreateChannelOptions argument is supplied (a cancellation token or a null
        // literal is not an override), and tell the caller the fallback scan must not upgrade
        // either.
        foreach (var channelArgument in channelCreation.ArgumentList.Arguments)
        {
            if (channelArgument.Expression.IsKind(SyntaxKind.NullLiteralExpression) ||
                channelArgument.Expression.IsKind(SyntaxKind.DefaultLiteralExpression))
            {
                continue;
            }

            if (semanticModel.GetTypeInfo(channelArgument.Expression, context.CancellationToken).Type
                    ?.ToDisplayString() == "RabbitMQ.Client.CreateChannelOptions")
            {
                channelOptionsOverride = true;
                return false;
            }
        }

        var channelReceiver = ((MemberAccessExpressionSyntax)channelCreation.Expression).Expression;

        // 3. CreateChannel lives on the factory itself; CreateModel/CreateChannelAsync live on a
        //    connection whose creation must trace to the factory.
        ExpressionSyntax? factoryExpression;
        int consumptionPosition;
        if (channelMethodName == "CreateChannel")
        {
            factoryExpression = channelReceiver;
            consumptionPosition = channelCreation.SpanStart;
        }
        else
        {
            var connectionCreation = ResolveToInvocation(context, anchor, channelReceiver, semanticModel);
            if (connectionCreation?.Expression is not MemberAccessExpressionSyntax connectionAccess ||
                connectionAccess.Name.Identifier.ValueText is not ("CreateConnection" or "CreateConnectionAsync"))
            {
                return false;
            }

            factoryExpression = connectionAccess.Expression;
            consumptionPosition = connectionCreation.SpanStart;
        }

        // 4. The factory itself: an inline creation or a symbol with a unique origin.
        if (factoryExpression is BaseObjectCreationExpressionSyntax inlineFactory)
        {
            trace = OptionsTrace.ForInlineCreation(inlineFactory);
            freshFactory = true;
            return true;
        }

        if (factoryExpression is IdentifierNameSyntax factoryIdentifier &&
            semanticModel.GetSymbolInfo(factoryIdentifier, context.CancellationToken).Symbol is { } factorySymbol &&
            factorySymbol is ILocalSymbol or IFieldSymbol or IParameterSymbol)
        {
            // Writes after the chain consumed the factory belong to later reuse of the
            // variable and must not break the fresh-default proof for THIS consumer.
            freshFactory = FindSingleInitializerValue(context, anchor, factorySymbol, consumptionPosition)
                is BaseObjectCreationExpressionSyntax;
            trace = OptionsTrace.ForSymbol(
                factorySymbol,
                factorySymbol is ILocalSymbol or IParameterSymbol ? consumptionPosition : null);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves an expression to the invocation that produced it: directly, or through a
    /// local/field symbol whose unique initializer (awaits unwrapped) is an invocation.
    /// </summary>
    private static InvocationExpressionSyntax? ResolveToInvocation(
        OperationAnalysisContext context,
        SyntaxNode anchor,
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        if (expression is InvocationExpressionSyntax direct)
        {
            return direct;
        }

        if (expression is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is not { } symbol ||
            symbol is not (ILocalSymbol or IFieldSymbol))
        {
            return null;
        }

        return FindSingleInitializerValue(context, anchor, symbol) as InvocationExpressionSyntax;
    }

    /// <summary>
    /// The unique initializer or assignment value of a symbol within the containing type, with
    /// awaits unwrapped. More than one write makes the origin ambiguous (null).
    /// </summary>
    private static ExpressionSyntax? FindSingleInitializerValue(
        OperationAnalysisContext context,
        SyntaxNode anchor,
        ISymbol instance,
        int? beforePosition = null)
    {
        var typeDeclaration = anchor.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var semanticModel = context.Operation.SemanticModel;
        if (typeDeclaration is null || semanticModel is null)
        {
            return null;
        }

        ExpressionSyntax? single = null;
        foreach (var declarator in typeDeclaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (beforePosition is { } declaratorCutoff && declarator.SpanStart >= declaratorCutoff)
            {
                continue;
            }

            if (declarator.Initializer?.Value is { } value &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(declarator, context.CancellationToken), instance))
            {
                if (single is not null)
                {
                    return null;
                }

                single = value;
            }
        }

        foreach (var assignmentNode in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (beforePosition is { } assignmentCutoff && assignmentNode.SpanStart >= assignmentCutoff)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignmentNode.Left, context.CancellationToken).Symbol, instance))
            {
                if (single is not null)
                {
                    return null;
                }

                single = assignmentNode.Right;
            }
        }

        while (single is AwaitExpressionSyntax awaited)
        {
            single = awaited.Expression;
        }

        return single;
    }

    // ---------------------------------------------------------------------
    // Concurrency knob proofs
    // ---------------------------------------------------------------------

    private enum KnobProof
    {
        NotFound,
        ProvenConcurrent,
        ProvenSequential,
        Unprovable
    }

    /// <summary>
    /// Result lattice for knob-write collection. Conditional replacements are handled as a UNION
    /// of candidate values (branch-insensitive: every collected constant is a possible value, so
    /// a constant above 1 anywhere still proves concurrency). <see cref="Unknown"/> records that
    /// some candidate value is not statically known (a fresh replacement that never sets the
    /// knob, a compound write) — it poisons sequential proofs without erasing concurrent
    /// evidence. <see cref="Invalid"/> means instance identity was lost entirely.
    /// </summary>
    private enum KnobCollection
    {
        Provable,
        Unknown,
        Invalid
    }

    private static KnobCollection Worst(KnobCollection left, KnobCollection right) =>
        (KnobCollection)Math.Max((int)left, (int)right);

    /// <summary>
    /// The set of values a knob may hold at the consumption point. Definite writes (top-level
    /// statements in the block that declared the options local) overwrite the set; writes nested
    /// in deeper blocks are conditional and join the union instead.
    /// </summary>
    private sealed class KnobValueState
    {
        public List<ExpressionSyntax> Values { get; } = new();

        public bool HasUnknownCandidate { get; set; }

        /// <summary>
        /// Sticky poison for escapes and nested-function writes: an alias or deferred mutator can
        /// change the knob at any time, so no later definite write may restore a sequential
        /// proof. Unlike <see cref="HasUnknownCandidate"/>, this survives Overwrite.
        /// </summary>
        public bool Poisoned { get; set; }

        /// <summary>
        /// The creation that produced the instance never set the knob, so the type's default is
        /// itself a candidate. Harmless while no constants are collected (NotFound semantics),
        /// but a conditional write must not become the only collected value while the untaken
        /// branch still leaves the default in play. A definite write supersedes the default.
        /// </summary>
        public bool HasDefaultCandidate { get; set; }

        public bool IsUnknown =>
            HasUnknownCandidate || Poisoned || (HasDefaultCandidate && Values.Count > 0);

        public void Overwrite(List<ExpressionSyntax> replacements, bool unknown)
        {
            Values.Clear();
            Values.AddRange(replacements);
            HasUnknownCandidate = unknown;
            HasDefaultCandidate = false;
        }
    }

    /// <summary>
    /// A write definitively replaces the knob value only when it is a top-level statement of the
    /// very block that declared the options local — straight-line code in document order. Any
    /// deeper nesting (if/loop/try) makes it a conditional candidate.
    /// </summary>
    private static bool IsDefiniteWrite(AssignmentExpressionSyntax assignment, BlockSyntax? declarationBlock) =>
        declarationBlock is not null &&
        assignment.Parent is ExpressionStatementSyntax statement &&
        statement.Parent == declarationBlock;

    /// <summary>
    /// The options instance correlated to a specific sink. Proofs are only trusted when the
    /// instance was traced from the sink itself; a sequential knob on some other options object
    /// in the same type must never silence this sink.
    /// </summary>
    private readonly struct OptionsTrace
    {
        public static readonly OptionsTrace Untraceable = default;

        private OptionsTrace(
            bool traced,
            BaseObjectCreationExpressionSyntax? inlineCreation,
            ISymbol? optionsSymbol,
            MethodDeclarationSyntax? helperDeclaration,
            int? localCutoffPosition)
        {
            Traced = traced;
            InlineCreation = inlineCreation;
            OptionsSymbol = optionsSymbol;
            HelperDeclaration = helperDeclaration;
            LocalCutoffPosition = localCutoffPosition;
        }

        public bool Traced { get; }

        public BaseObjectCreationExpressionSyntax? InlineCreation { get; }

        public ISymbol? OptionsSymbol { get; }

        /// <summary>A same-tree non-virtual helper method whose fresh creation is the options instance.</summary>
        public MethodDeclarationSyntax? HelperDeclaration { get; }

        /// <summary>
        /// For local/parameter options symbols: the position where the sink consumed the options
        /// (the processor creation or Parallel call). The SDK snapshots option values there, so
        /// writes and reassignments after this position belong to later reuse of the variable,
        /// not to this sink.
        /// </summary>
        public int? LocalCutoffPosition { get; }

        public static OptionsTrace ForInlineCreation(BaseObjectCreationExpressionSyntax creation) =>
            new(traced: true, creation, optionsSymbol: null, helperDeclaration: null, localCutoffPosition: null);

        public static OptionsTrace ForSymbol(ISymbol symbol, int? localCutoffPosition = null) =>
            new(traced: true, inlineCreation: null, symbol, helperDeclaration: null, localCutoffPosition);

        public static OptionsTrace ForHelper(MethodDeclarationSyntax helper) =>
            new(traced: true, inlineCreation: null, optionsSymbol: null, helper, localCutoffPosition: null);

        /// <summary>Sink traced, but no options argument or an opaque options expression.</summary>
        public static OptionsTrace WithoutKnownOptions { get; } =
            new(traced: true, inlineCreation: null, optionsSymbol: null, helperDeclaration: null, localCutoffPosition: null);
    }

    /// <summary>
    /// Traces the event receiver back to the processor creation that produced it and returns the
    /// options argument of that creation. Receivers held in a local or field are followed through
    /// their single initializer/assignment; anything else is untraceable.
    /// </summary>
    private static OptionsTrace TraceEventSinkOptions(
        OperationAnalysisContext context,
        IEventReferenceOperation eventReference,
        string optionsTypeName)
    {
        var receiver = eventReference.Instance is null ? null : Unwrap(eventReference.Instance);
        InvocationExpressionSyntax? creationSyntax = null;
        if (receiver is IInvocationOperation inlineInvocation)
        {
            creationSyntax = inlineInvocation.Syntax as InvocationExpressionSyntax;
        }
        else
        {
            var origin = receiver switch
            {
                ILocalReferenceOperation local => (ISymbol)local.Local,
                IFieldReferenceOperation field => field.Field,
                _ => null
            };
            if (origin is null)
            {
                return OptionsTrace.Untraceable;
            }

            creationSyntax = FindSingleCreationInvocation(context, eventReference.Syntax, origin);
        }

        if (creationSyntax is null ||
            context.Operation.SemanticModel?.GetOperation(creationSyntax, context.CancellationToken)
                is not IInvocationOperation creation)
        {
            return OptionsTrace.Untraceable;
        }

        var optionsArgument = creation.Arguments.FirstOrDefault(
            a => a.Parameter?.Type?.ToDisplayString() == optionsTypeName);
        if (optionsArgument is null)
        {
            return OptionsTrace.WithoutKnownOptions;
        }

        return Unwrap(optionsArgument.Value) switch
        {
            // BaseObjectCreationExpressionSyntax covers both `new Options {...}` and the
            // target-typed `new() {...}` form.
            IObjectCreationOperation { Syntax: BaseObjectCreationExpressionSyntax creationExpression } =>
                OptionsTrace.ForInlineCreation(creationExpression),
            ILocalReferenceOperation local => OptionsTrace.ForSymbol(local.Local, creation.Syntax.SpanStart),
            IFieldReferenceOperation field => OptionsTrace.ForSymbol(field.Field),
            IParameterReferenceOperation parameter =>
                OptionsTrace.ForSymbol(parameter.Parameter, creation.Syntax.SpanStart),
            IInvocationOperation helperCall when
                TryGetSameTreeHelperDeclaration(helperCall.TargetMethod, eventReference.Syntax) is { } helper =>
                OptionsTrace.ForHelper(helper),
            _ => OptionsTrace.WithoutKnownOptions
        };
    }

    /// <summary>
    /// Returns the options argument passed directly to a Parallel.* invocation.
    /// </summary>
    private static OptionsTrace TraceInvocationOptionsArgument(
        IInvocationOperation invocation,
        string optionsTypeName)
    {
        var optionsArgument = invocation.Arguments.FirstOrDefault(
            a => a.Parameter?.Type?.ToDisplayString() == optionsTypeName);
        if (optionsArgument is null)
        {
            return OptionsTrace.WithoutKnownOptions;
        }

        return Unwrap(optionsArgument.Value) switch
        {
            // BaseObjectCreationExpressionSyntax covers both `new Options {...}` and the
            // target-typed `new() {...}` form.
            IObjectCreationOperation { Syntax: BaseObjectCreationExpressionSyntax creationExpression } =>
                OptionsTrace.ForInlineCreation(creationExpression),
            ILocalReferenceOperation local => OptionsTrace.ForSymbol(local.Local, invocation.Syntax.SpanStart),
            IFieldReferenceOperation field => OptionsTrace.ForSymbol(field.Field),
            IParameterReferenceOperation parameter =>
                OptionsTrace.ForSymbol(parameter.Parameter, invocation.Syntax.SpanStart),
            IInvocationOperation helperCall when
                TryGetSameTreeHelperDeclaration(helperCall.TargetMethod, invocation.Syntax) is { } helper =>
                OptionsTrace.ForHelper(helper),
            _ => OptionsTrace.WithoutKnownOptions
        };
    }

    /// <summary>
    /// Resolves an options-helper invocation target to its declaration when its body is proof of
    /// what runs: non-virtual (no override can substitute another body), a single declaration,
    /// and declared in the same syntax tree as the sink (the analyzer only queries the sink's own
    /// semantic model).
    /// </summary>
    private static MethodDeclarationSyntax? TryGetSameTreeHelperDeclaration(
        IMethodSymbol? method,
        SyntaxNode anchor)
    {
        if (method is null ||
            method.IsVirtual ||
            method.IsAbstract ||
            method.IsOverride ||
            method.DeclaringSyntaxReferences.Length != 1)
        {
            return null;
        }

        return method.DeclaringSyntaxReferences[0].GetSyntax() is MethodDeclarationSyntax declaration &&
               declaration.SyntaxTree == anchor.SyntaxTree
            ? declaration
            : null;
    }

    /// <summary>
    /// Collects knob writes from a helper method that provably returns a fresh options creation:
    /// an expression-bodied `=> new Options {...}`, a single `return new Options {...};`, or a
    /// single returned local whose one declaration is initialized with a creation (initializer
    /// writes plus member writes on that local inside the helper). A returned shared instance, a
    /// reassignment to anything but a fresh creation, or multiple returns contribute nothing —
    /// the helper then proves neither direction.
    /// </summary>
    private static KnobCollection CollectHelperKnobValues(
        OperationAnalysisContext context,
        MethodDeclarationSyntax helper,
        string knobName,
        List<ExpressionSyntax> values)
    {
        if (helper.ExpressionBody is not null &&
            UnwrapKnobExpression(helper.ExpressionBody.Expression) is BaseObjectCreationExpressionSyntax inlineCreation)
        {
            CollectInitializerValues(inlineCreation, knobName, values);
            return KnobCollection.Provable;
        }

        var semanticModel = context.Operation.SemanticModel;
        if (helper.Body is null || semanticModel is null)
        {
            return KnobCollection.Provable;
        }

        // Returns inside nested lambdas/local functions belong to those functions, not the helper.
        var returns = helper.Body.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Where(r => !IsInsideNestedFunction(r, helper))
            .ToList();
        if (returns.Count != 1)
        {
            return KnobCollection.Provable;
        }

        var returnExpression = UnwrapKnobExpression(returns[0].Expression);

        if (returnExpression is BaseObjectCreationExpressionSyntax returnedCreation)
        {
            CollectInitializerValues(returnedCreation, knobName, values);
            return KnobCollection.Provable;
        }

        if (returnExpression is not IdentifierNameSyntax returnedIdentifier ||
            semanticModel.GetSymbolInfo(returnedIdentifier, context.CancellationToken).Symbol
                is not ILocalSymbol returnedLocal)
        {
            return KnobCollection.Provable;
        }

        var declarators = new List<VariableDeclaratorSyntax>();
        foreach (var declarator in helper.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(declarator, context.CancellationToken), returnedLocal))
            {
                declarators.Add(declarator);
            }
        }

        if (declarators.Count != 1 ||
            declarators[0].Initializer?.Value is not BaseObjectCreationExpressionSyntax localCreation)
        {
            return KnobCollection.Provable;
        }

        var declarationBlock = declarators[0]
            .Ancestors()
            .OfType<BlockSyntax>()
            .FirstOrDefault();
        var state = new KnobValueState();
        CollectInitializerValues(localCreation, knobName, state.Values);
        state.HasDefaultCandidate = state.Values.Count == 0;
        if (HasKnobIncrement(helper, knobName, returnedLocal, semanticModel, context))
        {
            state.Poisoned = true;
        }

        // If the local escapes before the return (argument, alias initializer, ref/out — any use
        // other than the return itself, a member-access receiver, or an assignment target), the
        // callee or alias can change the knob, so sequential proofs are poisoned.
        foreach (var reference in helper.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (reference == returnedIdentifier ||
                (reference.Parent is MemberAccessExpressionSyntax receiverUse &&
                 receiverUse.Expression == reference) ||
                (reference.Parent is AssignmentExpressionSyntax assignmentTarget &&
                 assignmentTarget.Left == reference))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(reference, context.CancellationToken).Symbol, returnedLocal))
            {
                state.Poisoned = true;
                break;
            }
        }
        foreach (var assignment in helper.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var targetsReturnedLocal =
                (assignment.Left is IdentifierNameSyntax left &&
                 SymbolEqualityComparer.Default.Equals(
                     semanticModel.GetSymbolInfo(left, context.CancellationToken).Symbol, returnedLocal)) ||
                (assignment.Left is MemberAccessExpressionSyntax leftMember &&
                 SymbolEqualityComparer.Default.Equals(
                     semanticModel.GetSymbolInfo(leftMember.Expression, context.CancellationToken).Symbol,
                     returnedLocal));

            // A write inside a nested lambda or local function is deferred — it is not part of
            // constructing the returned options, and there is no telling when (or whether) it
            // runs relative to the sink. It poisons sequential proofs as an unknown candidate
            // but must not erase construction-time concurrent constants.
            if (targetsReturnedLocal && IsInsideNestedFunction(assignment, helper))
            {
                state.Poisoned = true;
                continue;
            }

            if (assignment.Left is IdentifierNameSyntax identifier &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol, returnedLocal))
            {
                if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                    assignment.Right is BaseObjectCreationExpressionSyntax replacementCreation)
                {
                    var replacementValues = new List<ExpressionSyntax>();
                    CollectInitializerValues(replacementCreation, knobName, replacementValues);
                    if (IsDefiniteWrite(assignment, declarationBlock))
                    {
                        // A straight-line replacement discards the previous object entirely; a
                        // replacement that never sets the knob leaves an unknowable default.
                        state.Overwrite(replacementValues, unknown: replacementValues.Count == 0);
                    }
                    else
                    {
                        // A conditional replacement joins the union of candidate objects.
                        state.Values.AddRange(replacementValues);
                        state.HasUnknownCandidate |= replacementValues.Count == 0;
                    }

                    continue;
                }

                // Reassigned to something that is not a fresh creation: the returned instance is
                // no longer correlated, so nothing about this knob is provable.
                return KnobCollection.Invalid;
            }

            if (assignment.Left is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == knobName &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol,
                    returnedLocal))
            {
                ApplyKnobWrite(assignment, declarationBlock, state);
            }
        }

        values.AddRange(state.Values);
        return state.IsUnknown ? KnobCollection.Unknown : KnobCollection.Provable;
    }

    /// <summary>
    /// Applies one member write of the knob to the value state: a definite simple write
    /// overwrites the set, a conditional simple write joins it, and compound writes (+=, |=, ...)
    /// are statically unknowable.
    /// </summary>
    private static void ApplyKnobWrite(
        AssignmentExpressionSyntax assignment,
        BlockSyntax? declarationBlock,
        KnobValueState state)
    {
        var definite = IsDefiniteWrite(assignment, declarationBlock);
        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            if (definite)
            {
                state.Overwrite(new List<ExpressionSyntax> { assignment.Right }, unknown: false);
            }
            else
            {
                state.Values.Add(assignment.Right);
            }
        }
        else if (definite)
        {
            state.Overwrite(new List<ExpressionSyntax>(), unknown: true);
        }
        else
        {
            state.HasUnknownCandidate = true;
        }
    }

    /// <summary>
    /// Detects ++/-- on the knob property of the traced instance anywhere in the scope. The
    /// resulting value is not statically evaluated, so such an update permanently poisons
    /// sequential proofs (sticky, like an escape — its position relative to consumption is not
    /// modeled).
    /// </summary>
    private static bool HasKnobIncrement(
        SyntaxNode scope,
        string knobName,
        ISymbol instance,
        SemanticModel semanticModel,
        OperationAnalysisContext context,
        int? localCutoffPosition = null)
    {
        foreach (var node in scope.DescendantNodes())
        {
            var operand = node switch
            {
                PostfixUnaryExpressionSyntax postfix when
                    postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                    postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
                PrefixUnaryExpressionSyntax prefix when
                    prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                    prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
                _ => null
            };

            if (operand is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.ValueText != knobName)
            {
                continue;
            }

            // Same position rules as assignments: nested-function updates poison regardless of
            // declaration position, straight-line updates after the consumption cutoff are later
            // variable reuse and are ignored.
            if (!IsInsideNestedFunction(node, scope) &&
                localCutoffPosition is { } cutoff &&
                node.SpanStart >= cutoff)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol,
                    instance))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Strips parentheses and the null-forgiving operator from an expression.</summary>
    private static ExpressionSyntax? UnwrapKnobExpression(ExpressionSyntax? expression)
    {
        while (true)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }
            else if (expression is PostfixUnaryExpressionSyntax postfix &&
                     postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                expression = postfix.Operand;
            }
            else
            {
                return expression;
            }
        }
    }

    private static bool IsInsideNestedFunction(SyntaxNode node, SyntaxNode boundary)
    {
        for (var current = node.Parent; current is not null && current != boundary; current = current.Parent)
        {
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the single invocation that initializes or is assigned to the given local/field within
    /// the containing type. Multiple candidate creations make the receiver ambiguous (untraceable).
    /// </summary>
    private static InvocationExpressionSyntax? FindSingleCreationInvocation(
        OperationAnalysisContext context,
        SyntaxNode anchor,
        ISymbol instance)
    {
        var typeDeclaration = anchor.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var semanticModel = context.Operation.SemanticModel;
        if (typeDeclaration is null || semanticModel is null)
        {
            return null;
        }

        InvocationExpressionSyntax? single = null;
        foreach (var declarator in typeDeclaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer?.Value is not InvocationExpressionSyntax invocation ||
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(declarator, context.CancellationToken), instance))
            {
                continue;
            }

            if (single is not null)
            {
                return null;
            }

            single = invocation;
        }

        foreach (var assignment in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Right is not InvocationExpressionSyntax invocation ||
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol, instance))
            {
                continue;
            }

            if (single is not null)
            {
                return null;
            }

            single = invocation;
        }

        return single;
    }

    /// <summary>
    /// Classifies the values written to a knob property on the traced options instance:
    /// any constant other than 1 proves concurrency, non-constant values are unprovable, and
    /// only all-constant-1 writes prove sequential dispatch. Untraceable sinks fall back to a
    /// containing-type scan that can only strengthen to concurrent, never prove sequential.
    /// </summary>
    private static KnobProof EvaluateTracedKnob(
        OperationAnalysisContext context,
        SyntaxNode anchor,
        OptionsTrace trace,
        string knobName)
    {
        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel is null)
        {
            return KnobProof.NotFound;
        }

        var values = new List<ExpressionSyntax>();
        var collection = KnobCollection.Provable;
        if (trace.InlineCreation is not null)
        {
            CollectInitializerValues(trace.InlineCreation, knobName, values);
        }
        else if (trace.OptionsSymbol is not null)
        {
            collection = CollectInstancePropertyValues(
                context, anchor, trace.OptionsSymbol, knobName, values, trace.LocalCutoffPosition);
        }
        else if (trace.HelperDeclaration is not null)
        {
            collection = CollectHelperKnobValues(context, trace.HelperDeclaration, knobName, values);
        }
        else if (!trace.Traced)
        {
            // Untraceable receiver: a knob constant above 1 anywhere in the type is still evidence
            // of concurrency, but nothing here can prove this particular sink sequential.
            return AnyKnobConstantAboveOneInType(context, anchor, knobName)
                ? KnobProof.ProvenConcurrent
                : KnobProof.NotFound;
        }

        if (collection == KnobCollection.Invalid)
        {
            return KnobProof.Unprovable;
        }

        if (values.Count == 0)
        {
            // Unknown with no constants means a candidate object with an unknowable default
            // exists — that must not degrade to NotFound, which some sinks treat as default.
            return collection == KnobCollection.Unknown ? KnobProof.Unprovable : KnobProof.NotFound;
        }

        var anySequential = false;
        var anyUnprovable = false;
        foreach (var value in values)
        {
            var constant = semanticModel.GetConstantValue(value, context.CancellationToken);
            if (constant.HasValue && TryGetIntegralConstant(constant.Value, out var knobValue))
            {
                if (knobValue != 1)
                {
                    // Above 1 is concurrent; below 1 (e.g. MaxDegreeOfParallelism = -1) is unlimited.
                    return KnobProof.ProvenConcurrent;
                }

                anySequential = true;
            }
            else
            {
                anyUnprovable = true;
            }
        }

        if (anyUnprovable || collection == KnobCollection.Unknown)
        {
            return KnobProof.Unprovable;
        }

        return anySequential ? KnobProof.ProvenSequential : KnobProof.NotFound;
    }

    private static void CollectInitializerValues(
        BaseObjectCreationExpressionSyntax creation,
        string propertyName,
        List<ExpressionSyntax> values)
    {
        if (creation.Initializer is null)
        {
            return;
        }

        foreach (var expression in creation.Initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax assignment &&
                assignment.Left is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == propertyName)
            {
                values.Add(assignment.Right);
            }
        }
    }

    /// <summary>
    /// Collects every value written to <paramref name="propertyName"/> on the given instance
    /// symbol within the containing type: initializer object-creation properties, assignments of
    /// fresh object creations, and member assignments whose receiver resolves to the instance.
    /// </summary>
    private static KnobCollection CollectInstancePropertyValues(
        OperationAnalysisContext context,
        SyntaxNode anchor,
        ISymbol instance,
        string propertyName,
        List<ExpressionSyntax> values,
        int? localCutoffPosition = null)
    {
        var typeDeclaration = anchor.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var semanticModel = context.Operation.SemanticModel;
        if (typeDeclaration is null || semanticModel is null)
        {
            return KnobCollection.Provable;
        }

        var state = new KnobValueState();
        BlockSyntax? declarationBlock = null;

        foreach (var declarator in typeDeclaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (!SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(declarator, context.CancellationToken), instance))
            {
                continue;
            }

            // Definite-overwrite semantics are only sound when the consumption point is known
            // (writes provably precede it). Timer proofs have no cutoff — Start() can run between
            // writes — so they keep all-writes-must-be-safe union semantics.
            if (instance is ILocalSymbol && localCutoffPosition is not null)
            {
                declarationBlock = declarator.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
            }

            if (declarator.Initializer?.Value is BaseObjectCreationExpressionSyntax creation)
            {
                var before = state.Values.Count;
                CollectInitializerValues(creation, propertyName, state.Values);
                // Default-candidate tracking only applies to consumption-cutoff traces (options
                // sinks). Timer proofs already demand that every write prove the safe state, and
                // their documented proof shape is exactly a creation without the knob followed by
                // a safe write.
                state.HasDefaultCandidate |= localCutoffPosition is not null && state.Values.Count == before;
            }
            else if (declarator.Initializer?.Value is InvocationExpressionSyntax helperInvocation &&
                     TryGetSameTreeHelperDeclaration(
                         semanticModel.GetSymbolInfo(helperInvocation, context.CancellationToken).Symbol
                             as IMethodSymbol,
                         declarator) is { } helper)
            {
                var helperResult = CollectHelperKnobValues(context, helper, propertyName, state.Values);
                if (helperResult == KnobCollection.Invalid)
                {
                    return KnobCollection.Invalid;
                }

                state.HasUnknownCandidate |= helperResult == KnobCollection.Unknown;
            }
        }

        if (HasKnobIncrement(typeDeclaration, propertyName, instance, semanticModel, context, localCutoffPosition))
        {
            state.Poisoned = true;
        }

        // For locals, any pre-consumption use other than a member-access receiver or an
        // assignment target lets the instance escape (argument, alias initializer, ref/out), and
        // the escapee can change the knob before the sink consumes the options.
        if (localCutoffPosition is { } escapeCutoff && instance is ILocalSymbol)
        {
            foreach (var reference in typeDeclaration.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (reference.SpanStart >= escapeCutoff ||
                    (reference.Parent is MemberAccessExpressionSyntax receiverUse &&
                     receiverUse.Expression == reference) ||
                    (reference.Parent is AssignmentExpressionSyntax assignmentTarget &&
                     assignmentTarget.Left == reference))
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(reference, context.CancellationToken).Symbol, instance))
                {
                    state.Poisoned = true;
                    break;
                }
            }
        }

        foreach (var assignment in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            // For locals, a write inside a nested lambda or local function executes at an
            // unknowable time — declaration position says nothing about execution order (a local
            // function declared after the creation call can run before it), so such writes are
            // never span-filtered and never overwrite: they poison sequential proofs as unknown
            // candidates when they target the traced instance.
            if (localCutoffPosition is not null && IsInsideNestedFunction(assignment, typeDeclaration))
            {
                var targetsInstance =
                    (assignment.Left is MemberAccessExpressionSyntax nestedMember &&
                     nestedMember.Name.Identifier.ValueText == propertyName &&
                     SymbolEqualityComparer.Default.Equals(
                         semanticModel.GetSymbolInfo(nestedMember.Expression, context.CancellationToken).Symbol,
                         instance)) ||
                    SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol, instance);
                if (targetsInstance)
                {
                    state.Poisoned = true;
                }

                continue;
            }

            // Writes and reassignments after the sink consumed the options belong to later reuse
            // of the variable — the SDK snapshotted the values at the creation call.
            if (localCutoffPosition is { } cutoff && assignment.SpanStart >= cutoff)
            {
                continue;
            }

            if (assignment.Left is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == propertyName &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol,
                    instance))
            {
                ApplyKnobWrite(assignment, declarationBlock, state);
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol, instance))
            {
                continue;
            }

            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Right is BaseObjectCreationExpressionSyntax creation)
            {
                var replacementValues = new List<ExpressionSyntax>();
                CollectInitializerValues(creation, propertyName, replacementValues);
                if (IsDefiniteWrite(assignment, declarationBlock))
                {
                    // A straight-line replacement discards the previous object entirely; a
                    // replacement that never sets the knob leaves an unknowable default.
                    state.Overwrite(replacementValues, unknown: replacementValues.Count == 0);
                }
                else
                {
                    // A conditional replacement joins the union of candidate objects.
                    state.Values.AddRange(replacementValues);
                    state.HasUnknownCandidate |= replacementValues.Count == 0;
                }
            }
            else if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                     assignment.Right is InvocationExpressionSyntax replacementInvocation &&
                     TryGetSameTreeHelperDeclaration(
                         semanticModel.GetSymbolInfo(replacementInvocation, context.CancellationToken).Symbol
                             as IMethodSymbol,
                         assignment) is { } replacementHelper)
            {
                var replacementValues = new List<ExpressionSyntax>();
                var helperResult = CollectHelperKnobValues(
                    context, replacementHelper, propertyName, replacementValues);
                if (helperResult == KnobCollection.Invalid)
                {
                    return KnobCollection.Invalid;
                }

                var unknown = helperResult == KnobCollection.Unknown || replacementValues.Count == 0;
                if (IsDefiniteWrite(assignment, declarationBlock))
                {
                    state.Overwrite(replacementValues, unknown);
                }
                else
                {
                    state.Values.AddRange(replacementValues);
                    state.HasUnknownCandidate |= unknown;
                }
            }
            else
            {
                // Reassigned to something that is neither a fresh creation nor a provable helper:
                // the instance that reaches the sink is no longer the one whose writes were
                // collected, so nothing about this knob is provable.
                return KnobCollection.Invalid;
            }
        }

        values.AddRange(state.Values);
        return state.IsUnknown ? KnobCollection.Unknown : KnobCollection.Provable;
    }

    private static bool AnyKnobConstantAboveOneInType(
        OperationAnalysisContext context,
        SyntaxNode anchor,
        string knobName)
    {
        var typeDeclaration = anchor.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var semanticModel = context.Operation.SemanticModel;
        if (typeDeclaration is null || semanticModel is null)
        {
            return false;
        }

        foreach (var assignment in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var name = assignment.Left switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null
            };
            if (name != knobName)
            {
                continue;
            }

            var constant = semanticModel.GetConstantValue(assignment.Right, context.CancellationToken);
            if (constant.HasValue && TryGetIntegralConstant(constant.Value, out var knobValue) && knobValue > 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes an integral constant to long regardless of its declared type. Knob properties
    /// vary across SDKs (ServiceBus options use int, RabbitMQ.Client v7 declares
    /// ConsumerDispatchConcurrency as ushort), so a proven constant must count whichever
    /// integral type carries it.
    /// </summary>
    private static bool TryGetIntegralConstant(object? value, out long result)
    {
        switch (value)
        {
            case sbyte v: result = v; return true;
            case byte v: result = v; return true;
            case short v: result = v; return true;
            case ushort v: result = v; return true;
            case int v: result = v; return true;
            case uint v: result = v; return true;
            case long v: result = v; return true;
            default: result = 0; return false;
        }
    }

    /// <summary>
    /// A System.Timers.Timer is sequential only when THIS timer instance provably has
    /// AutoReset = false or a non-null SynchronizingObject. Assigning null (or an unprovable
    /// expression shape) to SynchronizingObject keeps callbacks on the thread pool where they
    /// can overlap, so it is not accepted as proof.
    /// </summary>
    private static bool IsTimersTimerSequential(
        OperationAnalysisContext context,
        SyntaxNode anchor,
        IEventReferenceOperation eventReference)
    {
        var receiver = eventReference.Instance is null ? null : Unwrap(eventReference.Instance);
        var instance = receiver switch
        {
            ILocalReferenceOperation local => (ISymbol)local.Local,
            IFieldReferenceOperation field => field.Field,
            _ => null
        };
        if (instance is null)
        {
            return false;
        }

        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel is null)
        {
            return false;
        }

        // Every write must prove the safe state: a later AutoReset = true (or an unprovable
        // value) re-enables overlapping callbacks, so one safe write is not sufficient proof.
        // An Unknown/Invalid collection (compound write, escape, opaque reassignment) means some
        // candidate value is not statically known and likewise voids the proof.
        var autoResetValues = new List<ExpressionSyntax>();
        var autoResetCollection = CollectInstancePropertyValues(
            context, anchor, instance, "AutoReset", autoResetValues);
        if (autoResetCollection == KnobCollection.Provable &&
            autoResetValues.Count > 0 &&
            autoResetValues.All(value =>
                semanticModel.GetConstantValue(value, context.CancellationToken)
                    is { HasValue: true, Value: false }))
        {
            return true;
        }

        var synchronizingValues = new List<ExpressionSyntax>();
        var synchronizingCollection = CollectInstancePropertyValues(
            context, anchor, instance, "SynchronizingObject", synchronizingValues);
        if (synchronizingCollection == KnobCollection.Provable &&
            synchronizingValues.Count > 0 &&
            synchronizingValues.All(IsProvablyNonNull))
        {
            return true;
        }

        return false;
    }

    private static bool IsProvablyNonNull(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized.Expression;
        }

        return current is ObjectCreationExpressionSyntax or ThisExpressionSyntax;
    }

    /// <summary>
    /// True when a finite Change(...) is invoked on the timer instance this creation is assigned
    /// to. The receiver must resolve to the same local/field symbol — a different timer being
    /// started in the same type must not make this dormant timer a sink.
    /// </summary>
    private static bool TimerInstanceIsStartedByChange(
        OperationAnalysisContext context,
        IObjectCreationOperation creation)
    {
        var timerSymbol = GetCreationTargetSymbol(creation);
        if (timerSymbol is null)
        {
            return false;
        }

        var typeDeclaration = creation.Syntax.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var semanticModel = context.Operation.SemanticModel;
        if (typeDeclaration is null || semanticModel is null)
        {
            return false;
        }

        foreach (var invocation in typeDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.ValueText != "Change")
            {
                continue;
            }

            if (semanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                    is not IMethodSymbol method ||
                method.ContainingType?.ToDisplayString() != "System.Threading.Timer")
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol,
                    timerSymbol))
            {
                continue;
            }

            if (semanticModel.GetOperation(invocation, context.CancellationToken)
                    is not IInvocationOperation changeOperation)
            {
                continue;
            }

            var period = changeOperation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "period");
            if (period is not null && !IsProvablyNonRecurringPeriod(period.Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the local or field symbol the object creation is directly assigned to
    /// (declaration initializer or simple assignment), if any.
    /// </summary>
    private static ISymbol? GetCreationTargetSymbol(IObjectCreationOperation creation)
    {
        var parent = creation.Parent;
        while (parent is IConversionOperation)
        {
            parent = parent.Parent;
        }

        return parent switch
        {
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } =>
                declarator.Symbol,
            ISimpleAssignmentOperation assignment => Unwrap(assignment.Target) switch
            {
                IFieldReferenceOperation field => (ISymbol)field.Field,
                ILocalReferenceOperation local => local.Local,
                _ => null
            },
            _ => null
        };
    }

    private static bool IsProvablyInfinitePeriod(IOperation operation)
    {
        var value = Unwrap(operation);

        if (value.ConstantValue.HasValue)
        {
            return value.ConstantValue.Value switch
            {
                int i => i == -1,
                long l => l == -1,
                uint u => u == uint.MaxValue,
                _ => false
            };
        }

        if (value is IFieldReferenceOperation fieldReference &&
            fieldReference.Field.Name is "Infinite" or "InfiniteTimeSpan" &&
            fieldReference.Field.ContainingType?.ToDisplayString() == "System.Threading.Timeout")
        {
            return true;
        }

        if (value is IInvocationOperation { TargetMethod.Name: "FromMilliseconds" } fromMilliseconds &&
            fromMilliseconds.TargetMethod.ContainingType?.ToDisplayString() == "System.TimeSpan" &&
            fromMilliseconds.Arguments.Length == 1)
        {
            var argument = Unwrap(fromMilliseconds.Arguments[0].Value);
            return argument.ConstantValue is { HasValue: true, Value: double d } && d == -1;
        }

        return false;
    }

    /// <summary>
    /// A timer period of zero is non-recurring just like an infinite one: the callback fires at
    /// most once, so invocations cannot overlap.
    /// </summary>
    private static bool IsProvablyNonRecurringPeriod(IOperation operation)
    {
        if (IsProvablyInfinitePeriod(operation))
        {
            return true;
        }

        var value = Unwrap(operation);
        if (value.ConstantValue.HasValue)
        {
            return value.ConstantValue.Value switch
            {
                int i => i == 0,
                long l => l == 0,
                uint u => u == 0,
                _ => false
            };
        }

        if (value is IFieldReferenceOperation { Field.Name: "Zero" } zeroField &&
            zeroField.Field.ContainingType?.ToDisplayString() == "System.TimeSpan")
        {
            return true;
        }

        if (value is IInvocationOperation { TargetMethod.Name: "FromMilliseconds" } fromMilliseconds &&
            fromMilliseconds.TargetMethod.ContainingType?.ToDisplayString() == "System.TimeSpan" &&
            fromMilliseconds.Arguments.Length == 1)
        {
            var argument = Unwrap(fromMilliseconds.Arguments[0].Value);
            return argument.ConstantValue is { HasValue: true, Value: double d } && d == 0;
        }

        return false;
    }

    // ---------------------------------------------------------------------
    // Handler resolution
    // ---------------------------------------------------------------------

    private static void AnalyzeHandlerValue(
        OperationAnalysisContext context,
        IOperation handlerValue,
        SinkContext sink,
        SyntaxNode registrationSyntax)
    {
        if (sink.Concurrency == SinkConcurrency.Sequential)
        {
            return;
        }

        var value = Unwrap(handlerValue);
        var target = value is IDelegateCreationOperation delegateCreation
            ? Unwrap(delegateCreation.Target)
            : value;

        switch (target)
        {
            case IAnonymousFunctionOperation lambda:
                AnalyzeHandlerBody(
                    context, lambda.Body, lambda.Syntax, lambda.Symbol, sink, registrationSyntax);

                // Thin delegation lambda (`args => HandleAsync(args)`): real handler logic lives
                // in the same-type instance method, so analyze that body as well.
                if (TryGetOneHopTarget(lambda, out var delegated))
                {
                    AnalyzeMethodHandler(context, delegated, sink, registrationSyntax);
                }

                break;

            case IMethodReferenceOperation methodReference:
                AnalyzeMethodHandler(context, methodReference.Method, sink, registrationSyntax);
                break;
        }
    }

    private static bool TryGetOneHopTarget(IAnonymousFunctionOperation lambda, out IMethodSymbol target)
    {
        target = null!;
        if (lambda.Body.Operations.Length != 1)
        {
            return false;
        }

        var statement = lambda.Body.Operations[0];
        var expression = statement switch
        {
            IReturnOperation { ReturnedValue: { } returned } => Unwrap(returned),
            IExpressionStatementOperation expressionStatement => Unwrap(expressionStatement.Operation),
            _ => null
        };
        if (expression is IAwaitOperation awaitOperation)
        {
            expression = Unwrap(awaitOperation.Operation);
        }

        if (expression is not IInvocationOperation invocation)
        {
            return false;
        }

        var method = invocation.TargetMethod;
        var isOnThis = invocation.Instance is null or IInstanceReferenceOperation;
        if (!isOnThis)
        {
            return false;
        }

        target = method;
        return true;
    }

    private static void AnalyzeMethodHandler(
        OperationAnalysisContext context,
        IMethodSymbol method,
        SinkContext sink,
        SyntaxNode registrationSyntax)
    {
        var enclosingType = GetEnclosingNamedType(context, registrationSyntax);
        if (enclosingType is null ||
            !SymbolEqualityComparer.Default.Equals(method.ContainingType, enclosingType))
        {
            return;
        }

        var declaration = method.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaration is null || declaration.SyntaxTree != registrationSyntax.SyntaxTree)
        {
            // Cross-tree bodies would need a foreign semantic model (RS1030); bail silently.
            return;
        }

        var declarationSyntax = declaration.GetSyntax(context.CancellationToken);
        var semanticModel = context.Operation.SemanticModel;
        var body = semanticModel?.GetOperation(declarationSyntax, context.CancellationToken) switch
        {
            IMethodBodyBaseOperation methodBody => methodBody.BlockBody ?? methodBody.ExpressionBody,
            // Handlers wired to local functions (`processor.ProcessMessageAsync += HandleAsync;`
            // with HandleAsync declared inside the method) surface as ILocalFunctionOperation.
            ILocalFunctionOperation localFunction => localFunction.Body ?? localFunction.IgnoredBody,
            _ => null
        };
        if (body is null)
        {
            return;
        }

        AnalyzeHandlerBody(context, body, declarationSyntax, method, sink, registrationSyntax);
    }

    private static INamedTypeSymbol? GetEnclosingNamedType(
        OperationAnalysisContext context,
        SyntaxNode node)
    {
        var typeDeclaration = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDeclaration is null)
        {
            return null;
        }

        return context.Operation.SemanticModel?.GetDeclaredSymbol(typeDeclaration, context.CancellationToken)
            as INamedTypeSymbol;
    }

    // ---------------------------------------------------------------------
    // Handler body analysis
    // ---------------------------------------------------------------------

    private static void AnalyzeHandlerBody(
        OperationAnalysisContext context,
        IBlockOperation body,
        SyntaxNode handlerBoundary,
        IMethodSymbol handlerSymbol,
        SinkContext sink,
        SyntaxNode registrationSyntax)
    {
        var operations = new List<IOperation>();
        CollectSameBoundaryOperations(body, operations);

        var guards = EvaluateHandlerGuards(operations, handlerBoundary, sink.TimerInstance);
        if (guards.HandlerSerialized)
        {
            return;
        }

        var writtenSymbols = CollectWrittenSymbols(operations);

        // symbol -> (catalog display name, use type, ordered use syntax nodes)
        var directCandidates = new Dictionary<ISymbol, DirectCandidate>(SymbolEqualityComparer.Default);
        foreach (var operation in operations)
        {
            var (symbol, type) = operation switch
            {
                IFieldReferenceOperation { Instance: null or IInstanceReferenceOperation } field =>
                    ((ISymbol)field.Field, field.Field.Type),
                IPropertyReferenceOperation { Instance: null or IInstanceReferenceOperation } property =>
                    ((ISymbol)property.Property, property.Property.Type),
                ILocalReferenceOperation local when IsDeclaredOutside(local.Local, handlerBoundary) =>
                    ((ISymbol)local.Local, local.Local.Type),
                IParameterReferenceOperation parameter when
                    !SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, handlerSymbol) =>
                    ((ISymbol)parameter.Parameter, parameter.Parameter.Type),
                _ => (null, null)
            };

            if (symbol is null || type is null || IsAlwaysSafeCapture(type) || writtenSymbols.Contains(symbol))
            {
                continue;
            }

            var catalogName = MatchNonThreadSafeCatalog(type);
            if (catalogName is null)
            {
                continue;
            }

            if (IsDisposeOnlyUse(operation.Syntax) ||
                IsInsideLock(context, operation.Syntax, handlerBoundary) ||
                guards.Covers(operation.Syntax))
            {
                continue;
            }

            if (!directCandidates.TryGetValue(symbol, out var candidate))
            {
                candidate = new DirectCandidate(catalogName, type);
                directCandidates[symbol] = candidate;
            }

            candidate.Uses.Add(operation.Syntax);
        }

        foreach (var pair in directCandidates)
        {
            ReportCandidate(
                context, sink, registrationSyntax,
                symbolName: pair.Key.Name,
                serviceType: pair.Value.UseType,
                catalogName: pair.Value.CatalogName,
                captureKind: pair.Key.Kind.ToString(),
                handlerIsAsync: handlerSymbol.IsAsync,
                handlerReturnsAwaitable: ReturnsAwaitable(handlerSymbol),
                captureSite: pair.Key.DeclaringSyntaxReferences.FirstOrDefault(),
                uses: pair.Value.Uses);
        }

        AnalyzeCapturedScopeResolutions(
            context, operations, handlerBoundary, handlerSymbol, sink, registrationSyntax, guards);
    }

    private sealed class DirectCandidate
    {
        public DirectCandidate(string catalogName, ITypeSymbol useType)
        {
            CatalogName = catalogName;
            UseType = useType;
        }

        public string CatalogName { get; }

        public ITypeSymbol UseType { get; }

        public List<SyntaxNode> Uses { get; } = new();
    }

    /// <summary>
    /// Resolving from a scope or provider captured from outside the handler hands the same
    /// instance to every concurrent invocation. Only scopes created inside the handler are safe.
    /// </summary>
    private static void AnalyzeCapturedScopeResolutions(
        OperationAnalysisContext context,
        List<IOperation> operations,
        SyntaxNode handlerBoundary,
        IMethodSymbol handlerSymbol,
        SinkContext sink,
        SyntaxNode registrationSyntax,
        HandlerGuards guards)
    {
        var reportedTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (operation is not IInvocationOperation invocation ||
                invocation.TargetMethod.Name is not
                    ("GetService" or "GetRequiredService" or "GetKeyedService" or "GetRequiredKeyedService") ||
                invocation.TargetMethod.TypeArguments.Length != 1)
            {
                continue;
            }

            var resolvedType = invocation.TargetMethod.TypeArguments[0];
            var catalogName = MatchNonThreadSafeCatalog(resolvedType);
            if (catalogName is null)
            {
                continue;
            }

            var receiver = invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value;
            if (receiver is null)
            {
                continue;
            }

            var origin = ResolveProviderOrigin(Unwrap(receiver));
            if (origin is null || !IsProviderLike(GetSymbolType(origin)))
            {
                continue;
            }

            var isShared = origin switch
            {
                IFieldSymbol => true,
                ILocalSymbol local => IsDeclaredOutside(local, handlerBoundary),
                IParameterSymbol parameter =>
                    !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, handlerSymbol),
                _ => false
            };
            if (!isShared)
            {
                continue;
            }

            if (IsInsideLock(context, invocation.Syntax, handlerBoundary) || guards.Covers(invocation.Syntax))
            {
                continue;
            }

            if (!reportedTypes.Add(resolvedType.ToDisplayString()))
            {
                continue;
            }

            ReportCandidate(
                context, sink, registrationSyntax,
                symbolName: resolvedType.Name,
                serviceType: resolvedType,
                catalogName: catalogName,
                captureKind: "ScopeResolution",
                handlerIsAsync: handlerSymbol.IsAsync,
                handlerReturnsAwaitable: ReturnsAwaitable(handlerSymbol),
                captureSite: origin.DeclaringSyntaxReferences.FirstOrDefault(),
                uses: new List<SyntaxNode> { invocation.Syntax });
        }
    }

    private static ISymbol? ResolveProviderOrigin(IOperation receiver)
    {
        var current = receiver;
        while (true)
        {
            current = Unwrap(current);
            if (current is IPropertyReferenceOperation { Property.Name: "ServiceProvider" } providerProperty &&
                providerProperty.Instance is not null)
            {
                current = providerProperty.Instance;
                continue;
            }

            return current switch
            {
                ILocalReferenceOperation local => local.Local,
                IFieldReferenceOperation field => field.Field,
                IParameterReferenceOperation parameter => parameter.Parameter,
                _ => null
            };
        }
    }

    /// <summary>
    /// True when the handler returns Task/ValueTask (including generic forms). A non-async
    /// handler with an awaitable return type cannot host a synchronous using-scope safely: the
    /// scope would dispose before the returned task completes.
    /// </summary>
    private static bool ReturnsAwaitable(IMethodSymbol handler)
    {
        return handler.ReturnType is INamedTypeSymbol { Name: "Task" or "ValueTask" } returnType &&
               returnType.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";
    }

    private static ITypeSymbol? GetSymbolType(ISymbol symbol) => symbol switch
    {
        IFieldSymbol field => field.Type,
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        _ => null
    };

    private static void ReportCandidate(
        OperationAnalysisContext context,
        SinkContext sink,
        SyntaxNode registrationSyntax,
        string symbolName,
        ITypeSymbol serviceType,
        string catalogName,
        string captureKind,
        bool handlerIsAsync,
        bool handlerReturnsAwaitable,
        SyntaxReference? captureSite,
        List<SyntaxNode> uses)
    {
        if (uses.Count == 0)
        {
            return;
        }

        uses.Sort((a, b) => a.SpanStart.CompareTo(b.SpanStart));
        var primary = uses[0].GetLocation();

        var additionalLocations = new List<Location>();
        if (captureSite is not null && captureSite.SyntaxTree == registrationSyntax.SyntaxTree)
        {
            additionalLocations.Add(captureSite.GetSyntax(context.CancellationToken).GetLocation());
        }
        else
        {
            additionalLocations.Add(primary);
        }

        additionalLocations.Add(registrationSyntax.GetLocation());
        for (var i = 1; i < uses.Count; i++)
        {
            additionalLocations.Add(uses[i].GetLocation());
        }

        var properties = ImmutableDictionary.CreateBuilder<string, string?>();
        properties.Add("SymbolName", symbolName);
        properties.Add("ServiceTypeName", serviceType.ToDisplayString());
        properties.Add("CaptureKind", captureKind);
        properties.Add("HandlerIsAsync", handlerIsAsync ? "true" : "false");
        properties.Add("HandlerReturnsAwaitable", handlerReturnsAwaitable ? "true" : "false");
        properties.Add("SinkKind", sink.SinkDisplay);

        var diagnostic = sink.Concurrency == SinkConcurrency.Concurrent
            ? Diagnostic.Create(
                DiagnosticDescriptors.ConcurrentHandlerSharedState,
                primary,
                additionalLocations,
                properties.ToImmutable(),
                symbolName,
                sink.Description,
                catalogName)
            : Diagnostic.Create(
                DiagnosticDescriptors.ConcurrentHandlerConfigGatedSharedState,
                primary,
                additionalLocations,
                properties.ToImmutable(),
                symbolName,
                sink.SinkDisplay,
                sink.KnobName);
        context.ReportDiagnostic(diagnostic);
    }

    // ---------------------------------------------------------------------
    // Capture classification helpers
    // ---------------------------------------------------------------------

    private static void CollectSameBoundaryOperations(IOperation root, List<IOperation> operations)
    {
        foreach (var child in root.ChildOperations)
        {
            if (child is IAnonymousFunctionOperation or ILocalFunctionOperation or IDelegateCreationOperation)
            {
                // Nested delegates get their own sink evaluation; descending here would
                // misattribute their captures to the outer handler.
                continue;
            }

            operations.Add(child);
            CollectSameBoundaryOperations(child, operations);
        }
    }

    private static HashSet<ISymbol> CollectWrittenSymbols(List<IOperation> operations)
    {
        var written = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var operation in operations)
        {
            var target = operation switch
            {
                IAssignmentOperation assignment => assignment.Target,
                IIncrementOrDecrementOperation increment => increment.Target,
                IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => argument.Value,
                _ => null
            };
            if (target is null)
            {
                continue;
            }

            var symbol = Unwrap(target) switch
            {
                IFieldReferenceOperation field => (ISymbol)field.Field,
                ILocalReferenceOperation local => local.Local,
                IParameterReferenceOperation parameter => parameter.Parameter,
                IPropertyReferenceOperation property => property.Property,
                _ => null
            };
            if (symbol is not null)
            {
                written.Add(symbol);
            }
        }

        return written;
    }

    private static bool IsDeclaredOutside(ISymbol symbol, SyntaxNode handlerBoundary)
    {
        var declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaration is null)
        {
            return false;
        }

        return declaration.SyntaxTree != handlerBoundary.SyntaxTree ||
               !handlerBoundary.FullSpan.Contains(declaration.Span);
    }

    private static bool IsDisposeOnlyUse(SyntaxNode useSyntax)
    {
        return useSyntax.Parent is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Expression == useSyntax &&
               memberAccess.Name.Identifier.ValueText is "Dispose" or "DisposeAsync" &&
               memberAccess.Parent is InvocationExpressionSyntax;
    }

    /// <summary>
    /// A lock only serializes concurrent invocations when the monitor object is shared across
    /// them. Locking an object created inside the handler gives every invocation its own monitor
    /// and guards nothing.
    /// </summary>
    private static bool IsInsideLock(
        OperationAnalysisContext context,
        SyntaxNode useSyntax,
        SyntaxNode handlerBoundary)
    {
        var semanticModel = context.Operation.SemanticModel;
        for (var node = useSyntax.Parent; node is not null && node != handlerBoundary; node = node.Parent)
        {
            if (node is not LockStatementSyntax lockStatement || semanticModel is null)
            {
                continue;
            }

            var lockTarget = semanticModel.GetSymbolInfo(
                lockStatement.Expression, context.CancellationToken).Symbol;
            if (lockTarget is ILocalSymbol local && !IsDeclaredOutside(local, handlerBoundary))
            {
                // Per-invocation monitor: keep looking for an outer, genuinely shared lock.
                continue;
            }

            if (lockTarget is IFieldSymbol or IParameterSymbol or ILocalSymbol)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------
    // Serialization guards: handlers that explicitly serialize their own execution
    // ---------------------------------------------------------------------

    private readonly struct HandlerGuards
    {
        public HandlerGuards(bool handlerSerialized, List<Microsoft.CodeAnalysis.Text.TextSpan>? semaphoreGuardedSpans)
        {
            HandlerSerialized = handlerSerialized;
            SemaphoreGuardedSpans = semaphoreGuardedSpans;
        }

        /// <summary>The whole handler provably runs one-at-a-time (reentrancy guard, re-arm, async lock).</summary>
        public bool HandlerSerialized { get; }

        /// <summary>Regions bracketed by a SemaphoreSlim wait + finally-release on the same semaphore.</summary>
        public List<Microsoft.CodeAnalysis.Text.TextSpan>? SemaphoreGuardedSpans { get; }

        public bool Covers(SyntaxNode use)
        {
            if (SemaphoreGuardedSpans is null)
            {
                return false;
            }

            foreach (var span in SemaphoreGuardedSpans)
            {
                if (span.Contains(use.Span))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static HandlerGuards EvaluateHandlerGuards(
        List<IOperation> operations,
        SyntaxNode handlerBoundary,
        ISymbol? sinkTimerInstance)
    {
        List<(ISymbol Semaphore, IInvocationOperation Wait)>? waits = null;
        List<(ISymbol Semaphore, IInvocationOperation Release)>? releases = null;
        List<Microsoft.CodeAnalysis.Text.TextSpan>? guardedSpans = null;

        foreach (var operation in operations)
        {
            if (operation is not IInvocationOperation invocation)
            {
                if (operation is IAwaitOperation awaitOperation &&
                    TryGetAsyncLockGuardSpan(awaitOperation, handlerBoundary, out var asyncLockSpan))
                {
                    (guardedSpans ??= new()).Add(asyncLockSpan);
                }

                continue;
            }

            var method = invocation.TargetMethod;
            var containingType = method.ContainingType?.ToDisplayString();
            switch (containingType)
            {
                case "System.Threading.SemaphoreSlim" when method.Name is "Wait" or "WaitAsync":
                {
                    // A semaphore created inside the handler is per-invocation and guards nothing.
                    var waitedSemaphore = GetInstanceReferenceSymbol(invocation.Instance);
                    if (waitedSemaphore is not null &&
                        (waitedSemaphore is not ILocalSymbol waitedLocal ||
                         IsDeclaredOutside(waitedLocal, handlerBoundary)))
                    {
                        (waits ??= new()).Add((waitedSemaphore, invocation));
                    }

                    break;
                }

                case "System.Threading.SemaphoreSlim" when method.Name == "Release":
                    if (GetInstanceReferenceSymbol(invocation.Instance) is { } releasedSemaphore &&
                        HasAncestorWithinBoundary<FinallyClauseSyntax>(invocation.Syntax, handlerBoundary))
                    {
                        (releases ??= new()).Add((releasedSemaphore, invocation));
                    }

                    break;

                case "System.Threading.Interlocked" when method.Name == "CompareExchange":
                case "System.Threading.Monitor" when method.Name == "TryEnter":
                    // The reentrancy guard only protects what executes after it; an early-return
                    // check that is itself a top-level statement of the handler dominates the
                    // rest of the body, so guard from its end to the end of the handler.
                    if (IsEarlyReturnGuard(invocation.Syntax, handlerBoundary, out var guardStatement))
                    {
                        (guardedSpans ??= new()).Add(
                            Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                                guardStatement.Span.End, handlerBoundary.Span.End));
                    }

                    break;

                case "System.Threading.Timer" when method.Name == "Change":
                    // Re-arm pattern: the handler stops ITS OWN timer before touching state.
                    // Only the sink's timer counts, and only uses after the stop are protected.
                    if (sinkTimerInstance is not null &&
                        SymbolEqualityComparer.Default.Equals(
                            GetInstanceReferenceSymbol(invocation.Instance), sinkTimerInstance) &&
                        invocation.Arguments.Any(a => IsProvablyInfinitePeriod(a.Value)))
                    {
                        (guardedSpans ??= new()).Add(
                            Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                                invocation.Syntax.Span.End, handlerBoundary.Span.End));
                    }

                    break;
            }
        }

        // A semaphore bracket only guards the try region whose finally releases the SAME
        // semaphore that was awaited before (or inside, prior to the guarded uses of) the try.
        if (waits is not null && releases is not null)
        {
            foreach (var (semaphore, release) in releases)
            {
                var finallyClause = release.Syntax.Ancestors().OfType<FinallyClauseSyntax>().FirstOrDefault();
                if (finallyClause?.Parent is not TryStatementSyntax tryStatement)
                {
                    continue;
                }

                foreach (var (waitedSemaphore, wait) in waits)
                {
                    if (!SymbolEqualityComparer.Default.Equals(semaphore, waitedSemaphore))
                    {
                        continue;
                    }

                    if (wait.Syntax.Span.End <= tryStatement.SpanStart)
                    {
                        (guardedSpans ??= new()).Add(tryStatement.Block.Span);
                        break;
                    }

                    if (tryStatement.Block.Span.Contains(wait.Syntax.Span))
                    {
                        // Wait inside the try: only uses after the wait are protected.
                        (guardedSpans ??= new()).Add(
                            Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                                wait.Syntax.Span.End, tryStatement.Block.Span.End));
                        break;
                    }
                }
            }
        }

        return new HandlerGuards(handlerSerialized: false, guardedSpans);
    }

    private static ISymbol? GetInstanceReferenceSymbol(IOperation? instance)
    {
        if (instance is null)
        {
            return null;
        }

        return Unwrap(instance) switch
        {
            IFieldReferenceOperation field => (ISymbol)field.Field,
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null
        };
    }

    private static bool IsEarlyReturnGuard(
        SyntaxNode invocationSyntax,
        SyntaxNode handlerBoundary,
        out IfStatementSyntax guardStatement)
    {
        guardStatement = null!;
        for (var node = invocationSyntax.Parent; node is not null && node != handlerBoundary; node = node.Parent)
        {
            if (node is not IfStatementSyntax ifStatement ||
                !ifStatement.Condition.Span.Contains(invocationSyntax.Span))
            {
                continue;
            }

            if (!ifStatement.Statement.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any())
            {
                return false;
            }

            // Only a guard that is a direct top-level statement of the handler body dominates
            // everything after it; a guard nested in another branch protects only that branch.
            var container = ifStatement.Parent;
            if (container is not BlockSyntax block || !IsHandlerBodyBlock(block, handlerBoundary))
            {
                return false;
            }

            guardStatement = ifStatement;
            return true;
        }

        return false;
    }

    private static bool IsHandlerBodyBlock(BlockSyntax block, SyntaxNode handlerBoundary)
    {
        return handlerBoundary switch
        {
            MethodDeclarationSyntax method => method.Body == block,
            LocalFunctionStatementSyntax localFunction => localFunction.Body == block,
            AnonymousFunctionExpressionSyntax anonymous => anonymous.Body == block,
            _ => false
        };
    }

    /// <summary>
    /// Disposable async-lock idiom: using (await _mutex.LockAsync()) { ... }. Crude name-based
    /// heuristic by design, but it only guards the using region (not the whole handler), and the
    /// lock object must be shared — an async lock created inside the handler guards nothing.
    /// </summary>
    private static bool TryGetAsyncLockGuardSpan(
        IAwaitOperation awaitOperation,
        SyntaxNode handlerBoundary,
        out Microsoft.CodeAnalysis.Text.TextSpan guardedSpan)
    {
        guardedSpan = default;
        if (Unwrap(awaitOperation.Operation) is not IInvocationOperation invocation)
        {
            return false;
        }

        var receiverTypeName =
            (invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value)?.Type?.Name ??
            invocation.TargetMethod.ContainingType?.Name;
        if (receiverTypeName is null ||
            (!receiverTypeName.Contains("Lock") && !receiverTypeName.Contains("Mutex")))
        {
            return false;
        }

        // The lock instance must come from outside the handler to serialize invocations.
        var lockInstance = GetInstanceReferenceSymbol(invocation.Instance);
        if (lockInstance is ILocalSymbol local && !IsDeclaredOutside(local, handlerBoundary))
        {
            return false;
        }

        for (var node = awaitOperation.Syntax.Parent; node is not null && node != handlerBoundary; node = node.Parent)
        {
            if (node is UsingStatementSyntax usingStatement)
            {
                guardedSpan = usingStatement.Statement.Span;
                return true;
            }

            if (node is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 } usingDeclaration &&
                usingDeclaration.Parent is BlockSyntax enclosingBlock)
            {
                // A using declaration holds the lock until the end of its enclosing block.
                guardedSpan = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                    usingDeclaration.Span.End, enclosingBlock.Span.End);
                return true;
            }
        }

        return false;
    }

    private static bool HasAncestorWithinBoundary<TSyntax>(SyntaxNode node, SyntaxNode handlerBoundary)
        where TSyntax : SyntaxNode
    {
        for (var current = node.Parent; current is not null && current != handlerBoundary; current = current.Parent)
        {
            if (current is TSyntax)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------
    // Type catalogs
    // ---------------------------------------------------------------------

    /// <summary>
    /// Documented non-thread-safe types whose shared use across concurrent invocations is a
    /// runtime defect. Matching is by fully-qualified name through the base-type chain and
    /// implemented interfaces, so source stubs and real package references behave identically.
    /// </summary>
    private static string? MatchNonThreadSafeCatalog(ITypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            var match = MatchCatalogEntry(current);
            if (match is not null)
            {
                return match;
            }
        }

        foreach (var implemented in type.AllInterfaces)
        {
            var match = MatchCatalogEntry(implemented);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string? MatchCatalogEntry(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        return (ns, type.Name) switch
        {
            ("Microsoft.EntityFrameworkCore", "DbContext") => "DbContext",
            ("Microsoft.EntityFrameworkCore.Storage", "IDbContextTransaction") => "IDbContextTransaction",
            ("System.Data.Common", "DbConnection") => "DbConnection",
            ("System.Data.Common", "DbCommand") => "DbCommand",
            ("System.Data.Common", "DbTransaction") => "DbTransaction",
            ("System.Data.Common", "DbDataReader") => "DbDataReader",
            ("System.Data", "IDbConnection") => "IDbConnection",
            ("System.Data", "IDbCommand") => "IDbCommand",
            ("System.Data", "IDbTransaction") => "IDbTransaction",
            ("System.Data", "IDataReader") => "IDataReader",
            ("Microsoft.AspNetCore.Http", "HttpContext") => "HttpContext",
            _ => null
        };
    }

    /// <summary>
    /// Types that are always safe to capture into a concurrent handler; they are the recommended
    /// fix patterns, so they must never re-warn.
    /// </summary>
    private static bool IsAlwaysSafeCapture(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        return (ns, type.Name) switch
        {
            ("Microsoft.EntityFrameworkCore", "IDbContextFactory") => true,
            ("Microsoft.Extensions.DependencyInjection", "IServiceScopeFactory") => true,
            ("Microsoft.Extensions.Logging", "ILogger") => true,
            ("Microsoft.Extensions.Options", "IOptions") => true,
            ("Microsoft.Extensions.Options", "IOptionsMonitor") => true,
            ("Microsoft.Extensions.Options", "IOptionsSnapshot") => true,
            ("Microsoft.AspNetCore.Http", "IHttpContextAccessor") => true,
            _ => false
        };
    }

    private static bool IsProviderLike(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        var ns = type.ContainingNamespace?.ToDisplayString();
        if ((ns, type.Name) is
            ("System", "IServiceProvider") or
            ("Microsoft.Extensions.DependencyInjection", "IServiceScope") or
            ("Microsoft.Extensions.DependencyInjection", "AsyncServiceScope") or
            ("Microsoft.Extensions.DependencyInjection", "IKeyedServiceProvider"))
        {
            return true;
        }

        return type.AllInterfaces.Any(i =>
            i.Name == "IServiceProvider" && i.ContainingNamespace?.ToDisplayString() == "System");
    }

    private static IOperation Unwrap(IOperation operation)
    {
        var current = operation;
        while (true)
        {
            switch (current)
            {
                case IConversionOperation conversion:
                    current = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    current = parenthesized.Operand;
                    continue;
                default:
                    return current;
            }
        }
    }
}
