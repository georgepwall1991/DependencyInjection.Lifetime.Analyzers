using Xunit;
using Xunit.Abstractions;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

/// <summary>
/// Integration tests that rebuild sample projects, consume SARIF output, and verify
/// that claimed diagnostics match the approved contract. These tests confirm that
/// sample drift is detectable from the standard test run.
/// </summary>
public class SampleDiagnosticsVerifierTests
{
    private readonly ITestOutputHelper _output;

    public SampleDiagnosticsVerifierTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Verifies that SampleApp rebuilds cleanly and all claimed DI diagnostics match
    /// the approved contract. Extra or missing claimed diagnostics fail the check.
    /// Unrelated compiler/code-analysis diagnostics and approved secondary diagnostics
    /// are not counted against the contract.
    /// </summary>
    [Fact]
    public void SampleApp_ClaimedDiagnosticsMatchContract()
    {
        var result = SampleDiagnosticsVerifier.VerifySampleApp();
        _output.WriteLine(result.Message);
        Assert.True(result.IsSuccess, result.Message);
    }

    /// <summary>
    /// Verifies that DI015InAction keeps its broken-vs-fixed contrast:
    /// the intentionally broken configuration still reports the documented plain and
    /// keyed DI015 cases, while the fixed configuration stays clean for DI015.
    /// </summary>
    [Fact]
    public void DI015InAction_BrokenCasesReportWhileFixedStaysClean()
    {
        var result = SampleDiagnosticsVerifier.VerifyDI015InAction();
        _output.WriteLine(result.Message);
        Assert.True(result.IsSuccess, result.Message);
    }
}
