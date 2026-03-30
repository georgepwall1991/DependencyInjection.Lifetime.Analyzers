using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

/// <summary>
/// Verifies sample project diagnostics by rebuilding with the analyzer attached,
/// consuming SARIF output, and matching claimed diagnostics by stable anchors (rule ID
/// and source-folder location). Approved secondary diagnostics for overlapping sample
/// cases are allowed without failing the check.
/// </summary>
public static class SampleDiagnosticsVerifier
{
    // AppContext.BaseDirectory resolves to:
    //   tests/DependencyInjection.Lifetime.Analyzers.Tests/bin/Release/net10.0/
    // Five levels up reaches the repository root.
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

    /// <summary>
    /// Verifies SampleApp diagnostics against its contract.
    /// </summary>
    public static SampleVerificationResult VerifySampleApp()
    {
        var projectPath = Path.Combine(RepoRoot, "samples", "SampleApp", "SampleApp.csproj");
        var contractPath = Path.Combine(RepoRoot, "samples", "SampleApp", "sample-diagnostics-contract.json");
        return VerifyProject(projectPath, contractPath, "SampleApp");
    }

    /// <summary>
    /// Verifies DI015InAction diagnostics against its contract.
    /// </summary>
    public static SampleVerificationResult VerifyDI015InAction()
    {
        var projectPath = Path.Combine(RepoRoot, "samples", "DI015InAction", "DI015InAction.csproj");
        var contractPath = Path.Combine(RepoRoot, "samples", "DI015InAction", "sample-diagnostics-contract.json");
        return VerifyProject(projectPath, contractPath, "DI015InAction");
    }

