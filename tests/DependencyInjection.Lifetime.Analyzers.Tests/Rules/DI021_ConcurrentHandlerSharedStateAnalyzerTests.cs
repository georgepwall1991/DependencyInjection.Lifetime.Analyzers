using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI021_ConcurrentHandlerSharedStateAnalyzerTests
{
    private const string BaseUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    private const string ServiceBusUsing = "using Azure.Messaging.ServiceBus;\n";
    private const string EventHubsUsing = "using Azure.Messaging.EventHubs;\n";
    private const string HostingUsing = "using Microsoft.Extensions.Hosting;\n";
    private const string DataUsing = "using System.Data.Common;\n";

    /// <summary>
    /// Source stubs mirroring the metadata shape of Azure.Messaging.ServiceBus. The analyzer
    /// matches sinks by fully-qualified name, so source-declared stubs behave like the real SDK.
    /// </summary>
    private const string ServiceBusStubs = """
        namespace Azure.Messaging.ServiceBus
        {
            public class ServiceBusClient
            {
                public ServiceBusProcessor CreateProcessor(string queueName) => new ServiceBusProcessor();
                public ServiceBusProcessor CreateProcessor(string queueName, ServiceBusProcessorOptions options) => new ServiceBusProcessor();
                public ServiceBusSessionProcessor CreateSessionProcessor(string queueName) => new ServiceBusSessionProcessor();
                public ServiceBusSessionProcessor CreateSessionProcessor(string queueName, ServiceBusSessionProcessorOptions options) => new ServiceBusSessionProcessor();
            }

            public class ServiceBusProcessorOptions
            {
                public int MaxConcurrentCalls { get; set; }
            }

            public class ServiceBusSessionProcessorOptions
            {
                public int MaxConcurrentSessions { get; set; }
                public int MaxConcurrentCallsPerSession { get; set; }
            }

            public class ServiceBusProcessor
            {
                public event System.Func<ProcessMessageEventArgs, System.Threading.Tasks.Task> ProcessMessageAsync;
                public event System.Func<ProcessErrorEventArgs, System.Threading.Tasks.Task> ProcessErrorAsync;
                public System.Threading.Tasks.Task StartProcessingAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
            }

            public class ServiceBusSessionProcessor
            {
                public event System.Func<ProcessSessionMessageEventArgs, System.Threading.Tasks.Task> ProcessMessageAsync;
                public event System.Func<ProcessErrorEventArgs, System.Threading.Tasks.Task> ProcessErrorAsync;
                public System.Threading.Tasks.Task StartProcessingAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
            }

            public class ProcessMessageEventArgs { }
            public class ProcessSessionMessageEventArgs { }
            public class ProcessErrorEventArgs { }
        }

        """;

    private const string EventHubsStubs = """
        namespace Azure.Messaging.EventHubs
        {
            public class EventProcessorClient
            {
                public event System.Func<Azure.Messaging.EventHubs.Processor.ProcessEventArgs, System.Threading.Tasks.Task> ProcessEventAsync;
                public event System.Func<Azure.Messaging.EventHubs.Processor.ProcessErrorEventArgs, System.Threading.Tasks.Task> ProcessErrorAsync;
            }
        }

        namespace Azure.Messaging.EventHubs.Processor
        {
            public class ProcessEventArgs { }
            public class ProcessErrorEventArgs { }
        }

        """;

    /// <summary>
    /// Source stubs mirroring RabbitMQ.Client across the v6/v7 surface drift: v6 ships
    /// EventingBasicConsumer.Received (sync) and AsyncEventingBasicConsumer.Received (async);
    /// v7 renames the async event to ReceivedAsync. One stub declares all three so each
    /// event-name row is exercisable; the analyzer matches by fully-qualified consumer type
    /// plus event name.
    /// </summary>
    private const string RabbitMqStubs = """
        namespace RabbitMQ.Client
        {
            public interface IModel { }
            public interface IChannel : IModel { }

            public class CreateChannelOptions
            {
                public ushort? ConsumerDispatchConcurrency { get; set; }
            }

            public interface IConnection
            {
                IModel CreateModel();
                System.Threading.Tasks.Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null, System.Threading.CancellationToken cancellationToken = default);
            }

            public class ConnectionFactory
            {
                public int ConsumerDispatchConcurrency { get; set; } = 1;
                public IModel CreateChannel() => null!;
                public IConnection CreateConnection() => null!;
                public System.Threading.Tasks.Task<IConnection> CreateConnectionAsync() => null!;
            }
        }

        namespace RabbitMQ.Client.Events
        {
            public delegate System.Threading.Tasks.Task AsyncEventHandler<in TEvent>(object sender, TEvent @event);

            public class BasicDeliverEventArgs { }

            public class EventingBasicConsumer
            {
                public EventingBasicConsumer(RabbitMQ.Client.IModel model) { }
                public event System.EventHandler<BasicDeliverEventArgs> Received;
            }

            public class AsyncEventingBasicConsumer
            {
                public AsyncEventingBasicConsumer(RabbitMQ.Client.IModel model) { }
                public event AsyncEventHandler<BasicDeliverEventArgs> Received;
                public event AsyncEventHandler<BasicDeliverEventArgs> ReceivedAsync;
            }
        }

        """;

    /// <summary>
    /// Source stubs mirroring System.Threading.Tasks.Dataflow execution blocks. The analyzer
    /// matches by fully-qualified name; MaxDegreeOfParallelism defaults to 1 (sequential).
    /// </summary>
    private const string DataflowStubs = """
        namespace System.Threading.Tasks.Dataflow
        {
            public class DataflowBlockOptions
            {
                public const int Unbounded = -1;
            }

            public class ExecutionDataflowBlockOptions : DataflowBlockOptions
            {
                public int MaxDegreeOfParallelism { get; set; } = 1;
            }

            public class ActionBlock<TInput>
            {
                public ActionBlock(System.Action<TInput> action) { }
                public ActionBlock(System.Action<TInput> action, ExecutionDataflowBlockOptions dataflowBlockOptions) { }
                public ActionBlock(System.Func<TInput, System.Threading.Tasks.Task> action) { }
                public ActionBlock(System.Func<TInput, System.Threading.Tasks.Task> action, ExecutionDataflowBlockOptions dataflowBlockOptions) { }
                public bool Post(TInput item) => true;
            }

            public class TransformBlock<TInput, TOutput>
            {
                public TransformBlock(System.Func<TInput, TOutput> transform) { }
                public TransformBlock(System.Func<TInput, TOutput> transform, ExecutionDataflowBlockOptions dataflowBlockOptions) { }
            }
        }

        """;

    private const string DataflowUsing = "using System.Threading.Tasks.Dataflow;\n";

    /// <summary>
    /// Source stubs mirroring Azure.Messaging.EventHubs.Primitives.EventProcessor&lt;TPartition&gt;
    /// — the advanced batch-processing base type whose partition handlers run concurrently.
    /// </summary>
    private const string EventHubsBatchStubs = """
        namespace Azure.Messaging.EventHubs
        {
            public class EventData { }
        }

        namespace Azure.Messaging.EventHubs.Primitives
        {
            public abstract class EventProcessorPartition { }

            public abstract class EventProcessor<TPartition> where TPartition : EventProcessorPartition, new()
            {
                protected abstract System.Threading.Tasks.Task OnProcessingEventBatchAsync(
                    System.Collections.Generic.IEnumerable<Azure.Messaging.EventHubs.EventData> events,
                    TPartition partition,
                    System.Threading.CancellationToken cancellationToken);

                protected abstract System.Threading.Tasks.Task OnProcessingErrorAsync(
                    System.Exception exception,
                    TPartition partition,
                    string operationDescription,
                    System.Threading.CancellationToken cancellationToken);
            }
        }

        """;

    private const string RabbitMqUsing = "using RabbitMQ.Client;\nusing RabbitMQ.Client.Events;\n";

    private const string EfCoreStubs = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext : System.IDisposable
            {
                public int SaveChanges() => 0;
                public System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult(0);
                public void Dispose() { }
            }

            public interface IDbContextFactory<TContext> where TContext : DbContext
            {
                TContext CreateDbContext();
                System.Threading.Tasks.Task<TContext> CreateDbContextAsync(System.Threading.CancellationToken cancellationToken = default);
            }
        }

        public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
        {
            public void Add(object entity) { }
        }

        """;

    private const string HostingStubs = """
        namespace Microsoft.Extensions.Hosting
        {
            public interface IHostedService
            {
                System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken);
                System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken);
            }

            public abstract class BackgroundService : IHostedService
            {
                protected abstract System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken);
                public virtual System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
                public virtual System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
            }
        }

        """;

    private static Task VerifyAsync(string source) =>
        AnalyzerVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer>.VerifyDiagnosticsAsync(source);

    private static Task VerifyNoneAsync(string source) =>
        AnalyzerVerifier<DI021_ConcurrentHandlerSharedStateAnalyzer>.VerifyNoDiagnosticsAsync(source);

    // ---------------------------------------------------------------------
    // Positives: always-concurrent sinks
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SessionProcessor_DbContextField_MethodGroupHandler_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + HostingUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + HostingStubs + """
            public class OrderProcessor : BackgroundService
            {
                private readonly AppDbContext _db;
                private readonly ServiceBusSessionProcessor _processor;

                public OrderProcessor(AppDbContext db, ServiceBusSessionProcessor processor)
                {
                    _db = db;
                    _processor = processor;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    _processor.ProcessMessageAsync += HandleAsync;
                    await _processor.StartProcessingAsync(stoppingToken);
                }

                private async Task HandleAsync(ProcessSessionMessageEventArgs args)
                {
                    {|DI021:_db|}.Add(args);
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task SessionProcessor_LambdaCapturesOuterLocal_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                public async Task RunAsync(ServiceBusSessionProcessor processor)
                {
                    var db = new AppDbContext();
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:db|}.Add(args);
                        await db.SaveChangesAsync();
                    };
                    await processor.StartProcessingAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task EventProcessorClient_FieldUseInLambda_ReportsDiagnostic()
    {
        var source = EventHubsUsing + BaseUsings + EventHubsStubs + EfCoreStubs + """
            public class TelemetryIngester
            {
                private readonly AppDbContext _db;
                private readonly EventProcessorClient _client;

                public TelemetryIngester(AppDbContext db, EventProcessorClient client)
                {
                    _db = db;
                    _client = client;
                }

                public void Start()
                {
                    _client.ProcessEventAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_MaxConcurrentCallsConstantAboveOne_ReportsWarning()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", new ServiceBusProcessorOptions
                    {
                        MaxConcurrentCalls = 8
                    });
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_OptionsConfiguredInCtor_SubscribedInStartAsync_ReportsWarning()
    {
        var source = ServiceBusUsing + HostingUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + HostingStubs + """
            public class Worker : BackgroundService
            {
                private readonly AppDbContext _db;
                private readonly ServiceBusProcessor _processor;

                public Worker(AppDbContext db, ServiceBusClient client)
                {
                    _db = db;
                    var options = new ServiceBusProcessorOptions();
                    options.MaxConcurrentCalls = 4;
                    _processor = client.CreateProcessor("queue", options);
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    _processor.ProcessMessageAsync += HandleAsync;
                    await _processor.StartProcessingAsync(stoppingToken);
                }

                private async Task HandleAsync(ProcessMessageEventArgs args)
                {
                    {|DI021:_db|}.Add(args);
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_FinitePeriod_CallbackUsesField_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 5000);
                }

                private void Poll(object state)
                {
                    {|DI021:_db|}.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TimersTimer_ElapsedWithDefaultAutoReset_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new System.Timers.Timer(5000);
                    timer.Elapsed += (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ParallelForEach_SharedDbConnection_ReportsDiagnostic()
    {
        var source = DataUsing + BaseUsings + """
            public class Importer
            {
                public void Import(string[] rows, DbConnection connection)
                {
                    Parallel.ForEach(rows, row =>
                    {
                        var command = {|DI021:connection|}.CreateCommand();
                        command.CommandText = row;
                        command.ExecuteNonQuery();
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ParallelForEachAsync_SharedDbContext_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                public async Task ImportAsync(string[] rows)
                {
                    var db = new AppDbContext();
                    await Parallel.ForEachAsync(rows, async (row, ct) =>
                    {
                        {|DI021:db|}.Add(row);
                        await db.SaveChangesAsync(ct);
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ParallelInvoke_SharedDbContext_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly AppDbContext _db;

                public Importer(AppDbContext db)
                {
                    _db = db;
                }

                public void Import()
                {
                    Parallel.Invoke(
                        () => {|DI021:_db|}.Add("a"),
                        () => {|DI021:_db|}.Add("b"));
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task OneHopDelegation_ThinLambdaToInstanceMethod_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly ServiceBusSessionProcessor _processor;

                public Worker(AppDbContext db, ServiceBusSessionProcessor processor)
                {
                    _db = db;
                    _processor = processor;
                }

                public void Start()
                {
                    _processor.ProcessMessageAsync += args => HandleAsync(args);
                }

                private async Task HandleAsync(ProcessSessionMessageEventArgs args)
                {
                    {|DI021:_db|}.Add(args);
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task CapturedScopeResolution_LongLivedScopeResolvedInsideHandler_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly IServiceScopeFactory _scopeFactory;
                private IServiceScope _scope;

                public Worker(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    _scope = _scopeFactory.CreateScope();
                    processor.ProcessMessageAsync += async args =>
                    {
                        var db = {|DI021:_scope.ServiceProvider.GetRequiredService<AppDbContext>()|};
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task CapturedOuterScopeLocal_ResolvedInsideParallelBody_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Importer(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Import(string[] rows)
                {
                    using var scope = _scopeFactory.CreateScope();
                    Parallel.ForEach(rows, row =>
                    {
                        var db = {|DI021:scope.ServiceProvider.GetRequiredService<AppDbContext>()|};
                        db.Add(row);
                        db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task DerivedDbContext_TwoLevelsDeep_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public abstract class TenantDbContext : AppDbContext { }
            public class OrdersDbContext : TenantDbContext { }

            public class Worker
            {
                private readonly OrdersDbContext _db;

                public Worker(OrdersDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task EnclosingMethodParameter_CapturedIntoHandler_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                public void Start(ServiceBusSessionProcessor processor, AppDbContext db)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:db|}.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task StaticDbContextField_UsedInMethodGroupHandler_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private static AppDbContext _shared = new AppDbContext();

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += HandleAsync;
                }

                private static async Task HandleAsync(ProcessSessionMessageEventArgs args)
                {
                    {|DI021:_shared|}.Add(args);
                    await _shared.SaveChangesAsync();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task NestedReceiverChain_ProcessorBehindHolderField_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class ProcessorHolder
            {
                public ServiceBusSessionProcessor Processor { get; } = new ServiceBusSessionProcessor();
            }

            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly ProcessorHolder _holder = new ProcessorHolder();

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _holder.Processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task HttpContextField_UsedInTimerCallback_ReportsDiagnostic()
    {
        var source = BaseUsings + """
            namespace Microsoft.AspNetCore.Http
            {
                public class HttpContext
                {
                    public string TraceIdentifier { get; set; }
                }
            }

            public class Snapshotter
            {
                private readonly Microsoft.AspNetCore.Http.HttpContext _context;

                public Snapshotter(Microsoft.AspNetCore.Http.HttpContext context)
                {
                    _context = context;
                }

                public void Start()
                {
                    var timer = new Timer(_ => System.Console.WriteLine({|DI021:_context|}.TraceIdentifier), null, 0, 1000);
                }
            }
            """;

        await VerifyAsync(source);
    }

    // ---------------------------------------------------------------------
    // DI022: config-gated sinks with unprovable concurrency
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ServiceBusProcessor_NoOptions_ReportsInfo()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue");
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_KnobFromConfiguration_ReportsInfo()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client, int configuredConcurrency)
                {
                    var processor = client.CreateProcessor("queue", new ServiceBusProcessorOptions
                    {
                        MaxConcurrentCalls = configuredConcurrency
                    });
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    // ---------------------------------------------------------------------
    // Cross-method knob proofs: options built by same-type helper methods
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ServiceBusProcessor_OptionsFromExpressionBodiedHelper_KnobAboveOne_ReportsWarning()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions() =>
                    new ServiceBusProcessorOptions { MaxConcurrentCalls = 8 };

                public void Start(ServiceBusClient client)
                {
                    var options = CreateOptions();
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_OptionsFromBlockHelper_LocalWrites_KnobAboveOne_ReportsWarning()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions()
                {
                    var options = new ServiceBusProcessorOptions();
                    options.MaxConcurrentCalls = 8;
                    return options;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_OptionsFromHelper_KnobOne_NoDiagnostic()
    {
        // A helper that provably pins MaxConcurrentCalls = 1 proves THIS processor sequential —
        // the helper's fresh creation is instance-correlated by construction.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions() =>
                    new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_OptionsFromHelper_ParameterDrivenKnob_ReportsInfo()
    {
        // The helper's knob value comes from a parameter — unprovable, stays config-gated.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions(int concurrency) =>
                    new ServiceBusProcessorOptions { MaxConcurrentCalls = concurrency };

                public void Start(ServiceBusClient client, int configured)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions(configured));
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_OptionsFromVirtualHelper_ReportsInfo()
    {
        // A virtual helper can be overridden — its body is not proof of what runs.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                protected virtual ServiceBusProcessorOptions CreateOptions() =>
                    new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperKnobOne_LaterLocalWriteAboveOne_ReportsWarning()
    {
        // Helper proves 1, but a later direct write on the traced local raises it — any
        // constant above 1 wins.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions() =>
                    new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };

                public void Start(ServiceBusClient client)
                {
                    var options = CreateOptions();
                    options.MaxConcurrentCalls = 4;
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperReturnsField_ReportsInfo()
    {
        // The helper hands out a shared instance, not a fresh creation — its writes are not
        // correlated to this processor, so nothing is proven.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly ServiceBusProcessorOptions _shared = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private ServiceBusProcessorOptions CreateOptions() => _shared;

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task Parallel_OptionsFromHelper_MaxDegreeOne_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static System.Threading.Tasks.ParallelOptions CreateOptions() =>
                    new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 1 };

                public void Run(int[] items)
                {
                    System.Threading.Tasks.Parallel.ForEach(items, CreateOptions(), item =>
                    {
                        _db.Add(item);
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_SequentialHelper_ReassignedFromConcurrentHelper_ReportsWarning()
    {
        // The initializer's sequential proof is stale after the local is reassigned from another
        // helper; the replacement helper's constant 8 is what reaches the sink.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateSequential() =>
                    new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };

                private static ServiceBusProcessorOptions CreateConcurrent() =>
                    new ServiceBusProcessorOptions { MaxConcurrentCalls = 8 };

                public void Start(ServiceBusClient client)
                {
                    var options = CreateSequential();
                    options = CreateConcurrent();
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_SequentialHelper_ReassignedOpaque_ReportsInfo()
    {
        // An opaque reassignment (config load, factory call outside the file's proof rules)
        // invalidates the initializer's sequential proof — the sink is config-gated again.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateSequential() =>
                    new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };

                public virtual ServiceBusProcessorOptions LoadFromConfiguration() =>
                    new ServiceBusProcessorOptions();

                public void Start(ServiceBusClient client)
                {
                    var options = CreateSequential();
                    options = LoadFromConfiguration();
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task Parallel_HelperLocalSequentialThenFreshReplacement_ReportsWarning()
    {
        // Inside the helper, the returned local is replaced by a fresh ParallelOptions whose
        // MaxDegreeOfParallelism defaults to unlimited — the stale 1 must not prove sequential.
        var source = BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static System.Threading.Tasks.ParallelOptions CreateOptions()
                {
                    var options = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 1 };
                    options = new System.Threading.Tasks.ParallelOptions();
                    return options;
                }

                public void Run(int[] items)
                {
                    System.Threading.Tasks.Parallel.ForEach(items, CreateOptions(), item =>
                    {
                        {|DI021:_db|}.Add(item);
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_SequentialInitializer_FreshReplacement_ReportsInfo()
    {
        // Type-level variant of the stale-proof hole: the local's sequential initializer is
        // replaced by a fresh default-configured options object before reaching the sink.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    options = new ServiceBusProcessorOptions();
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperWithDeferredLambdaWrite_ReportsInfo()
    {
        // The lambda's knob write is deferred — it is not part of constructing the returned
        // options, so it must not upgrade to DI021; but it threatens the initializer's 1, so
        // nothing is proven either way.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;
                public static Action Later;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions()
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    Later = () => options.MaxConcurrentCalls = 8;
                    return options;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_LocalReusedOpaquelyAfterCreation_NoDiagnostic()
    {
        // The processor snapshots its options at creation; reusing the local afterwards cannot
        // affect the already-created processor, so the pre-creation sequential proof stands.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public virtual ServiceBusProcessorOptions LoadFromConfiguration() =>
                    new ServiceBusProcessorOptions();

                public void Start(ServiceBusClient client)
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    var processor = client.CreateProcessor("queue", options);
                    options = LoadFromConfiguration();
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_KnobRaisedAfterCreation_NoDiagnostic()
    {
        // A knob write after the processor was created belongs to later reuse of the local,
        // not to this processor.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    var processor = client.CreateProcessor("queue", options);
                    options.MaxConcurrentCalls = 8;
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperConditionalSequentialReplacement_ReportsWarning()
    {
        // The sequential replacement is conditional — the concurrent initializer can still reach
        // the sink, so the union of candidate values must keep the 8.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions(bool sequential)
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 8 };
                    if (sequential)
                    {
                        options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    }

                    return options;
                }

                public void Start(ServiceBusClient client, bool sequential)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions(sequential));
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_LocalConditionalSequentialReplacement_ReportsWarning()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client, bool sequential)
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 8 };
                    if (sequential)
                    {
                        options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    }

                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperCompoundKnobWrite_ReportsInfo()
    {
        // `+= 1` makes the real value 2; the analyzer does not evaluate compound writes, so the
        // knob is unprovable — never proven sequential by the stale constant 1.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions()
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    options.MaxConcurrentCalls += 1;
                    return options;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperStraightLineOverwriteToOne_NoDiagnostic()
    {
        // The straight-line write definitively overwrites the initializer's 8 before the
        // options are returned — the processor is provably sequential.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions()
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 8 };
                    options.MaxConcurrentCalls = 1;
                    return options;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_LocalStraightLineOverwriteToOne_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 8 };
                    options.MaxConcurrentCalls = 1;
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperConditionalKnobRaise_ReportsWarning()
    {
        // A write inside a nested block is conditional: it joins the candidate union instead of
        // overwriting, so the possible 8 still reports.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions(bool highLoad)
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    if (highLoad)
                    {
                        options.MaxConcurrentCalls = 8;
                    }

                    return options;
                }

                public void Start(ServiceBusClient client, bool highLoad)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions(highLoad));
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_ConcurrentHelperWithDeferredLambdaWrite_ReportsWarning()
    {
        // The deferred lambda write poisons sequential proofs but must not erase the
        // construction-time concurrent constant — the returned options are configured with 8.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;
                public static Action Later;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions()
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 8 };
                    Later = () => options.MaxConcurrentCalls = 1;
                    return options;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_LocalFunctionMutatorDeclaredAfterSink_ReportsInfo()
    {
        // The local function is declared after the creation call but invoked before it —
        // declaration position says nothing about execution order, so its write poisons the
        // sequential proof instead of being span-filtered away.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    Raise();
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };

                    void Raise() => options.MaxConcurrentCalls = 8;
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperLocalEscapesToMethod_ReportsInfo()
    {
        // The options escape to Tune before the return — the callee can raise the knob, so the
        // initializer's 1 is no longer proof of sequential dispatch.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions()
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    Tune(options);
                    return options;
                }

                private static void Tune(ServiceBusProcessorOptions options) =>
                    options.MaxConcurrentCalls = 8;

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TimersTimer_AutoResetFalse_ConditionalCompoundReenable_ReportsDiagnostic()
    {
        // The conditional compound write can re-enable overlapping callbacks, so the straight-line
        // AutoReset = false is not sufficient proof of sequential elapsed events.
        var source = BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(bool allowOverlap)
                {
                    var timer = new System.Timers.Timer(1000);
                    timer.AutoReset = false;
                    if (allowOverlap)
                    {
                        timer.AutoReset |= true;
                    }

                    timer.Elapsed += (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperWithUnrelatedNestedReturn_KnobAboveOne_ReportsWarning()
    {
        // The lambda's return belongs to the nested function, not the helper — the helper still
        // has exactly one return, so its concurrent constant must be proven.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions()
                {
                    Func<int> log = () => { return 0; };
                    log();
                    return new ServiceBusProcessorOptions { MaxConcurrentCalls = 8 };
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_AliasMutationAfterDefiniteWrite_ReportsInfo()
    {
        // The alias escape poisons the proof permanently — a later definite write on the
        // original local must not restore a sequential proof the alias can still break.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var options = new ServiceBusProcessorOptions();
                    var alias = options;
                    options.MaxConcurrentCalls = 1;
                    alias.MaxConcurrentCalls = 8;
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperParenthesizedReturn_KnobOne_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions()
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    return (options);
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task TimersTimer_AutoResetTrueAtStart_LaterFalse_ReportsDiagnostic()
    {
        // The timer starts while AutoReset is still true — a later safe write does not undo the
        // overlapping callbacks already enabled at Start(). Timer proofs have no consumption
        // cutoff, so every write must prove the safe state.
        var source = BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new System.Timers.Timer(1000);
                    timer.AutoReset = true;
                    timer.Elapsed += (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                    timer.AutoReset = false;
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task Parallel_HelperConditionalSafeWrite_DefaultCandidateRemains_ReportsWarning()
    {
        // The initializer never sets MaxDegreeOfParallelism, so the unlimited default is itself
        // a candidate; the conditional 1 must not become the only collected value.
        var source = BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static System.Threading.Tasks.ParallelOptions CreateOptions(bool sequential)
                {
                    var options = new System.Threading.Tasks.ParallelOptions();
                    if (sequential)
                    {
                        options.MaxDegreeOfParallelism = 1;
                    }

                    return options;
                }

                public void Run(int[] items, bool sequential)
                {
                    System.Threading.Tasks.Parallel.ForEach(items, CreateOptions(sequential), item =>
                    {
                        {|DI021:_db|}.Add(item);
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_HelperIncrementsKnob_ReportsInfo()
    {
        // ++ is a write the assignment scan cannot evaluate — the resulting value is unknown,
        // so the stale constant 1 must not prove sequential dispatch.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions()
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    options.MaxConcurrentCalls++;
                    return options;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_KnobIncrementAfterCreation_NoDiagnostic()
    {
        // The increment happens after the processor snapshotted its options — later variable
        // reuse, ignored exactly like post-consumption assignments.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var options = new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 };
                    var processor = client.CreateProcessor("queue", options);
                    options.MaxConcurrentCalls++;
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_ParenthesizedExpressionBodiedHelper_KnobOne_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                private static ServiceBusProcessorOptions CreateOptions() =>
                    (new ServiceBusProcessorOptions { MaxConcurrentCalls = 1 });

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", CreateOptions());
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }




    // ---------------------------------------------------------------------
    // EventHubs EventProcessor<TPartition> batch overrides
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EventProcessorBatch_DbContextField_ReportsWarning()
    {
        // Partition batches are dispatched concurrently across partitions.
        var source = "using System.Collections.Generic;\nusing Azure.Messaging.EventHubs;\nusing Azure.Messaging.EventHubs.Primitives;\n" + BaseUsings + EventHubsBatchStubs + EfCoreStubs + """
            public class MyPartition : EventProcessorPartition { public MyPartition() { } }

            public class BatchProcessor : EventProcessor<MyPartition>
            {
                private readonly AppDbContext _db;

                public BatchProcessor(AppDbContext db)
                {
                    _db = db;
                }

                protected override async Task OnProcessingEventBatchAsync(
                    IEnumerable<EventData> events, MyPartition partition, CancellationToken cancellationToken)
                {
                    foreach (var item in events)
                    {
                        {|DI021:_db|}.Add(item);
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                }

                protected override Task OnProcessingErrorAsync(
                    Exception exception, MyPartition partition, string operationDescription, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task EventProcessorBatch_DbContextFactory_NoDiagnostic()
    {
        var source = "using System.Collections.Generic;\nusing Azure.Messaging.EventHubs;\nusing Azure.Messaging.EventHubs.Primitives;\n" + BaseUsings + EventHubsBatchStubs + EfCoreStubs + """
            public class MyPartition : EventProcessorPartition { public MyPartition() { } }

            public class BatchProcessor : EventProcessor<MyPartition>
            {
                private readonly Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> _factory;

                public BatchProcessor(Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> factory)
                {
                    _factory = factory;
                }

                protected override async Task OnProcessingEventBatchAsync(
                    IEnumerable<EventData> events, MyPartition partition, CancellationToken cancellationToken)
                {
                    using var db = _factory.CreateDbContext();
                    foreach (var item in events)
                    {
                        db.Add(item);
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }

                protected override Task OnProcessingErrorAsync(
                    Exception exception, MyPartition partition, string operationDescription, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task EventProcessorBatch_SameNamedUserBaseType_NoDiagnostic()
    {
        // A user-defined EventProcessor<T> in another namespace is not the Azure SDK type.
        var source = BaseUsings + EfCoreStubs + """
            namespace MyApp.Processing
            {
                public abstract class EventProcessor<TPartition>
                {
                    protected abstract System.Threading.Tasks.Task OnProcessingEventBatchAsync(int[] events, TPartition partition);
                }
            }

            public class BatchProcessor : MyApp.Processing.EventProcessor<string>
            {
                private readonly AppDbContext _db;

                public BatchProcessor(AppDbContext db)
                {
                    _db = db;
                }

                protected override Task OnProcessingEventBatchAsync(int[] events, string partition)
                {
                    _db.Add(events);
                    return _db.SaveChangesAsync();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    // ---------------------------------------------------------------------
    // TPL Dataflow execution-block sinks
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ActionBlock_NoOptions_NoDiagnostic()
    {
        // MaxDegreeOfParallelism defaults to 1: an ActionBlock without options is sequential.
        var source = DataflowUsing + BaseUsings + DataflowStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var block = new ActionBlock<int>(item =>
                    {
                        _db.Add(item);
                        _db.SaveChanges();
                    });
                    block.Post(1);
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ActionBlock_MaxDegreeAboveOne_ReportsWarning()
    {
        var source = DataflowUsing + BaseUsings + DataflowStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var block = new ActionBlock<int>(item =>
                    {
                        {|DI021:_db|}.Add(item);
                        _db.SaveChanges();
                    }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4 });
                    block.Post(1);
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ActionBlock_MaxDegreeUnbounded_ReportsWarning()
    {
        var source = DataflowUsing + BaseUsings + DataflowStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var options = new ExecutionDataflowBlockOptions
                    {
                        MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded
                    };
                    var block = new ActionBlock<int>(async item =>
                    {
                        {|DI021:_db|}.Add(item);
                        await _db.SaveChangesAsync();
                    }, options);
                    block.Post(1);
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ActionBlock_MaxDegreeOneExplicit_NoDiagnostic()
    {
        var source = DataflowUsing + BaseUsings + DataflowStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var block = new ActionBlock<int>(item =>
                    {
                        _db.Add(item);
                        _db.SaveChanges();
                    }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });
                    block.Post(1);
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ActionBlock_UnprovableMaxDegree_ReportsInfo()
    {
        var source = DataflowUsing + BaseUsings + DataflowStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(int configured)
                {
                    var block = new ActionBlock<int>(item =>
                    {
                        {|DI022:_db|}.Add(item);
                        _db.SaveChanges();
                    }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = configured });
                    block.Post(1);
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TransformBlock_MaxDegreeAboveOne_ReportsWarning()
    {
        var source = DataflowUsing + BaseUsings + DataflowStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var block = new TransformBlock<int, int>(item =>
                    {
                        {|DI021:_db|}.Add(item);
                        _db.SaveChanges();
                        return item;
                    }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 8 });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ActionBlock_InHandlerCreation_NoDiagnostic()
    {
        var source = DataflowUsing + BaseUsings + DataflowStubs + EfCoreStubs + """
            public class Worker
            {
                public void Start()
                {
                    var block = new ActionBlock<int>(item =>
                    {
                        using var db = new AppDbContext();
                        db.Add(item);
                        db.SaveChanges();
                    }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4 });
                    block.Post(1);
                }
            }
            """;

        await VerifyNoneAsync(source);
    }


    [Fact]
    public async Task ActionBlock_CallerProvidedOptions_ReportsInfo()
    {
        // The options come from the caller — nothing proves the knob, so the sink is
        // config-gated rather than assumed sequential.
        var source = DataflowUsing + BaseUsings + DataflowStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ExecutionDataflowBlockOptions options)
                {
                    var block = new ActionBlock<int>(item =>
                    {
                        {|DI022:_db|}.Add(item);
                        _db.SaveChanges();
                    }, options);
                    block.Post(1);
                }
            }
            """;

        await VerifyAsync(source);
    }

    // ---------------------------------------------------------------------
    // PLINQ ForAll sinks
    // ---------------------------------------------------------------------

    [Fact]
    public async Task PlinqForAll_DbContextField_ReportsWarning()
    {
        // PLINQ partitions run concurrently by default.
        var source = "using System.Linq;\n" + BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Run(int[] items)
                {
                    items.AsParallel().ForAll(item =>
                    {
                        {|DI021:_db|}.Add(item);
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task PlinqForAll_WithDegreeOfParallelismOne_NoDiagnostic()
    {
        // A proven single-degree query runs its partitions sequentially.
        var source = "using System.Linq;\n" + BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Run(int[] items)
                {
                    items.AsParallel().WithDegreeOfParallelism(1).ForAll(item =>
                    {
                        _db.Add(item);
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task PlinqForAll_WithDegreeOfParallelismAboveOne_ReportsWarning()
    {
        var source = "using System.Linq;\n" + BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Run(int[] items)
                {
                    items.AsParallel().WithDegreeOfParallelism(8).ForAll(item =>
                    {
                        {|DI021:_db|}.Add(item);
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task PlinqForAll_InHandlerCreation_NoDiagnostic()
    {
        var source = "using System.Linq;\n" + BaseUsings + EfCoreStubs + """
            public class Worker
            {
                public void Run(int[] items)
                {
                    items.AsParallel().ForAll(item =>
                    {
                        using var db = new AppDbContext();
                        db.Add(item);
                        db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }


    [Fact]
    public async Task PlinqForAll_SequentialChainThroughConcat_NoDiagnostic()
    {
        // Binary operators name their receiver parameter "first"/"outer", not "source" — the
        // proven single-degree setting must still be found through them.
        var source = "using System.Linq;\n" + BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Run(int[] items, int[] more)
                {
                    items.AsParallel().WithDegreeOfParallelism(1).Concat(more.AsParallel()).ForAll(item =>
                    {
                        _db.Add(item);
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }


    // ---------------------------------------------------------------------
    // RabbitMQ instance-correlated chain proofs (factory -> connection -> channel -> consumer)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RabbitMq_ChainProvenSequential_OtherFactoryConcurrent_NoDiagnostic()
    {
        // THIS consumer's chain pins ConsumerDispatchConcurrency = 1; the unrelated factory's 4
        // must not contaminate it.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var otherFactory = new ConnectionFactory { ConsumerDispatchConcurrency = 4 };
                    var factory = new ConnectionFactory { ConsumerDispatchConcurrency = 1 };
                    var connection = factory.CreateConnection();
                    var channel = connection.CreateModel();
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (sender, args) =>
                    {
                        _db.Add(args);
                        _db.SaveChanges();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task RabbitMq_ChainDefaultFactory_NoDiagnostic()
    {
        // A fully traced fresh factory that never sets the knob keeps the sequential default
        // of 1 — the config-gated Info is unnecessary noise here.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var factory = new ConnectionFactory();
                    var connection = factory.CreateConnection();
                    var channel = connection.CreateModel();
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (sender, args) =>
                    {
                        _db.Add(args);
                        _db.SaveChanges();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task RabbitMq_AsyncChainProvenSequential_NoDiagnostic()
    {
        // v7 shape: awaited connection/channel creation links still trace.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public async Task StartAsync()
                {
                    var factory = new ConnectionFactory { ConsumerDispatchConcurrency = 1 };
                    var connection = await factory.CreateConnectionAsync();
                    var channel = await connection.CreateChannelAsync();
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += async (sender, args) =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task RabbitMq_ChainProvenConcurrent_ReportsWarning()
    {
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var factory = new ConnectionFactory { ConsumerDispatchConcurrency = 4 };
                    var connection = factory.CreateConnection();
                    var channel = connection.CreateModel();
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RabbitMq_ChannelOptionsArgument_ReportsInfo()
    {
        // A CreateChannelOptions argument can override the factory knob per channel — the chain
        // bails conservatively to the config-gated tier.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public async Task StartAsync(CreateChannelOptions channelOptions)
                {
                    var factory = new ConnectionFactory { ConsumerDispatchConcurrency = 1 };
                    var connection = await factory.CreateConnectionAsync();
                    var channel = await connection.CreateChannelAsync(channelOptions);
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += async (sender, args) =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }


    [Fact]
    public async Task RabbitMq_ChannelOptionsOverridesConcurrentFactory_ReportsInfo()
    {
        // The per-channel options may pin this channel back to sequential — the factory's 4
        // must not upgrade the diagnostic past the config-gated tier.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public async Task StartAsync()
                {
                    var factory = new ConnectionFactory { ConsumerDispatchConcurrency = 4 };
                    var connection = await factory.CreateConnectionAsync();
                    var channel = await connection.CreateChannelAsync(new CreateChannelOptions { ConsumerDispatchConcurrency = 1 });
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += async (sender, args) =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }


    [Fact]
    public async Task RabbitMq_ChainConcurrent_CancellationTokenArgument_ReportsWarning()
    {
        // A cancellation token is not a channel-options override — the chain-proven 4 stands.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public async Task StartAsync(CancellationToken token)
                {
                    var factory = new ConnectionFactory { ConsumerDispatchConcurrency = 4 };
                    var connection = await factory.CreateConnectionAsync();
                    var channel = await connection.CreateChannelAsync(cancellationToken: token);
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += async (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }


    [Fact]
    public async Task RabbitMq_FreshDefaultFactory_ReassignedAfterConsumerBuilt_NoDiagnostic()
    {
        // The factory variable is reused after this consumer's chain was already built — the
        // later reassignment belongs to a different consumer and must not break the
        // fresh-default sequential proof.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var factory = new ConnectionFactory();
                    var connection = factory.CreateConnection();
                    var channel = connection.CreateModel();
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (sender, args) =>
                    {
                        _db.Add(args);
                        _db.SaveChanges();
                    };

                    factory = new ConnectionFactory { ConsumerDispatchConcurrency = 4 };
                    factory.CreateConnection();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    // ---------------------------------------------------------------------
    // RabbitMQ consumer sinks (v6 Received / v7 ReceivedAsync)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RabbitMq_EventingBasicConsumer_Received_DbContextField_ReportsInfo()
    {
        // The dispatch-concurrency knob lives on the ConnectionFactory, not the consumer, and
        // defaults to config-bound wiring in real apps — unprovable, so the config-gated tier.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(IModel channel)
                {
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (sender, args) =>
                    {
                        {|DI022:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RabbitMq_AsyncEventingBasicConsumer_ReceivedAsync_CapturedLocal_ReportsInfo()
    {
        // v7 surface: the async event was renamed to ReceivedAsync.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                public void Start(IModel channel)
                {
                    var db = new AppDbContext();
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += async (sender, args) =>
                    {
                        {|DI022:db|}.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RabbitMq_AsyncEventingBasicConsumer_Received_V6Async_ReportsInfo()
    {
        // v6 surface: the async consumer's event is still named Received.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(IModel channel)
                {
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.Received += async (sender, args) =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RabbitMq_ConsumerDispatchConcurrencyAboveOne_ReportsWarning()
    {
        // A constant ConsumerDispatchConcurrency above 1 in the containing type proves the
        // dispatch pump is concurrent, upgrading the config-gated Info to the full warning.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var factory = new ConnectionFactory { ConsumerDispatchConcurrency = 4 };
                    var channel = factory.CreateChannel();
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += async (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RabbitMq_ConsumerDispatchConcurrencyUshortConstant_ReportsWarning()
    {
        // RabbitMQ.Client v7 declares ConsumerDispatchConcurrency as ushort, so the proven
        // constant arrives as a ushort value, not int — it must still upgrade to DI021.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            namespace RabbitMQ.Client.V7
            {
                public class ConnectionFactory
                {
                    public ushort ConsumerDispatchConcurrency { get; set; } = 1;
                    public RabbitMQ.Client.IModel CreateChannel() => null!;
                }
            }

            public class OrderConsumer
            {
                private const ushort Concurrency = 4;
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var factory = new RabbitMQ.Client.V7.ConnectionFactory { ConsumerDispatchConcurrency = Concurrency };
                    var channel = factory.CreateChannel();
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += async (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task RabbitMq_SameNamedUserType_OtherNamespace_NoDiagnostic()
    {
        // Sinks match by fully-qualified name; a user-defined consumer with the same simple
        // name must stay silent.
        var source = BaseUsings + EfCoreStubs + """
            namespace MyApp.Messaging
            {
                public class EventingBasicConsumer
                {
                    public event System.EventHandler<object> Received;
                    public void Raise() => Received?.Invoke(this, new object());
                }
            }

            public class OrderConsumer
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(MyApp.Messaging.EventingBasicConsumer consumer)
                {
                    consumer.Received += (sender, args) =>
                    {
                        _db.Add(args);
                        _db.SaveChanges();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task RabbitMq_DbContextFactoryCapture_NoDiagnostic()
    {
        // IDbContextFactory<T> is whitelisted: resolving a fresh context per delivery is the
        // recommended pattern.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> _factory;

                public OrderConsumer(Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> factory)
                {
                    _factory = factory;
                }

                public void Start(IModel channel)
                {
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += async (sender, args) =>
                    {
                        using var db = _factory.CreateDbContext();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task RabbitMq_InHandlerCreation_NoDiagnostic()
    {
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                public void Start(IModel channel)
                {
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (sender, args) =>
                    {
                        using var db = new AppDbContext();
                        db.Add(args);
                        db.SaveChanges();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task RabbitMq_LockGuardedUse_NoDiagnostic()
    {
        // The shared serialization-guard machinery applies to RabbitMQ sinks: a lock on a
        // monitor shared from outside the handler serializes the captured context.
        var source = RabbitMqUsing + BaseUsings + RabbitMqStubs + EfCoreStubs + """
            public class OrderConsumer
            {
                private readonly AppDbContext _db;
                private readonly object _gate = new object();

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(IModel channel)
                {
                    var consumer = new EventingBasicConsumer(channel);
                    consumer.Received += (sender, args) =>
                    {
                        lock (_gate)
                        {
                            _db.Add(args);
                            _db.SaveChanges();
                        }
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    // ---------------------------------------------------------------------
    // Negatives: safe patterns
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Handler_CreatesOwnScopePerInvocation_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Worker(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_UsesDbContextFactoryPerInvocation_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> _factory;

                public Worker(Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> factory)
                {
                    _factory = factory;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        using var db = await _factory.CreateDbContextAsync();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_CreatesContextInline_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        using var db = new AppDbContext();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_MaxConcurrentCallsOne_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateProcessor("queue", new ServiceBusProcessorOptions
                    {
                        MaxConcurrentCalls = 1
                    });
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task SessionProcessor_SingleSessionSingleCall_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var processor = client.CreateSessionProcessor("queue", new ServiceBusSessionProcessorOptions
                    {
                        MaxConcurrentSessions = 1
                    });
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Parallel_MaxDegreeOfParallelismOne_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly AppDbContext _db;

                public Importer(AppDbContext db)
                {
                    _db = db;
                }

                public void Import(string[] rows)
                {
                    var options = new ParallelOptions { MaxDegreeOfParallelism = 1 };
                    Parallel.ForEach(rows, options, row =>
                    {
                        _db.Add(row);
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_InfiniteTimeSpanPeriod_OneShot_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class OneShot
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public OneShot(AppDbContext db)
                {
                    _db = db;
                }

                public void Schedule()
                {
                    _timer = new Timer(Run, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_TimeoutInfinitePeriod_OneShot_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class OneShot
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public OneShot(AppDbContext db)
                {
                    _db = db;
                }

                public void Schedule()
                {
                    _timer = new Timer(Run, null, 5000, Timeout.Infinite);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_NeverStartedSingleArgCtor_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Dormant
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Dormant(AppDbContext db)
                {
                    _db = db;
                    _timer = new Timer(Run);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task TimersTimer_AutoResetFalse_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class OneShot
            {
                private readonly AppDbContext _db;

                public OneShot(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new System.Timers.Timer(5000);
                    timer.AutoReset = false;
                    timer.Elapsed += (sender, args) =>
                    {
                        _db.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_SerializedWithSemaphoreSlim_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        await _gate.WaitAsync();
                        try
                        {
                            _db.Add(args);
                            await _db.SaveChangesAsync();
                        }
                        finally
                        {
                            _gate.Release();
                        }
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_SerializedWithLock_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly AppDbContext _db;
                private readonly object _sync = new object();

                public Importer(AppDbContext db)
                {
                    _db = db;
                }

                public void Import(string[] rows)
                {
                    Parallel.ForEach(rows, row =>
                    {
                        lock (_sync)
                        {
                            _db.Add(row);
                            _db.SaveChanges();
                        }
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_InterlockedReentrancyGuard_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private int _running;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Poll(object state)
                {
                    if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                    {
                        return;
                    }

                    try
                    {
                        _db.Add(state);
                        _db.SaveChanges();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _running, 0);
                    }
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_TimerRearmGuard_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Poll(object state)
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    try
                    {
                        _db.Add(state);
                        _db.SaveChanges();
                    }
                    finally
                    {
                        _timer.Change(1000, 1000);
                    }
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_CapturesOnlyWhitelistedServices_NoDiagnostic()
    {
        var source = "using Microsoft.Extensions.Logging;\n" + ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { void Log(string message); }
                public interface ILogger<T> : ILogger { }
            }

            public class Worker
            {
                private readonly ILogger<Worker> _logger;
                private readonly IServiceScopeFactory _scopeFactory;

                public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory)
                {
                    _logger = logger;
                    _scopeFactory = scopeFactory;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _logger.Log("received");
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Add(args);
                        await db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_FieldReassignedInsideHandlerBeforeUse_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private AppDbContext _db;

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db = new AppDbContext();
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task Handler_UsesOnlyHandlerParameters_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        System.Console.WriteLine(args);
                        await Task.CompletedTask;
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task MassTransitShapedConsumer_CtorInjectedDbContext_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            namespace MassTransit
            {
                public interface ConsumeContext<T> { T Message { get; } }
                public interface IConsumer<T>
                {
                    System.Threading.Tasks.Task Consume(ConsumeContext<T> context);
                }
            }

            public class OrderSubmitted { }

            public class OrderConsumer : MassTransit.IConsumer<OrderSubmitted>
            {
                private readonly AppDbContext _db;

                public OrderConsumer(AppDbContext db)
                {
                    _db = db;
                }

                public async Task Consume(MassTransit.ConsumeContext<OrderSubmitted> context)
                {
                    _db.Add(context.Message);
                    await _db.SaveChangesAsync();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task AwaitedTaskRun_SingleFanOut_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public async Task RunAsync()
                {
                    await Task.Run(() =>
                    {
                        _db.Add("row");
                        _db.SaveChanges();
                    });
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task SameNamedUserType_DifferentNamespace_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            namespace MyCompany.Messaging
            {
                public class ServiceBusProcessor
                {
                    public event System.Func<string, System.Threading.Tasks.Task> ProcessMessageAsync;
                }
            }

            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(MyCompany.Messaging.ServiceBusProcessor processor)
                {
                    processor.ProcessMessageAsync += async message =>
                    {
                        _db.Add(message);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task MethodGroupHandler_DeclaredOnDifferentType_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Handlers
            {
                public static Task HandleAsync(ProcessSessionMessageEventArgs args) => Task.CompletedTask;
            }

            public class Worker
            {
                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += Handlers.HandleAsync;
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadSafeCustomSingleton_CapturedInHandler_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class MetricsCollector
            {
                public void Increment(string name) { }
            }

            public class Worker
            {
                private readonly MetricsCollector _metrics;

                public Worker(MetricsCollector metrics)
                {
                    _metrics = metrics;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _metrics.Increment("messages");
                        await Task.CompletedTask;
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task DisposeOnlyReference_InHandler_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Dispose();
                        await Task.CompletedTask;
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task TwoProcessorsInOneType_SequentialKnobDoesNotSuppressOtherSink()
    {
        // The first processor is provably sequential (its own options say MaxConcurrentCalls = 1);
        // the second uses the default and must still report DI022 — a sequential knob on a
        // different options instance in the same type is not proof for this sink.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    var sequential = client.CreateProcessor("queue-a", new ServiceBusProcessorOptions
                    {
                        MaxConcurrentCalls = 1
                    });
                    sequential.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };

                    var defaulted = client.CreateProcessor("queue-b");
                    defaulted.ProcessMessageAsync += async args =>
                    {
                        {|DI022:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TimersTimer_SynchronizingObjectAssignedNull_StillReports()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new System.Timers.Timer(5000);
                    timer.SynchronizingObject = null;
                    timer.Elapsed += (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ServiceBusProcessor_TargetTypedOptionsSequential_NoDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusClient client)
                {
                    ServiceBusProcessorOptions options = new() { MaxConcurrentCalls = 1 };
                    var processor = client.CreateProcessor("queue", options);
                    processor.ProcessMessageAsync += async args =>
                    {
                        _db.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task SemaphoreGuard_UseBeforeWait_StillReports()
    {
        // The semaphore bracket only protects uses inside the guarded try region; the access
        // before WaitAsync is still concurrent.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _gate.WaitAsync();
                        try
                        {
                            await _db.SaveChangesAsync();
                        }
                        finally
                        {
                            _gate.Release();
                        }
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_InfiniteDueTimeNeverStarted_NoDiagnostic()
    {
        // Timeout.Infinite due time creates the timer disabled; without a Change(...) on this
        // timer it never fires, finite period or not.
        var source = BaseUsings + EfCoreStubs + """
            public class Dormant
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Dormant(AppDbContext db)
                {
                    _db = db;
                    _timer = new Timer(Run, null, Timeout.Infinite, 5000);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_ChangeOnDifferentTimer_DormantNotReported()
    {
        // The finite Change(...) starts a different timer; the dormant single-arg timer
        // capturing the DbContext never fires.
        var source = BaseUsings + EfCoreStubs + """
            public class TwoTimers
            {
                private readonly AppDbContext _db;
                private Timer _dormant;
                private Timer _active;

                public TwoTimers(AppDbContext db)
                {
                    _db = db;
                    _dormant = new Timer(UseDb);
                    _active = new Timer(Heartbeat);
                    _active.Change(0, 1000);
                }

                private void UseDb(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }

                private void Heartbeat(object state)
                {
                    System.Console.WriteLine("tick");
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task ThreadingTimer_ZeroPeriod_OneShot_NoDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            public class OneShot
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public OneShot(AppDbContext db)
                {
                    _db = db;
                }

                public void Schedule()
                {
                    _timer = new Timer(Run, null, 0, 0);
                }

                private void Run(object state)
                {
                    _db.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyNoneAsync(source);
    }

    [Fact]
    public async Task TimerRearmGuard_StopsDifferentTimer_StillReports()
    {
        // Stopping some other timer does not serialize this handler's own invocations.
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private Timer _timer;
                private Timer _other;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _other = new Timer(Noop, null, 0, 1000);
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Noop(object state)
                {
                }

                private void Poll(object state)
                {
                    _other.Change(Timeout.Infinite, Timeout.Infinite);
                    {|DI021:_db|}.Add(state);
                    _db.SaveChanges();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TimerRearmGuard_StopAfterUse_StillReports()
    {
        // The shared-state access happens before the handler stops its own timer, so
        // overlapping invocations can still race on it.
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Poll(object state)
                {
                    {|DI021:_db|}.Add(state);
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    _db.SaveChanges();
                    _timer.Change(1000, 1000);
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task LockOnPerInvocationObject_StillReports()
    {
        // Each invocation creates its own monitor object, so the lock serializes nothing.
        var source = BaseUsings + EfCoreStubs + """
            public class Importer
            {
                private readonly AppDbContext _db;

                public Importer(AppDbContext db)
                {
                    _db = db;
                }

                public void Import(string[] rows)
                {
                    Parallel.ForEach(rows, row =>
                    {
                        var gate = new object();
                        lock (gate)
                        {
                            {|DI021:_db|}.Add(row);
                            _db.SaveChanges();
                        }
                    });
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task AsyncLockGuard_UseOutsideUsingRegion_StillReports()
    {
        // The disposable async-lock only guards the using region; the access after it races.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class AsyncLock
            {
                public Task<System.IDisposable> LockAsync() => Task.FromResult<System.IDisposable>(null);
            }

            public class Worker
            {
                private readonly AppDbContext _db;
                private readonly AsyncLock _mutex = new AsyncLock();

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        using (await _mutex.LockAsync())
                        {
                            _db.Add(args);
                        }

                        await {|DI021:_db|}.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task EventProcessorClient_ProcessorNamespaceVariant_ReportsDiagnostic()
    {
        var source = BaseUsings + EfCoreStubs + """
            namespace Azure.Messaging.EventHubs.Processor
            {
                public class EventProcessorClient
                {
                    public event System.Func<ProcessEventArgs, System.Threading.Tasks.Task> ProcessEventAsync;
                }

                public class ProcessEventArgs { }
            }

            public class TelemetryIngester
            {
                private readonly AppDbContext _db;

                public TelemetryIngester(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(Azure.Messaging.EventHubs.Processor.EventProcessorClient client)
                {
                    client.ProcessEventAsync += async args =>
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task PerInvocationSemaphore_StillReports()
    {
        // Each invocation creates its own semaphore, so the bracket serializes nothing.
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += async args =>
                    {
                        var gate = new SemaphoreSlim(1, 1);
                        await gate.WaitAsync();
                        try
                        {
                            {|DI021:_db|}.Add(args);
                            await _db.SaveChangesAsync();
                        }
                        finally
                        {
                            gate.Release();
                        }
                    };
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task InterlockedGuard_UseBeforeGuard_StillReports()
    {
        // The shared access before the reentrancy check is not protected by it.
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;
                private int _running;
                private Timer _timer;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    _timer = new Timer(Poll, null, 0, 1000);
                }

                private void Poll(object state)
                {
                    {|DI021:_db|}.Add(state);
                    if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
                    {
                        return;
                    }

                    try
                    {
                        _db.SaveChanges();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _running, 0);
                    }
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task TimersTimer_AutoResetFalseThenTrue_StillReports()
    {
        // The later AutoReset = true re-enables overlapping callbacks; one safe write is not proof.
        var source = BaseUsings + EfCoreStubs + """
            public class Poller
            {
                private readonly AppDbContext _db;

                public Poller(AppDbContext db)
                {
                    _db = db;
                }

                public void Start()
                {
                    var timer = new System.Timers.Timer(5000);
                    timer.AutoReset = false;
                    timer.AutoReset = true;
                    timer.Elapsed += (sender, args) =>
                    {
                        {|DI021:_db|}.Add(args);
                        _db.SaveChanges();
                    };
                    timer.Start();
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task LocalFunctionHandler_CapturedField_ReportsDiagnostic()
    {
        var source = ServiceBusUsing + BaseUsings + ServiceBusStubs + EfCoreStubs + """
            public class Worker
            {
                private readonly AppDbContext _db;

                public Worker(AppDbContext db)
                {
                    _db = db;
                }

                public void Start(ServiceBusSessionProcessor processor)
                {
                    processor.ProcessMessageAsync += HandleAsync;

                    async Task HandleAsync(ProcessSessionMessageEventArgs args)
                    {
                        {|DI021:_db|}.Add(args);
                        await _db.SaveChangesAsync();
                    }
                }
            }
            """;

        await VerifyAsync(source);
    }

    [Fact]
    public async Task PeriodicTimerLoop_ScopePerTick_NoDiagnostic()
    {
        var source = HostingUsing + BaseUsings + EfCoreStubs + HostingStubs + """
            public class Worker : BackgroundService
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public Worker(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
                    while (await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Add("tick");
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
            }
            """;

        await VerifyNoneAsync(source);
    }
}
