using Xunit;
using DependencyInjection.Lifetime.Analyzers.Scan;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Scan;

public sealed class ScanFilterTests
{
    [Theory]
    [InlineData("tests/**", "tests/App.Tests/Foo.cs", false)]
    [InlineData("tests/**", "src/App/Foo.cs", true)]
    [InlineData("**/Migrations/**", "src/Data/Migrations/Initial.cs", false)]
    [InlineData("Migrations", "src/Data/Migrations/Initial.cs", false)]
    [InlineData("*.Designer.cs", "src/Data/Form.Designer.cs", false)]
    [InlineData("*.Designer.cs", "src/Data/Form.cs", true)]
    [InlineData("**/*.g.cs", "src/Generated/Form.g.cs", false)]
    public void Glob_ExcludesRelativePaths(string glob, string path, bool included)
    {
        var filter = new ScanFilter([], [], [glob]);
        var finding = new Finding("DI003", "Warning", "", path, 1, 1);
        Assert.Equal(included, filter.Includes(finding));
    }

    [Fact]
    public void RulesAndSkippedRules_Combine()
    {
        var filter = new ScanFilter(["DI003", "DI016"], ["DI016"], []);
        Assert.True(filter.Includes(new Finding("DI003", "Warning", "", "a.cs", 1, 1)));
        Assert.False(filter.Includes(new Finding("DI016", "Warning", "", "a.cs", 1, 1)));
        Assert.False(filter.Includes(new Finding("DI019", "Warning", "", "a.cs", 1, 1)));
    }

    [Fact]
    public void EmptyFilter_IncludesEverything()
    {
        var filter = new ScanFilter([], [], []);
        Assert.True(filter.IsEmpty);
        Assert.True(filter.Includes(new Finding("DI003", "Warning", "", "a.cs", 1, 1)));
    }
}