    private static SampleVerificationResult VerifyProject(
        string projectPath, string contractPath, string projectName)
    {
        if (!File.Exists(projectPath))
            return SampleVerificationResult.Failure($"Project not found: {projectPath}");

        if (!File.Exists(contractPath))
            return SampleVerificationResult.Failure($"Contract file not found: {contractPath}");

        // Build in temp dir to keep repo clean
        var tempDir = Path.Combine(Path.GetTempPath(), $"sample-verifier-{projectName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sarifPath = Path.Combine(tempDir, $"{projectName}.sarif");
            var buildResult = RunDotnetBuild(projectPath, sarifPath);

            if (!buildResult.Success)
                return SampleVerificationResult.Failure(
                    $"Build failed for {projectName}:\n{buildResult.Output}");

            if (!File.Exists(sarifPath))
                return SampleVerificationResult.Failure(
                    $"SARIF file was not produced at: {sarifPath}");

            var sarif = ReadSarif(sarifPath);
            var contract = ReadContract(contractPath);

            return VerifyContract(sarif, contract, projectName);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static BuildOutput RunDotnetBuild(string projectPath, string sarifPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -t:Rebuild --configuration Release" +
                        $" -p:RunAnalyzersDuringBuild=true" +
                        $" \"-p:ErrorLog={sarifPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new BuildOutput(
            Success: process.ExitCode == 0,
            Output: stdout + (stderr.Length > 0 ? "\n" + stderr : ""));
    }

    private static List<SarifResult> ReadSarif(string sarifPath)
    {
        var json = File.ReadAllText(sarifPath);
        using var doc = JsonDocument.Parse(json);
        var results = new List<SarifResult>();

        var runs = doc.RootElement.GetProperty("runs");
        foreach (var run in runs.EnumerateArray())
        {
            if (!run.TryGetProperty("results", out var resultsEl))
                continue;

            foreach (var result in resultsEl.EnumerateArray())
            {
                var ruleId = result.TryGetProperty("ruleId", out var r) ? r.GetString() ?? "" : "";
                var level = result.TryGetProperty("level", out var l) ? l.GetString() ?? "" : "";
                var message = result.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";

                string? fileUri = null;
                int startLine = 0;

                if (result.TryGetProperty("locations", out var locs))
                {
                    foreach (var loc in locs.EnumerateArray())
                    {
                        if (loc.TryGetProperty("resultFile", out var rf))
                        {
                            if (rf.TryGetProperty("uri", out var u))
                                fileUri = u.GetString();
                            if (rf.TryGetProperty("region", out var reg) &&
                                reg.TryGetProperty("startLine", out var sl))
                                startLine = sl.GetInt32();
                        }
                        break;
                    }
                }

                results.Add(new SarifResult(ruleId, level, message, fileUri ?? "", startLine));
            }
        }

        return results;
    }

    private static SampleContract ReadContract(string contractPath)
    {
        var json = File.ReadAllText(contractPath);
        return JsonSerializer.Deserialize<SampleContract>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize contract: {contractPath}");
    }

    private static SampleVerificationResult VerifyContract(
        List<SarifResult> sarif, SampleContract contract, string projectName)
    {
        var failures = new List<string>();

        // Verify folder-based claims (used by SampleApp)
        foreach (var claim in contract.FolderClaims)
        {
            var folderSegment = $"/Diagnostics/{claim.Folder}/";
            var claimedRuleId = claim.RuleId;

            // Diagnostics in this folder with the claimed rule ID
            var claimedDiagnostics = sarif
                .Where(r => r.FileUri.Contains(folderSegment, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(r.RuleId, claimedRuleId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Diagnostics in this folder with ANY rule ID at warning level
            // (excluding approved secondary and the claimed rule itself)
            var approvedSecondary = claim.ApprovedSecondaryRuleIds ?? [];
            var unexpectedDiagnostics = sarif
                .Where(r => r.FileUri.Contains(folderSegment, StringComparison.OrdinalIgnoreCase)
                            && r.Level == "warning"
                            && !string.Equals(r.RuleId, claimedRuleId, StringComparison.OrdinalIgnoreCase)
                            && !approvedSecondary.Contains(r.RuleId, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (claim.ExpectedCount.HasValue)
            {
                if (claimedDiagnostics.Count != claim.ExpectedCount.Value)
                {
                    failures.Add(
                        $"[{projectName}/{claim.Folder}] Expected {claim.ExpectedCount.Value} " +
                        $"{claimedRuleId} diagnostic(s), observed {claimedDiagnostics.Count}." +
                        (claimedDiagnostics.Count == 0
                            ? $" No {claimedRuleId} diagnostics found in Diagnostics/{claim.Folder}/."
                            : $" Observed: {string.Join("; ", claimedDiagnostics.Select(d => $"line {d.StartLine}"))}"));
                }
            }
            else if (claimedDiagnostics.Count == 0)
            {
                failures.Add(
                    $"[{projectName}/{claim.Folder}] Expected at least one {claimedRuleId} diagnostic " +
                    $"in Diagnostics/{claim.Folder}/ but none were observed.");
            }

            foreach (var unexpected in unexpectedDiagnostics)
            {
                failures.Add(
                    $"[{projectName}/{claim.Folder}] Unexpected {unexpected.RuleId} ({unexpected.Level}) " +
                    $"diagnostic in Diagnostics/{claim.Folder}/ at line {unexpected.StartLine}. " +
                    $"If this is intentional, add '{unexpected.RuleId}' to approvedSecondaryRuleIds for this folder claim.");
            }
        }

        // Verify file-based claims (used by DI015InAction or precise matches)
        foreach (var claim in contract.FileClaims)
        {
            var matchingResults = sarif
                .Where(r => r.FileUri.Contains(claim.FilePathContains, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(r.RuleId, claim.RuleId, StringComparison.OrdinalIgnoreCase)
                            && (claim.MessageContains == null ||
                                r.Message.Contains(claim.MessageContains, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matchingResults.Count == 0)
            {
                failures.Add(
                    $"[{projectName}] Expected {claim.RuleId} diagnostic in file matching '{claim.FilePathContains}'" +
                    (claim.MessageContains != null ? $" with message containing '{claim.MessageContains}'" : "") +
                    " but none were observed. This claim may be stale.");
            }
        }

        // Verify absence claims (sections that should stay clean for a specific rule)
        foreach (var absence in contract.AbsenceClaims)
        {
            var unexpectedDiagnostics = sarif
                .Where(r => r.FileUri.Contains(absence.FilePathContains, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(r.RuleId, absence.RuleId, StringComparison.OrdinalIgnoreCase)
                            && (absence.MessageContains == null ||
                                r.Message.Contains(absence.MessageContains, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (unexpectedDiagnostics.Count > 0)
            {
                foreach (var d in unexpectedDiagnostics)
                {
                    failures.Add(
                        $"[{projectName}] Unexpected {absence.RuleId} diagnostic found in '{absence.FilePathContains}' " +
                        (absence.MessageContains != null ? $"with message containing '{absence.MessageContains}' " : "") +
                        $"at line {d.StartLine}: \"{d.Message}\". " +
                        $"This section should be clean for {absence.RuleId}.");
                }
            }
        }

        if (failures.Count == 0)
            return SampleVerificationResult.Ok(
                $"{projectName} verification passed ({sarif.Count} total SARIF results).");

        var sb = new StringBuilder();
        sb.AppendLine($"{projectName} verification FAILED ({failures.Count} issue(s)):");
        foreach (var f in failures)
        {
            sb.AppendLine($"  - {f}");
        }

        return SampleVerificationResult.Failure(sb.ToString());
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record BuildOutput(bool Success, string Output);
}

/// <summary>
/// The result of a sample verification run.
/// </summary>
public sealed class SampleVerificationResult
{
    public bool IsSuccess { get; }
    public string Message { get; }

    private SampleVerificationResult(bool isSuccess, string message)
    {
        IsSuccess = isSuccess;
        Message = message;
    }

    public static SampleVerificationResult Ok(string message) => new(true, message);
    public static SampleVerificationResult Failure(string message) => new(false, message);

    public override string ToString() => Message;
}

/// <summary>
/// A SARIF result entry extracted from the build output.
/// </summary>
internal sealed record SarifResult(
    string RuleId,
    string Level,
    string Message,
    string FileUri,
    int StartLine);

/// <summary>
/// Contract for a sample project's expected diagnostics.
/// </summary>
internal sealed class SampleContract
{
    /// <summary>
    /// Folder-based claims: for each Diagnostics/{Folder}/ directory, assert that
    /// the claimed rule ID appears the expected number of times, and no other
    /// unexpected warning-level DI diagnostics appear.
    /// </summary>
    [JsonPropertyName("folderClaims")]
    public List<FolderClaim> FolderClaims { get; set; } = [];

    /// <summary>
    /// File-based claims: assert that a specific diagnostic appears in a given file
    /// (matched by path substring) with an optional message filter.
    /// </summary>
    [JsonPropertyName("fileClaims")]
    public List<FileClaim> FileClaims { get; set; } = [];

    /// <summary>
    /// Absence claims: assert that a specific rule ID does NOT appear in a given file.
    /// Used to verify "fixed" configurations stay clean.
    /// </summary>
    [JsonPropertyName("absenceClaims")]
    public List<AbsenceClaim> AbsenceClaims { get; set; } = [];
}

/// <summary>
/// A folder-level claim: the claimed rule should appear in Diagnostics/{Folder}/*.
/// </summary>
internal sealed class FolderClaim
{
    /// <summary>Subfolder name under Diagnostics/ (e.g. "DI001").</summary>
    [JsonPropertyName("folder")]
    public string Folder { get; set; } = "";

    /// <summary>Rule ID expected to appear in this folder (e.g. "DI001").</summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = "";

    /// <summary>
    /// Exact number of diagnostics expected. If null, at least one is required.
    /// </summary>
    [JsonPropertyName("expectedCount")]
    public int? ExpectedCount { get; set; }

    /// <summary>
    /// Rule IDs that are approved to appear in this folder without failing the check.
    /// Useful when one sample illustrates multiple overlapping rules.
    /// </summary>
    [JsonPropertyName("approvedSecondaryRuleIds")]
    public List<string>? ApprovedSecondaryRuleIds { get; set; }
}

/// <summary>
/// A file-level presence claim: the rule must appear in the given file.
/// </summary>
internal sealed class FileClaim
{
    /// <summary>Substring of the file path to match (e.g. "Program.cs").</summary>
    [JsonPropertyName("filePathContains")]
    public string FilePathContains { get; set; } = "";

    /// <summary>Rule ID that must appear (e.g. "DI015").</summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = "";

    /// <summary>Optional substring the diagnostic message must contain.</summary>
    [JsonPropertyName("messageContains")]
    public string? MessageContains { get; set; }
}

/// <summary>
/// An absence claim: the rule must NOT appear in files matching the path filter.
/// An optional message filter further narrows which diagnostics are checked.
/// </summary>
internal sealed class AbsenceClaim
{
    /// <summary>Substring of the file path to match.</summary>
    [JsonPropertyName("filePathContains")]
    public string FilePathContains { get; set; } = "";

    /// <summary>Rule ID that must not appear.</summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = "";

    /// <summary>
    /// Optional substring the diagnostic message must contain for the absence
    /// to apply. If null, all diagnostics with the rule ID in the matched file
    /// are checked.
    /// </summary>
    [JsonPropertyName("messageContains")]
    public string? MessageContains { get; set; }
}
