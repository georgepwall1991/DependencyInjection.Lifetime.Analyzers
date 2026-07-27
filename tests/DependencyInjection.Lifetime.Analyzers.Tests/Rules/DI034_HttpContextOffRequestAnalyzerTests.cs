using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI034_HttpContextOffRequestAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using Microsoft.Extensions.DependencyInjection;

        namespace Microsoft.AspNetCore.Http
        {
            public abstract class HttpContext
            {
                public abstract string TraceIdentifier { get; }
            }

            public interface IHttpContextAccessor
            {
                HttpContext? HttpContext { get; set; }
            }
        }

        """;

    [Fact]
    public async Task HttpContextParameter_CapturedByDiscardedTaskRun_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        _ = Task.Run(() => Console.WriteLine({|DI034:context|}.TraceIdentifier));
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task AccessorReadInsideBackgroundWork_ReportsDiagnostic()
    {
        // Reading the accessor from inside the work is no better: the AsyncLocal has moved on.
        var source =
            Usings
            + """
                public class Handler
                {
                    private readonly IHttpContextAccessor _accessor;

                    public Handler(IHttpContextAccessor accessor)
                    {
                        _accessor = accessor;
                    }

                    public void Handle()
                    {
                        _ = Task.Run(() => Console.WriteLine({|DI034:_accessor.HttpContext|}!.TraceIdentifier));
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task HttpContextLocal_CapturedByTaskFactoryStartNew_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Handler
                {
                    private readonly IHttpContextAccessor _accessor;

                    public Handler(IHttpContextAccessor accessor)
                    {
                        _accessor = accessor;
                    }

                    public void Handle()
                    {
                        var context = _accessor.HttpContext!;
                        Task.Factory.StartNew(() => Console.WriteLine({|DI034:context|}.TraceIdentifier));
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task AwaitedBackgroundWork_NoDiagnostic()
    {
        // Awaiting keeps the request — and the context — alive until the work finishes.
        var source =
            Usings
            + """
                public class Handler
                {
                    public async Task HandleAsync(HttpContext context)
                    {
                        await Task.Run(() => Console.WriteLine(context.TraceIdentifier));
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task ValuesCopiedBeforeBackgroundWork_NoDiagnostic()
    {
        // The documented fix: take what you need out of the context first.
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        var traceId = context.TraceIdentifier;
                        _ = Task.Run(() => Console.WriteLine(traceId));
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task SynchronouslyWaitedBackgroundWork_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        Task.Run(() => Console.WriteLine(context.TraceIdentifier)).Wait();
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task BackgroundWorkWithoutHttpContext_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(string message)
                    {
                        _ = Task.Run(() => Console.WriteLine(message));
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyNoDiagnosticsAsync(
            source
        );
    }

    [Fact]
    public async Task NameOfHttpContext_NoDiagnostic()
    {
        // nameof compiles to a constant string; nothing is captured or read.
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        _ = Task.Run(() => Console.WriteLine(nameof(context)));
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task WaitWithTimeout_ReportsDiagnostic()
    {
        // Wait(timeout) can return while the work is still running, so it proves nothing.
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        Task.Run(() => Console.WriteLine({|DI034:context|}.TraceIdentifier)).Wait(0);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StoredFiniteWaitResult_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        var completed = Task.Run(
                            () => Console.WriteLine({|DI034:context|}.TraceIdentifier)
                        ).Wait(TimeSpan.Zero);
                        GC.KeepAlive(completed);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ReturnedCancelableWaitResult_ReportsDiagnostic()
    {
        var source =
            Usings
            + """
                public class Handler
                {
                    public bool Handle(
                        HttpContext context,
                        System.Threading.CancellationToken cancellationToken)
                    {
                        return Task.Run(
                            () => Console.WriteLine({|DI034:context|}.TraceIdentifier)
                        ).Wait(System.Threading.Timeout.Infinite, cancellationToken);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task StoredUserDefinedWaitResult_NoDiagnostic()
    {
        var source =
            Usings
            + """
                public static class TaskExtensions
                {
                    public static bool Wait(this Task task, string mode)
                    {
                        task.GetAwaiter().GetResult();
                        return true;
                    }
                }

                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        var completed = Task.Run(
                            () => Console.WriteLine(context.TraceIdentifier)
                        ).Wait("complete");
                        GC.KeepAlive(completed);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task GenericTaskFactory_ReportsDiagnostic()
    {
        // A constructed TaskFactory<int> is still a TaskFactory.
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        new TaskFactory<int>().StartNew(() => {|DI034:context|}.TraceIdentifier.Length);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task WaitWithInfiniteTimeout_NoDiagnostic()
    {
        // Timeout.Infinite blocks exactly as the parameterless overload does.
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        Task.Run(() => Console.WriteLine(context.TraceIdentifier))
                            .Wait(System.Threading.Timeout.Infinite);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task WaitWithInfiniteTimeoutAndToken_NoDiagnostic()
    {
        // The timeout decides; an infinite one blocks regardless of the token argument.
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        Task.Run(() => Console.WriteLine(context.TraceIdentifier))
                            .Wait(System.Threading.Timeout.Infinite, System.Threading.CancellationToken.None);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task WaitWithNamedDefaultTimeout_ReportsDiagnostic()
    {
        // `millisecondsTimeout: default` is zero, not infinite.
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        Task.Run(() => Console.WriteLine({|DI034:context|}.TraceIdentifier))
                            .Wait(millisecondsTimeout: default);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyDiagnosticsAsync(source);
    }

    [Fact]
    public async Task WaitWithReorderedNamedArguments_NoDiagnostic()
    {
        // Named arguments in either order bind to the same parameters.
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context)
                    {
                        Task.Run(() => Console.WriteLine(context.TraceIdentifier))
                            .Wait(
                                cancellationToken: System.Threading.CancellationToken.None,
                                millisecondsTimeout: System.Threading.Timeout.Infinite);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task WaitWithCancelableToken_ReportsDiagnostic()
    {
        // A cancelable token can end the wait while the work keeps running.
        var source =
            Usings
            + """
                public class Handler
                {
                    public void Handle(HttpContext context, System.Threading.CancellationToken token)
                    {
                        Task.Run(() => Console.WriteLine({|DI034:context|}.TraceIdentifier))
                            .Wait(System.Threading.Timeout.Infinite, token);
                    }
                }
                """;

        await AnalyzerVerifier<DI034_HttpContextOffRequestAnalyzer>.VerifyDiagnosticsAsync(source);
    }
}
