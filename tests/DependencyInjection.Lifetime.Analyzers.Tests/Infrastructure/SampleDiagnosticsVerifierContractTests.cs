using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public class SampleDiagnosticsVerifierContractTests
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

    /// <summary>
    /// Every public diagnostic ID (DI001-DI016) must have at least one folder claim
    /// in the SampleApp contract with at least one concrete claim anchor. This prevents
    /// a new diagnostic from being added without corresponding sample coverage, and
    /// catches mislabelled or empty folder claims that would satisfy a weaker check.
    /// </summary>
    [Fact]
    public void EveryPublicDiagnosticId_HasFolderClaimWithAnchorsInContract()
    {
        var publicIds = GetPublicDiagnosticIds();
        var contract = ReadSampleAppContract();

        // A folder claim counts as coverage only if it has at least one concrete
        // claim with a matching rule ID and a non-empty anchor.
        var coveredIds = contract.FolderClaims
            .Where(fc => fc.Claims.Any(c =>
                string.Equals(c.RuleId, fc.RuleId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Anchor)))
            .Select(fc => fc.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = publicIds.Where(id => !coveredIds.Contains(id)).ToList();

        Assert.True(
            missing.Count == 0,
            $"The following public diagnostic IDs have no folder claim with concrete anchors in " +
            $"sample-diagnostics-contract.json: {string.Join(", ", missing)}. " +
            $"Add sample coverage and a contract entry for each.");
    }

    /// <summary>
    /// Every folder claim in the contract must reference an existing sample directory
    /// under samples/SampleApp/Diagnostics/. This detects stale folder claims that
    /// reference removed or renamed sample directories.
    /// </summary>
    [Fact]
    public void ContractFolderClaims_ReferenceExistingSampleDirectories()
    {
        var contract = ReadSampleAppContract();
        var diagnosticsDir = Path.Combine(RepoRoot, "samples", "SampleApp", "Diagnostics");

        var missing = new List<string>();
        foreach (var folder in contract.FolderClaims.Select(fc => fc.Folder))
        {
            var dirPath = Path.Combine(diagnosticsDir, folder);
            if (!Directory.Exists(dirPath))
                missing.Add(folder);
        }

        Assert.True(
            missing.Count == 0,
            $"Contract folder claims reference non-existent sample directories: " +
            $"{string.Join(", ", missing)}. Update the contract or create the missing directories.");
    }

    /// <summary>
    /// Every rule ID referenced in contract claims must exist in the public diagnostic
    /// inventory (DiagnosticIds). This prevents the contract from referencing
    /// non-existent or mistyped rule IDs.
    /// </summary>
    [Fact]
    public void AllClaimedDiagnosticIds_ExistInPublicInventory()
    {
        var publicIds = GetAllDiagnosticIdValues();
        var contract = ReadSampleAppContract();

        var claimedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fc in contract.FolderClaims)
        {
            claimedIds.Add(fc.RuleId);
            foreach (var claim in fc.Claims)
                claimedIds.Add(claim.RuleId);
            foreach (var sec in fc.ApprovedSecondaryDiagnostics)
                claimedIds.Add(sec.RuleId);
        }
        foreach (var fc in contract.FileClaims)
            claimedIds.Add(fc.RuleId);
        foreach (var ac in contract.AbsenceClaims)
            claimedIds.Add(ac.RuleId);

        var unknown = claimedIds.Where(id => !publicIds.Contains(id)).ToList();

        Assert.True(
            unknown.Count == 0,
            $"Contract references diagnostic IDs not found in DiagnosticIds: " +
            $"{string.Join(", ", unknown)}. Fix the contract or add the missing ID constants.");
    }

    /// <summary>
    /// When a claim references a file path that cannot be resolved at all,
    /// verification should fail with a clear stale-claim message. Uses a
    /// GUID-based synthetic path to prevent accidental collision with real files.
    /// </summary>
    [Fact]
    public void ContractClaim_WithDeletedSampleFile_FailsVerification()
    {
        var sarif = new List<SarifResult>();

        var contract = new SampleContract
        {
            FolderClaims =
            [
                new FolderClaim
                {
                    Folder = "SYNTHETIC_DELETED_00000000",
                    RuleId = "DI999",
                    Claims =
                    [
                        new DiagnosticClaim
                        {
                            FilePathContains = "SYNTHETIC_DELETED_00000000/NonExistentFile_00000000.cs",
                            Anchor = "var deleted = true;",
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
    }

    /// <summary>
    /// Returns all unique public diagnostic IDs matching the DI\d{3} pattern
    /// (e.g., DI001-DI016), excluding sub-variants like DI012b.
    /// </summary>
    private static HashSet<string> GetPublicDiagnosticIds()
    {
        return GetAllDiagnosticIdValues()
            .Where(id => Regex.IsMatch(id, @"^DI\d{3}$"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns all diagnostic ID values from the DiagnosticIds type,
    /// including sub-variants like DI012b.
    /// </summary>
    private static HashSet<string> GetAllDiagnosticIdValues()
    {
        return typeof(DiagnosticIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static SampleContract ReadSampleAppContract()
    {
        var contractPath = Path.Combine(RepoRoot, "samples", "SampleApp", "sample-diagnostics-contract.json");
        var json = File.ReadAllText(contractPath);
        return JsonSerializer.Deserialize<SampleContract>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? throw new InvalidOperationException($"Failed to deserialize contract: {contractPath}");
    }

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
