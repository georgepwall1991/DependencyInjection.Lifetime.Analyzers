using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure.CodeFixVerifier<
    DependencyInjection.Lifetime.Analyzers.Rules.DI014_RootProviderNotDisposedAnalyzer,
    DependencyInjection.Lifetime.Analyzers.CodeFixes.DI014_RootProviderNotDisposedCodeFixProvider>;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

public class DI014_RootProviderNotDisposedCodeFixTests
{
    [Fact]
    public async Task Fixes_RootProvider_Not_Disposed()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
    }
}";

        var fixtest = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(9, 24); // Location of BuildServiceProvider() call

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Fixes_Async_Method_With_Await_Using()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public async Task Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
    }
}";

        var fixtest = @"
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public async Task Main()
    {
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(10, 24);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Fixes_Explicit_Type()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider provider = services.BuildServiceProvider();
    }
}";

        var fixtest = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        using ServiceProvider provider = services.BuildServiceProvider();
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(9, 36);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Preserves_Trivia()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        // Create the provider
        var provider = services.BuildServiceProvider(); // End comment
    }
}";

        var fixtest = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        // Create the provider
        using var provider = services.BuildServiceProvider(); // End comment
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(10, 24);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Fixes_Multiple_BuildServiceProvider_Calls()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services1 = new ServiceCollection();
        var provider1 = services1.BuildServiceProvider();

        var services2 = new ServiceCollection();
        var provider2 = services2.BuildServiceProvider();
    }
}";

        var fixtest = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services1 = new ServiceCollection();
        using var provider1 = services1.BuildServiceProvider();

        var services2 = new ServiceCollection();
        using var provider2 = services2.BuildServiceProvider();
    }
}";

        var expected = new[]
        {
            VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(9, 25),
            VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
                .WithLocation(12, 25)
        };

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Fixes_Await_Using_In_Async_Method_With_BuildServiceProvider()
    {
        // DI014 fixer detects async method and uses await using
        var test = @"
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public async Task MainAsync()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        await Task.Delay(100);
    }
}";

        var fixtest = @"
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public async Task MainAsync()
    {
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
        await Task.Delay(100);
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(10, 24);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Fixes_Using_In_Local_Function()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        void SetupServices()
        {
            var services = new ServiceCollection();
            var provider = services.BuildServiceProvider();
        }
    }
}";

        var fixtest = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        void SetupServices()
        {
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();
        }
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(11, 28);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Fixes_Chained_BuildServiceProvider()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
    }
}";

        var fixtest = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(8, 24);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }
}
