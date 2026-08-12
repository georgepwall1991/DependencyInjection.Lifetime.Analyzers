using System.Runtime.CompilerServices;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

/// <summary>
/// Microsoft.CodeAnalysis.Testing restores extra reference packages under
/// Path.GetTempPath()/test-packages. The shared system temp cache races under
/// parallel xUnit and produces NullReferenceException / DirectoryNotFoundException.
/// </summary>
internal static class TestPackageCacheIsolation
{
    [ModuleInitializer]
    internal static void PinTempDirectory()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
        );
        var tempDirectory = Path.Combine(repositoryRoot, ".tmp-test");
        Directory.CreateDirectory(tempDirectory);

        Environment.SetEnvironmentVariable("TMPDIR", tempDirectory);
        Environment.SetEnvironmentVariable("TMP", tempDirectory);
        Environment.SetEnvironmentVariable("TEMP", tempDirectory);
    }
}
