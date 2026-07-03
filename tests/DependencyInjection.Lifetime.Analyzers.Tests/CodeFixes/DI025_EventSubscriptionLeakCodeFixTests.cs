using System;
using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

/// <summary>
/// Tests for the DI025 code fix: insert the mirrored -= at the top of an existing Dispose
/// method when the handler is a method group whose receiver resolves inside Dispose.
/// </summary>
public class DI025_EventSubscriptionLeakCodeFixTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        public interface IBus
        {
            event EventHandler MessageReceived;
        }

        public class Bus : IBus
        {
            public event EventHandler MessageReceived;
        }

        public static class Registrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddSingleton<IBus, Bus>();
                services.AddTransient<OrderHandler>();
            }
        }

        """;

    [Fact]
    public async Task CodeFix_InsertsUnsubscribe_AtTopOfExistingDispose()
    {
        var source = Usings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;
                private bool _disposed;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnMessage|];
                }

                public void Dispose()
                {
                    _disposed = true;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = Usings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;
                private bool _disposed;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public void Dispose()
                {
                    _bus.MessageReceived -= OnMessage;
                    _disposed = true;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, "DI025_AddUnsubscribeInDispose");
    }

    [Fact]
    public async Task CodeFix_InsertsUnsubscribe_InDisposeAsync()
    {
        var source = Usings + """
            public class OrderHandler : IAsyncDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnMessage|];
                }

                public System.Threading.Tasks.ValueTask DisposeAsync()
                {
                    return default;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = Usings + """
            public class OrderHandler : IAsyncDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public System.Threading.Tasks.ValueTask DisposeAsync()
                {
                    _bus.MessageReceived -= OnMessage;
                    return default;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, "DI025_AddUnsubscribeInDispose");
    }

    [Fact]
    public async Task CodeFix_PrefersParameterlessDispose_OverDisposeBool()
    {
        var source = Usings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnMessage|];
                }

                public void Dispose()
                {
                    Dispose(true);
                }

                protected virtual void Dispose(bool disposing)
                {
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = Usings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public void Dispose()
                {
                    _bus.MessageReceived -= OnMessage;
                    Dispose(true);
                }

                protected virtual void Dispose(bool disposing)
                {
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, "DI025_AddUnsubscribeInDispose");
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenTypeHasNoDisposeMethod()
    {
        var source = Usings + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", "DI025_AddUnsubscribeInDispose");
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenDisposeMethodExistsWithoutDisposableContract()
    {
        // A method merely named Dispose on a type that does not implement IDisposable is
        // never called by the container; inserting -= there would fake a repair.
        var source = Usings + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public void Dispose()
                {
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", "DI025_AddUnsubscribeInDispose");
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForAnonymousHandler()
    {
        var source = Usings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;
                private int _count;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += (sender, e) => _count++;
                }

                public void Dispose()
                {
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", "DI025_AddUnsubscribeInDispose");
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenReceiverIsConstructorParameter()
    {
        var source = Usings + """
            public class OrderHandler : IDisposable
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                public void Dispose()
                {
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", "DI025_AddUnsubscribeInDispose");
    }

    // ----------------------------------------------------------------
    // DI026 (scoped-publisher Info tier): the mirrored -= insertion is
    // lifetime-agnostic and applies identically.
    // ----------------------------------------------------------------

    private const string ScopedUsings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        public interface IBus
        {
            event EventHandler MessageReceived;
        }

        public class Bus : IBus
        {
            public event EventHandler MessageReceived;
        }

        public static class Registrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddScoped<IBus, Bus>();
                services.AddTransient<OrderHandler>();
            }
        }

        """;

    [Fact]
    public async Task CodeFix_InsertsUnsubscribe_ForScopedPublisherInfoTier()
    {
        var source = ScopedUsings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    {|DI026:_bus.MessageReceived += OnMessage|};
                }

                public void Dispose()
                {
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = ScopedUsings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public void Dispose()
                {
                    _bus.MessageReceived -= OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, "DI025_AddUnsubscribeInDispose");
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForScopedPublisherCtorParameterReceiver()
    {
        var source = ScopedUsings + """
            public class OrderHandler : IDisposable
            {
                public OrderHandler(IBus bus)
                {
                    bus.MessageReceived += OnMessage;
                }

                public void Dispose()
                {
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI026", "DI025_AddUnsubscribeInDispose");
    }

    // ----------------------------------------------------------------
    // Chained receivers: the mirrored -= re-resolves the same chain in
    // Dispose when the chain root is an instance field or property.
    // ----------------------------------------------------------------

    private const string ChainedUsings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        public class Inner
        {
            public event EventHandler Changed;
        }

        public class Outer
        {
            public Inner Inner { get; } = new Inner();
        }

        public static class Registrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddSingleton<Outer>();
                services.AddTransient<OrderHandler>();
            }
        }

        """;

    [Fact]
    public async Task CodeFix_InsertsChainedUnsubscribe_ForFieldRootedChain()
    {
        var source = ChainedUsings + """
            public class OrderHandler : IDisposable
            {
                private readonly Outer _outer;

                public OrderHandler(Outer outer)
                {
                    _outer = outer;
                    [|_outer.Inner.Changed += OnChanged|];
                }

                public void Dispose()
                {
                }

                private void OnChanged(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = ChainedUsings + """
            public class OrderHandler : IDisposable
            {
                private readonly Outer _outer;

                public OrderHandler(Outer outer)
                {
                    _outer = outer;
                    _outer.Inner.Changed += OnChanged;
                }

                public void Dispose()
                {
                    _outer.Inner.Changed -= OnChanged;
                }

                private void OnChanged(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, "DI025_AddUnsubscribeInDispose");
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForParenthesizedCtorParameterRootedChain()
    {
        // Parentheses must not hide the constructor-parameter root; the cloned chain would
        // reference the parameter inside Dispose and fail to compile.
        var source = ChainedUsings + """
            public class OrderHandler : IDisposable
            {
                public OrderHandler(Outer outer)
                {
                    (outer.Inner).Changed += OnChanged;
                }

                public void Dispose()
                {
                }

                private void OnChanged(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", "DI025_AddUnsubscribeInDispose");
    }

    [Fact]
    public async Task CodeFix_NotOffered_ForCtorParameterRootedChain()
    {
        // The constructor parameter does not resolve inside Dispose; cloning the
        // chain verbatim would not compile.
        var source = ChainedUsings + """
            public class OrderHandler : IDisposable
            {
                public OrderHandler(Outer outer)
                {
                    outer.Inner.Changed += OnChanged;
                }

                public void Dispose()
                {
                }

                private void OnChanged(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", "DI025_AddUnsubscribeInDispose");
    }

    // ----------------------------------------------------------------
    // Tier 2 — add a Dispose method when the disposal contract already
    // exists (inherited from a base) but no dispose-shaped method is
    // declared in the analyzed type. Only the standard virtual
    // Dispose(bool) pattern is safe: overriding it means our unsubscribe
    // actually runs through the base's Dispose() -> Dispose(true)
    // dispatch. Any inherited shape where an added public Dispose() would
    // never be the method the container calls is refused as a fake repair.
    // ----------------------------------------------------------------

    private const string AddDisposeKey = "DI025_AddDisposeWithUnsubscribe";
    private const string ImplementInterfaceKey = "DI025_ImplementIDisposableWithUnsubscribe";

    [Fact]
    public async Task CodeFix_AddsDisposeBoolOverride_WhenBaseHasVirtualDisposePattern()
    {
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() => Dispose(true);
                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() => Dispose(true);
                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        _bus.MessageReceived -= OnMessage;
                    }

                    base.Dispose(disposing);
                }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenBaseHasNonVirtualDispose()
    {
        // Adding our own Dispose() would hide the base's accessible non-virtual one; refuse.
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_AddDispose_WhenBaseExplicitlyImplementsIDisposable()
    {
        // The base already satisfies IDisposable via an explicit implementation, so the
        // container calls that base method. A new public Dispose() here would never run — a
        // fake repair — so the add-dispose action is refused (no virtual Dispose(bool) hook).
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                void IDisposable.Dispose() { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenBlockBodiedDisposeAlreadyExists()
    {
        // When the analyzed type already has a usable Dispose, the tier-1 insert is the right
        // repair; the add-dispose action must not also fire (it would duplicate the member).
        var source = Usings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                public void Dispose()
                {
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenBaseDisposeBoolIsPublic()
    {
        // A `public virtual void Dispose(bool)` cannot be overridden as `protected` (CS0507),
        // and mirroring `public` would broaden the surface; refuse rather than emit either.
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() => Dispose(true);
                public virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenNearestBaseDisposeBoolIsSealed()
    {
        // The nearest `Dispose(bool)` declaration is a sealed override, so a further-up virtual
        // hook is unreachable; the fix must evaluate the nearest declaration and refuse.
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() => Dispose(true);
                protected virtual void Dispose(bool disposing) { }
            }

            public class MidBase : DisposableBase
            {
                protected sealed override void Dispose(bool disposing) => base.Dispose(disposing);
            }

            public class OrderHandler : MidBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenBaseDisposeBoolIsAbstract()
    {
        // An abstract `Dispose(bool)` hook cannot be chained to with `base.Dispose(disposing)`;
        // refuse so the fix never emits a call to an abstract base member.
        var source = Usings + """
            public abstract class DisposableBase : IDisposable
            {
                public void Dispose() => Dispose(true);
                protected abstract void Dispose(bool disposing);
            }

            public abstract class HandlerBase : DisposableBase
            {
                private readonly IBus _bus;

                protected HandlerBase(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }

            public class OrderHandler : HandlerBase
            {
                public OrderHandler(IBus bus) : base(bus) { }

                protected override void Dispose(bool disposing) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenBaseDisposeDoesNotDispatchToHook()
    {
        // The base's `Dispose()` never calls `Dispose(bool)`, so an added override would never
        // run — a fake repair. Refuse unless the pattern actually dispatches to the hook.
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() { }
                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenBaseDisposeDispatchesToAnotherObject()
    {
        // `_inner.Dispose(true)` disposes a different object, not the base's own virtual hook,
        // so the added override would never run. Only a bare `Dispose(...)`/`this.Dispose(...)`
        // counts as dispatch.
        var source = Usings + """
            public class Inner
            {
                public void Dispose(bool disposing) { }
            }

            public class DisposableBase : IDisposable
            {
                private readonly Inner _inner = new Inner();
                public void Dispose() { _inner.Dispose(true); }
                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenHookReturnsNonVoid()
    {
        // `protected virtual int Dispose(bool)` is not the standard pattern; emitting a void
        // override against it would not compile.
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() { Dispose(true); }
                protected virtual int Dispose(bool disposing) => 0;
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenDispatchOnlyInsideNestedFunction()
    {
        // The only Dispose(true) call sits inside a local function the base's Dispose()
        // never invokes; the dispatch proof must not descend into nested bodies.
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose()
                {
                    void Cleanup() => Dispose(true);
                }

                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenBaseDisposeDispatchesFalse()
    {
        // `Dispose(false)` runs the managed-cleanup branch as if finalizing, so the generated
        // `if (disposing)` guard would never fire on the container path. Require literal `true`.
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() => Dispose(false);
                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenBaseDeclaresPatternButNotIDisposable()
    {
        // The base has the Dispose()/Dispose(bool) shape but does NOT implement IDisposable, so
        // the container never disposes the service — overriding the hook is a fake repair.
        var source = Usings + """
            public class DisposableBase
            {
                public void Dispose() => Dispose(true);
                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    [Fact]
    public async Task CodeFix_AddDispose_NotOffered_WhenBaseDisposeDispatchesToADifferentHook()
    {
        // The dispatching Dispose() binds Dispose(true) to the grandparent's own private
        // Dispose(bool); the overridable hook found on the intermediate base is a SEPARATE
        // method the container's dispose path never reaches — so the override would never run.
        var source = Usings + """
            public class GrandBase : IDisposable
            {
                public void Dispose() { Dispose(true); }
                private void Dispose(bool disposing) { }
            }

            public class MidBase : GrandBase
            {
                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : MidBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", AddDisposeKey);
    }

    // ----------------------------------------------------------------
    // Tier 3 — implement IDisposable outright, but only for
    // scoped-registered subscribers (a scope deterministically disposes
    // them). Transients would walk into DI008 territory, so stay refused.
    // ----------------------------------------------------------------

    private const string ScopedSubscriberUsings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;

        public interface IBus
        {
            event EventHandler MessageReceived;
        }

        public class Bus : IBus
        {
            public event EventHandler MessageReceived;
        }

        public static class Registrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddSingleton<IBus, Bus>();
                services.AddScoped<OrderHandler>();
            }
        }

        """;

    [Fact]
    public async Task CodeFix_ImplementsIDisposable_ForScopedRegisteredSubscriber()
    {
        var source = ScopedSubscriberUsings + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnMessage|];
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = ScopedSubscriberUsings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }

                public void Dispose()
                {
                    _bus.MessageReceived -= OnMessage;
                }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, ImplementInterfaceKey);
    }

    [Fact]
    public async Task CodeFix_ImplementsIDisposable_QualifiesInterface_WhenLocalIDisposableCollides()
    {
        // A namespace-local `IDisposable` shadows `System.IDisposable`, so an unqualified base
        // entry would bind to the wrong type. The fix must emit the fully-qualified name.
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace MyApp
            {
                public interface IDisposable { }

                public interface IBus
                {
                    event EventHandler MessageReceived;
                }

                public class Bus : IBus
                {
                    public event EventHandler MessageReceived;
                }

                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IBus, Bus>();
                        services.AddScoped<OrderHandler>();
                    }
                }

                public class OrderHandler
                {
                    private readonly IBus _bus;

                    public OrderHandler(IBus bus)
                    {
                        _bus = bus;
                        [|_bus.MessageReceived += OnMessage|];
                    }

                    private void OnMessage(object sender, EventArgs e) { }
                }
            }
            """;

        var fixedSource = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace MyApp
            {
                public interface IDisposable { }

                public interface IBus
                {
                    event EventHandler MessageReceived;
                }

                public class Bus : IBus
                {
                    public event EventHandler MessageReceived;
                }

                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddSingleton<IBus, Bus>();
                        services.AddScoped<OrderHandler>();
                    }
                }

                public class OrderHandler : System.IDisposable
                {
                    private readonly IBus _bus;

                    public OrderHandler(IBus bus)
                    {
                        _bus = bus;
                        _bus.MessageReceived += OnMessage;
                    }

                    private void OnMessage(object sender, EventArgs e) { }

                    public void Dispose()
                    {
                        _bus.MessageReceived -= OnMessage;
                    }
                }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, ImplementInterfaceKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_ImplementInterface_ForTransientSubscriber()
    {
        // A transient that becomes IDisposable is exactly the DI008 disposable-transient-capture
        // shape; the fix must never trade a DI025 for a DI008.
        var source = Usings + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", ImplementInterfaceKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_ImplementInterface_ForMixedTransientAndScopedSubscriber()
    {
        // Registered BOTH transient and scoped: the still-live transient registration makes an
        // added IDisposable the DI008 shape, so implement-interface must not be offered even
        // though the max-rank lifetime is scoped.
        var mixedUsings = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public interface IBus
            {
                event EventHandler MessageReceived;
            }

            public class Bus : IBus
            {
                public event EventHandler MessageReceived;
            }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IBus, Bus>();
                    services.AddTransient<OrderHandler>();
                    services.AddScoped<OrderHandler>();
                }
            }

            """;

        var source = mixedUsings + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", ImplementInterfaceKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_ImplementInterface_ForAnonymousHandler()
    {
        var source = ScopedSubscriberUsings + """
            public class OrderHandler
            {
                private readonly IBus _bus;
                private int _count;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += (sender, e) => _count++;
                }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI025", ImplementInterfaceKey);
    }

    [Fact]
    public async Task CodeFix_NotOffered_ImplementInterface_ForDI026TransientSubscriber()
    {
        // DI026 only ever fires for transient subscribers, so the implement-interface tier is
        // never appropriate there.
        var source = ScopedUsings + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnMessage;
                }

                private void OnMessage(object sender, EventArgs e) { }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixNotOfferedAsync(source, diagnostic => diagnostic.Id == "DI026", ImplementInterfaceKey);
    }

    [Fact]
    public async Task CodeFix_ImplementsIDisposable_ForScopedSubscriber_WithChainedReceiver()
    {
        var chainedScopedUsings = """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            public class Inner
            {
                public event EventHandler Changed;
            }

            public class Outer
            {
                public Inner Inner { get; } = new Inner();
            }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<Outer>();
                    services.AddScoped<OrderHandler>();
                }
            }

            """;

        var source = chainedScopedUsings + """
            public class OrderHandler
            {
                private readonly Outer _outer;

                public OrderHandler(Outer outer)
                {
                    _outer = outer;
                    [|_outer.Inner.Changed += OnChanged|];
                }

                private void OnChanged(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = chainedScopedUsings + """
            public class OrderHandler : IDisposable
            {
                private readonly Outer _outer;

                public OrderHandler(Outer outer)
                {
                    _outer = outer;
                    _outer.Inner.Changed += OnChanged;
                }

                private void OnChanged(object sender, EventArgs e) { }

                public void Dispose()
                {
                    _outer.Inner.Changed -= OnChanged;
                }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, ImplementInterfaceKey);
    }

    // ----------------------------------------------------------------
    // Fix-all — a type carrying several leaks must yield ONE disposal
    // member holding every -=, not one synthesized member per diagnostic.
    // ----------------------------------------------------------------

    [Fact]
    public async Task CodeFix_FixAll_TwoSubscriptions_SynthesizesSingleDisposeWithBothUnsubscribes()
    {
        var source = ScopedSubscriberUsings + """
            public class OrderHandler
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnA|];
                    [|_bus.MessageReceived += OnB|];
                }

                private void OnA(object sender, EventArgs e) { }
                private void OnB(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = ScopedSubscriberUsings + """
            public class OrderHandler : IDisposable
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnA;
                    _bus.MessageReceived += OnB;
                }

                private void OnA(object sender, EventArgs e) { }
                private void OnB(object sender, EventArgs e) { }

                public void Dispose()
                {
                    _bus.MessageReceived -= OnA;
                    _bus.MessageReceived -= OnB;
                }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, ImplementInterfaceKey);
    }

    [Fact]
    public async Task CodeFix_FixAll_TwoSubscriptions_SynthesizesSingleDisposeBoolOverride()
    {
        var source = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() => Dispose(true);
                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    [|_bus.MessageReceived += OnA|];
                    [|_bus.MessageReceived += OnB|];
                }

                private void OnA(object sender, EventArgs e) { }
                private void OnB(object sender, EventArgs e) { }
            }
            """;

        var fixedSource = Usings + """
            public class DisposableBase : IDisposable
            {
                public void Dispose() => Dispose(true);
                protected virtual void Dispose(bool disposing) { }
            }

            public class OrderHandler : DisposableBase
            {
                private readonly IBus _bus;

                public OrderHandler(IBus bus)
                {
                    _bus = bus;
                    _bus.MessageReceived += OnA;
                    _bus.MessageReceived += OnB;
                }

                private void OnA(object sender, EventArgs e) { }
                private void OnB(object sender, EventArgs e) { }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        _bus.MessageReceived -= OnA;
                        _bus.MessageReceived -= OnB;
                    }

                    base.Dispose(disposing);
                }
            }
            """;

        await CodeFixVerifier<DI025_EventSubscriptionLeakAnalyzer, DI025_EventSubscriptionLeakCodeFixProvider>
            .VerifyCodeFixAsync(source, [], fixedSource, AddDisposeKey);
    }
}
