using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public class SampleDiagnosticsVerifierContractTests
{
    [Fact]
    public void StaleClaimReportsObservedDiagnosticAtMovedLocation()
    {
        using var temp = new TemporaryDirectory();
        var sample = CreateSampleFile(temp.Path);

        var sarif = new List<SarifResult>
        {
            new(
                0,
                "DI999",
                "warning",
                "Claim moved to safe line",
                new Uri(sample.SourcePath).AbsoluteUri,
                sample.SafeLine)
        };

        var contract = new SampleContract
        {
            FolderClaims =
            [
                new FolderClaim
                {
                    Folder = "DI999",
                    RuleId = "DI999",
                    Claims =
                    [
                        new DiagnosticClaim
                        {
                            FilePathContains = "Diagnostics/DI999/Sample.cs",
                            Anchor = "var broken = 1;",
                            RuleId = "DI999",
                            Severity = "warning"
                        }
                    ]
                }
            ]
        };

        var result = SampleDiagnosticsVerifier.VerifyContract(sarif, contract, "Synthetic");

        Assert.False(result.IsSuccess);
        Assert.Contains("Stale claim", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sample.SafeLine.ToString(), result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Claim moved to safe line", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeverityMismatchReportsObservedSeverity()
    {
        using var temp = new TemporaryDirectory();
        var sample = CreateSampleFile(temp.Path);

        var sarif = new List<SarifResult>
        {
            new(
                0,
                "DI999",
                "note",
                "Claim severity changed",
                new Uri(sample.SourcePath).AbsoluteUri,
                sample.BrokenLine)
        };

        var contract = new SampleContract
        {
            FolderClaims =
            [
                new FolderClaim
                {
                    Folder = "DI999",
                    RuleId = "DI999",
                    Claims =
                    [
                        new DiagnosticClaim
                        {
                            FilePathContains = "Diagnostics/DI999/Sample.cs",
                            Anchor = "var broken = 1;",
                            RuleId = "DI999",
                            Severity = "warning"
                        }
                    ]
                }
            ]
        };

        var result = SampleDiagnosticsVerifier.VerifyContract(sarif, contract, "Synthetic");

        Assert.False(result.IsSuccess);
        Assert.Contains("warning", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("note", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Claim severity changed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnexpectedSecondaryDiagnosticInClaimedFolderFailsVerification()
    {
        using var temp = new TemporaryDirectory();
        var sample = CreateSampleFile(temp.Path);

        var sarif = new List<SarifResult>
        {
            new(
                0,
                "DI999",
                "warning",
                "Claimed diagnostic",
                new Uri(sample.SourcePath).AbsoluteUri,
                sample.BrokenLine),
            new(
                1,
                "DI998",
                "warning",
                "Unexpected secondary diagnostic",
                new Uri(sample.SourcePath).AbsoluteUri,
                sample.SurpriseLine)
        };

        var contract = new SampleContract
        {
            FolderClaims =
            [
                new FolderClaim
                {
                    Folder = "DI999",
                    RuleId = "DI999",
                    Claims =
                    [
                        new DiagnosticClaim
                        {
                            FilePathContains = "Diagnostics/DI999/Sample.cs",
                            Anchor = "var broken = 1;",
                            RuleId = "DI999",
                            Severity = "warning"
                        }
                    ]
                }
            ]
        };

        var result = SampleDiagnosticsVerifier.VerifyContract(sarif, contract, "Synthetic");

        Assert.False(result.IsSuccess);
        Assert.Contains("DI998", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unexpected", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApprovedSecondaryDiagnosticStillPasses()
    {
        using var temp = new TemporaryDirectory();
        var sample = CreateSampleFile(temp.Path);

        var sarif = new List<SarifResult>
        {
            new(
                0,
                "DI999",
                "warning",
                "Claimed diagnostic",
                new Uri(sample.SourcePath).AbsoluteUri,
                sample.BrokenLine),
            new(
                1,
                "DI998",
                "warning",
                "Approved secondary diagnostic",
                new Uri(sample.SourcePath).AbsoluteUri,
                sample.SurpriseLine)
        };

        var contract = new SampleContract
        {
            FolderClaims =
            [
                new FolderClaim
                {
                    Folder = "DI999",
                    RuleId = "DI999",
                    Claims =
                    [
                        new DiagnosticClaim
                        {
                            FilePathContains = "Diagnostics/DI999/Sample.cs",
                            Anchor = "var broken = 1;",
                            RuleId = "DI999",
                            Severity = "warning"
                        }
                    ],
                    ApprovedSecondaryDiagnostics =
                    [
                        new DiagnosticClaim
                        {
                            FilePathContains = "Diagnostics/DI999/Sample.cs",
                            Anchor = "var surprise = 3;",
                            RuleId = "DI998",
                            Severity = "warning"
                        }
                    ]
                }
            ]
        };

        var result = SampleDiagnosticsVerifier.VerifyContract(sarif, contract, "Synthetic");

        Assert.True(result.IsSuccess, result.Message);
    }

    private static SampleSource CreateSampleFile(string root)
    {
        var sourcePath = Path.Combine(root, "Diagnostics", "DI999", "Sample.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);

        File.WriteAllText(
            sourcePath,
            """
namespace Synthetic.Diagnostics.DI999;

public static class Sample
{
    public static void Example()
    {
        var broken = 1;
        var safe = 2;
        var surprise = 3;
    }
}
""");

        var lines = File.ReadAllLines(sourcePath);
        var brokenLine = Array.FindIndex(lines, line => line.Contains("var broken = 1;", StringComparison.Ordinal)) + 1;
        var safeLine = Array.FindIndex(lines, line => line.Contains("var safe = 2;", StringComparison.Ordinal)) + 1;
        var surpriseLine = Array.FindIndex(lines, line => line.Contains("var surprise = 3;", StringComparison.Ordinal)) + 1;

        return new SampleSource(sourcePath, brokenLine, safeLine, surpriseLine);
    }

    private sealed record SampleSource(string SourcePath, int BrokenLine, int SafeLine, int SurpriseLine);

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sample-contract-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
