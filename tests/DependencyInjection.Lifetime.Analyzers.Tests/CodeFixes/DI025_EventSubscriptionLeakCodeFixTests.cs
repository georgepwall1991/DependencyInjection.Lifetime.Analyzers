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
}
