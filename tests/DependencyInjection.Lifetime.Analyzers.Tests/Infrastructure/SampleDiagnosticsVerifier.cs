using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

public static class SampleDiagnosticsVerifier
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

    public static SampleVerificationResult VerifySampleApp()
    {
        var projectPath = Path.Combine(RepoRoot, "samples", "SampleApp", "SampleApp.csproj");
        var contractPath = Path.Combine(RepoRoot, "samples", "SampleApp", "sample-diagnostics-contract.json");
        return VerifyProject(projectPath, contractPath, "SampleApp");
    }

    public static SampleVerificationResult VerifyDI015InAction()
    {
        var projectPath = Path.Combine(RepoRoot, "samples", "DI015InAction", "DI015InAction.csproj");
        var contractPath = Path.Combine(RepoRoot, "samples", "DI015InAction", "sample-diagnostics-contract.json");
        return VerifyProject(projectPath, contractPath, "DI015InAction");
    }

    private static SampleVerificationResult VerifyProject(string projectPath, string contractPath, string projectName)
    {
        if (!File.Exists(projectPath))
            return SampleVerificationResult.Failure($"Project not found: {projectPath}");

        if (!File.Exists(contractPath))
            return SampleVerificationResult.Failure($"Contract file not found: {contractPath}");

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
            Output: stdout + (stderr.Length > 0 ? "\n" + stderr : string.Empty));
    }

    private static List<SarifResult> ReadSarif(string sarifPath)
    {
        var json = File.ReadAllText(sarifPath);
        using var doc = JsonDocument.Parse(json);
        var results = new List<SarifResult>();
        var index = 0;

        if (!doc.RootElement.TryGetProperty("runs", out var runs))
            return results;

        foreach (var run in runs.EnumerateArray())
        {
            if (!run.TryGetProperty("results", out var resultsEl))
                continue;

            foreach (var result in resultsEl.EnumerateArray())
            {
                var ruleId = result.TryGetProperty("ruleId", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                var level = result.TryGetProperty("level", out var l) ? l.GetString() ?? string.Empty : string.Empty;
                var message = result.TryGetProperty("message", out var m) ? ReadSarifMessage(m) : string.Empty;

                string fileUri = string.Empty;
                int startLine = 0;

                if (result.TryGetProperty("locations", out var locations))
                {
                    foreach (var location in locations.EnumerateArray())
                    {
                        if (location.TryGetProperty("resultFile", out var resultFile))
                        {
                            if (resultFile.TryGetProperty("uri", out var uri))
                                fileUri = uri.GetString() ?? string.Empty;

                            if (resultFile.TryGetProperty("region", out var region) &&
                                region.TryGetProperty("startLine", out var startLineEl) &&
                                startLineEl.TryGetInt32(out var line))
                            {
                                startLine = line;
                            }
                        }

                        break;
                    }
                }

                results.Add(new SarifResult(index++, ruleId, level, message, fileUri, startLine));
            }
        }

        return results;
    }

    private static string ReadSarifMessage(JsonElement messageElement)
    {
        if (messageElement.ValueKind == JsonValueKind.String)
            return messageElement.GetString() ?? string.Empty;

        if (messageElement.ValueKind == JsonValueKind.Object &&
            messageElement.TryGetProperty("text", out var textElement))
        {
            return textElement.GetString() ?? string.Empty;
        }

        return messageElement.ToString();
    }

    private static SampleContract ReadContract(string contractPath)
    {
        var json = File.ReadAllText(contractPath);
        return JsonSerializer.Deserialize<SampleContract>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize contract: {contractPath}");
    }

    internal static SampleVerificationResult VerifyContract(
        List<SarifResult> sarif, SampleContract contract, string projectName)
    {
        var failures = new List<string>();

        foreach (var folderClaim in contract.FolderClaims)
            failures.AddRange(VerifyFolderClaim(sarif, folderClaim, projectName));

        foreach (var fileGroup in contract.FileClaims.GroupBy(c => c.FilePathContains))
            failures.AddRange(VerifyDiagnosticClaims(sarif, fileGroup.ToList(), projectName, fileGroup.Key));

        foreach (var absence in contract.AbsenceClaims)
            failures.AddRange(VerifyAbsenceClaim(sarif, absence, projectName));

        if (failures.Count == 0)
            return SampleVerificationResult.Ok($"{projectName} verification passed ({sarif.Count} total SARIF results).");

        var sb = new StringBuilder();
        sb.AppendLine($"{projectName} verification FAILED ({failures.Count} issue(s)):");
        foreach (var failure in failures)
            sb.AppendLine($"  - {failure}");

        return SampleVerificationResult.Failure(sb.ToString());
    }

    private static List<string> VerifyFolderClaim(
        List<SarifResult> sarif, FolderClaim folderClaim, string projectName)
    {
        var folderSegment = $"/Diagnostics/{folderClaim.Folder}/";
        var folderResults = sarif
            .Where(r => PathMatches(r.FileUri, folderSegment))
            .ToList();

        var failures = new List<string>();
        var matched = new HashSet<int>();
        var allowedSignatures = new HashSet<(string RuleId, string Severity)>(StringTupleComparer.OrdinalIgnoreCase);

        foreach (var claim in folderClaim.Claims)
        {
            allowedSignatures.Add((claim.RuleId, claim.Severity));
            MatchAndRecordClaim(
                folderResults,
                claim,
                projectName,
                folderClaim.Folder,
                matched,
                failures);
        }

        foreach (var secondary in folderClaim.ApprovedSecondaryDiagnostics)
        {
            allowedSignatures.Add((secondary.RuleId, secondary.Severity));
            MatchAndRecordClaim(
                folderResults,
                secondary,
                projectName,
                folderClaim.Folder,
                matched,
                failures,
                isSecondary: true);
        }

        foreach (var result in folderResults)
        {
            if (!allowedSignatures.Contains((result.RuleId, result.Level)))
                continue;

            if (!matched.Contains(result.Index))
            {
                failures.Add(
                    $"[{projectName}/{folderClaim.Folder}] Unexpected {FormatDiagnostic(result)}. " +
                    $"This diagnostic is not bound to an approved claim anchor.");
            }
        }

        return failures;
    }

    private static List<string> VerifyDiagnosticClaims(
        List<SarifResult> sarif,
        IReadOnlyList<DiagnosticClaim> claims,
        string projectName,
        string scopeLabel)
    {
        var failures = new List<string>();
        var matched = new HashSet<int>();

        foreach (var claim in claims)
        {
            MatchAndRecordClaim(
                sarif,
                claim,
                projectName,
                scopeLabel,
                matched,
                failures);
        }

        foreach (var result in sarif.Where(r => claims.Any(c => MatchesClaimSignature(r, c))))
        {
            if (!matched.Contains(result.Index))
            {
                failures.Add(
                    $"[{projectName}/{scopeLabel}] Unexpected {FormatDiagnostic(result)}. " +
                    $"This diagnostic is not bound to an approved claim anchor.");
            }
        }

        return failures;
    }

    private static void MatchAndRecordClaim(
        List<SarifResult> results,
        DiagnosticClaim claim,
        string projectName,
        string scopeLabel,
        HashSet<int> matched,
        List<string> failures,
        bool isSecondary = false)
    {
        var scope = $"[{projectName}/{scopeLabel}]";
        var sameRuleResults = results
            .Where(result =>
                PathMatches(result.FileUri, claim.FilePathContains) &&
                string.Equals(result.RuleId, claim.RuleId, StringComparison.OrdinalIgnoreCase) &&
                (claim.MessageContains is null ||
                 result.Message.Contains(claim.MessageContains, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var sameSeverityResults = sameRuleResults
            .Where(result => string.Equals(result.Level, claim.Severity, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sourcePath = ResolveSourcePath(results, claim);
        if (sourcePath is null)
        {
            failures.Add(
                $"{scope} Stale claim anchored at '{claim.Anchor}' expected {claim.RuleId} ({claim.Severity}) " +
                $"in '{claim.FilePathContains}', but the source file could not be resolved.");
            return;
        }

        int anchorLine;
        try
        {
            anchorLine = FindAnchorLine(sourcePath, claim.Anchor, claim.Occurrence);
        }
        catch (Exception ex)
        {
            failures.Add(
                $"{scope} Stale claim anchored at '{claim.Anchor}' expected {claim.RuleId} ({claim.Severity}) " +
                $"in '{claim.FilePathContains}', but the anchor could not be found: {ex.Message}");
            return;
        }

        var exactMatch = sameSeverityResults.FirstOrDefault(result => result.StartLine == anchorLine);
        if (exactMatch is null)
        {
            var observed = sameRuleResults.Count == 0
                ? "none"
                : string.Join("; ", sameRuleResults.Select(FormatDiagnostic));

            var prefix = isSecondary ? "Approved secondary claim" : "Stale claim";
            failures.Add(
                $"{scope} {prefix} anchored at '{claim.Anchor}' expected {claim.RuleId} ({claim.Severity}) " +
                $"at line {anchorLine}, but observed {observed}.");
            return;
        }

        matched.Add(exactMatch.Index);
    }

    private static List<string> VerifyAbsenceClaim(
        List<SarifResult> sarif, AbsenceClaim absence, string projectName)
    {
        var unexpectedDiagnostics = sarif
            .Where(r =>
                PathMatches(r.FileUri, absence.FilePathContains) &&
                string.Equals(r.RuleId, absence.RuleId, StringComparison.OrdinalIgnoreCase) &&
                (absence.MessageContains is null ||
                 r.Message.Contains(absence.MessageContains, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (unexpectedDiagnostics.Count == 0)
            return [];

        return unexpectedDiagnostics
            .Select(d =>
                $"[{projectName}] Unexpected {absence.RuleId} diagnostic in '{absence.FilePathContains}' " +
                (absence.MessageContains is null ? string.Empty : $"with message containing '{absence.MessageContains}' ") +
                $"at line {d.StartLine}: \"{d.Message}\". This section should be clean for {absence.RuleId}.")
            .ToList();
    }

    private static bool MatchesClaimBasics(SarifResult result, DiagnosticClaim claim) =>
        PathMatches(result.FileUri, claim.FilePathContains) &&
        string.Equals(result.RuleId, claim.RuleId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(result.Level, claim.Severity, StringComparison.OrdinalIgnoreCase) &&
        (claim.MessageContains is null ||
         result.Message.Contains(claim.MessageContains, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesClaimSignature(SarifResult result, DiagnosticClaim claim) =>
        PathMatches(result.FileUri, claim.FilePathContains) &&
        string.Equals(result.RuleId, claim.RuleId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(result.Level, claim.Severity, StringComparison.OrdinalIgnoreCase);

    private static string? ResolveSourcePath(IEnumerable<SarifResult> results, DiagnosticClaim claim)
    {
        var candidate = results
            .Select(r => GetLocalPath(r.FileUri))
            .FirstOrDefault(path => PathMatches(path, claim.FilePathContains));

        if (!string.IsNullOrWhiteSpace(candidate))
            return candidate;

        var samplesRoot = Path.Combine(RepoRoot, "samples");
        if (!Directory.Exists(samplesRoot))
            return null;

        return Directory
            .EnumerateFiles(samplesRoot, "*.cs", SearchOption.AllDirectories)
            .FirstOrDefault(path => PathMatches(path, claim.FilePathContains));
    }

    private static int FindAnchorLine(string sourcePath, string anchor, int occurrence)
    {
        if (occurrence < 1)
            throw new InvalidOperationException($"Invalid anchor occurrence '{occurrence}'.");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Source file not found: {sourcePath}");

        var lines = File.ReadAllLines(sourcePath);
        var matches = lines
            .Select((line, index) => new { Line = line, Index = index })
            .Where(x => x.Line.Contains(anchor, StringComparison.Ordinal))
            .ToList();

        if (matches.Count < occurrence)
            throw new InvalidOperationException($"Anchor '{anchor}' was found {matches.Count} time(s) in '{sourcePath}'.");

        return matches[occurrence - 1].Index + 1;
    }

    private static bool PathMatches(string? candidatePath, string pathContains)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(pathContains))
            return false;

        var normalizedCandidate = GetLocalPath(candidatePath).Replace('\\', '/');
        var normalizedNeedle = pathContains.Replace('\\', '/');
        return normalizedCandidate.Contains(normalizedNeedle, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLocalPath(string fileUri)
    {
        if (Uri.TryCreate(fileUri, UriKind.Absolute, out var uri) && uri.IsFile)
            return uri.LocalPath;

        return fileUri;
    }

    private static string FormatDiagnostic(SarifResult diagnostic) =>
        $"{diagnostic.RuleId} ({diagnostic.Level}) at line {diagnostic.StartLine}: \"{diagnostic.Message}\"";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record BuildOutput(bool Success, string Output);
}

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

internal sealed record SarifResult(
    int Index,
    string RuleId,
    string Level,
    string Message,
    string FileUri,
    int StartLine);

internal sealed class SampleContract
{
    [JsonPropertyName("folderClaims")]
    public List<FolderClaim> FolderClaims { get; set; } = [];

    [JsonPropertyName("fileClaims")]
    public List<DiagnosticClaim> FileClaims { get; set; } = [];

    [JsonPropertyName("absenceClaims")]
    public List<AbsenceClaim> AbsenceClaims { get; set; } = [];
}

internal sealed class FolderClaim
{
    [JsonPropertyName("folder")]
    public string Folder { get; set; } = string.Empty;

    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("claims")]
    public List<DiagnosticClaim> Claims { get; set; } = [];

    [JsonPropertyName("approvedSecondaryDiagnostics")]
    public List<DiagnosticClaim> ApprovedSecondaryDiagnostics { get; set; } = [];
}

internal sealed class DiagnosticClaim
{
    [JsonPropertyName("filePathContains")]
    public string FilePathContains { get; set; } = string.Empty;

    [JsonPropertyName("anchor")]
    public string Anchor { get; set; } = string.Empty;

    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("occurrence")]
    public int Occurrence { get; set; } = 1;

    [JsonPropertyName("messageContains")]
    public string? MessageContains { get; set; }
}

internal sealed class AbsenceClaim
{
    [JsonPropertyName("filePathContains")]
    public string FilePathContains { get; set; } = string.Empty;

    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("messageContains")]
    public string? MessageContains { get; set; }
}

internal sealed class StringTupleComparer : IEqualityComparer<(string RuleId, string Severity)>
{
    public static readonly StringTupleComparer OrdinalIgnoreCase = new();

    public bool Equals((string RuleId, string Severity) x, (string RuleId, string Severity) y) =>
        string.Equals(x.RuleId, y.RuleId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.Severity, y.Severity, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string RuleId, string Severity) obj) =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RuleId ?? string.Empty),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Severity ?? string.Empty));
}
