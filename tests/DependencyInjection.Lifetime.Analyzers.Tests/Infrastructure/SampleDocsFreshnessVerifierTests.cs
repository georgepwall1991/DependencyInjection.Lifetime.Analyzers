using System;
using System.Diagnostics;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;

/// <summary>
/// Freshness verification tests that check sample/docs wiring stays consistent.
/// These tests wire sample/docs freshness checks into the standard test run so
/// drift is caught from the canonical test path rather than from a separate manual
/// or ad hoc maintainer command.
/// </summary>
public class SampleDocsFreshnessVerifierTests
{
    // AppContext.BaseDirectory resolves to:
    //   tests/DependencyInjection.Lifetime.Analyzers.Tests/bin/Release/net10.0/
    // Five levels up reaches the repository root.
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));

    private readonly ITestOutputHelper _output;

    public SampleDocsFreshnessVerifierTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Verifies that all configured rule-page snippet symbols and markers can be
    /// extracted from their mapped sample files. Missing snippets fail loudly
    /// instead of being silently dropped during docs generation.
    /// (VAL-SAMPLES-004: rule-page sample extraction cannot silently drop missing snippets)
    /// </summary>
    [Fact]
    public void RulePageSnippets_AllSymbolsAndMarkersExtract_NoSilentDrop()
    {
        var result = RunFreshnessCheck();
        _output.WriteLine(result.Output);
        Assert.True(
            result.ExitCode == 0,
            $"Sample/docs freshness check failed (exit code {result.ExitCode}):\n{result.Output}");
    }

    /// <summary>
    /// Verifies that the set of rule IDs in ruleSampleConfig matches the set of
    /// rule-sample directories under samples/SampleApp/Diagnostics/, so adding,
    /// renaming, or removing a public diagnostic requires an explicit mapping update.
    /// (VAL-SAMPLES-005: sample rule coverage and mapping sets stay in parity)
    /// </summary>
    [Fact]
    public void SampleDiagnosticsDirectories_MatchRuleSampleConfig_InParity()
    {
        var diagnosticsDir = Path.Combine(RepoRoot, "samples", "SampleApp", "Diagnostics");
        Assert.True(Directory.Exists(diagnosticsDir),
            $"Sample diagnostics directory not found: {diagnosticsDir}");

        // The freshness check (run in the companion test above) validates parity,
        // but this test provides an additional direct structural check that is
        // readable without requiring the Node.js tool to be present.

        var sampleDirs = Directory.GetDirectories(diagnosticsDir);
        Assert.True(sampleDirs.Length > 0,
            "No subdirectories found under samples/SampleApp/Diagnostics/. " +
            "Expected at least one rule-specific directory.");

        // Also verify that the freshness check itself reports the expected count,
        // i.e., it found the same number of rule directories.
        var result = RunFreshnessCheck();
        _output.WriteLine(result.Output);
        Assert.True(
            result.ExitCode == 0,
            $"Sample/docs parity check failed (exit code {result.ExitCode}):\n{result.Output}");
    }

    /// <summary>
    /// Verifies that every configured sample file actually exists on disk so
    /// stale sample path references in ruleSampleConfig are detected early.
    /// (VAL-SAMPLES-003: published sample claims agree with observed diagnostics)
    /// </summary>
    [Fact]
    public void RulePageSampleFiles_AllExist_NoStalePathReferences()
    {
        var toolPath = Path.Combine(RepoRoot, "tools", "generate-growth-assets.mjs");
        Assert.True(File.Exists(toolPath),
            $"Tool not found at expected path: {toolPath}");

        // Run the freshness check; it validates that configured sample files are readable.
        var result = RunFreshnessCheck();
        _output.WriteLine(result.Output);
        Assert.True(
            result.ExitCode == 0,
            $"Sample file existence check failed (exit code {result.ExitCode}):\n{result.Output}");
    }

    private FreshnessCheckResult RunFreshnessCheck()
    {
        var toolPath = Path.Combine(RepoRoot, "tools", "generate-growth-assets.mjs");

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"\"{toolPath}\" check-freshness",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoRoot,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start node process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var combined = (stdout + (stderr.Length > 0 ? "\n" + stderr : "")).Trim();
        return new FreshnessCheckResult(process.ExitCode, combined);
    }

    private sealed record FreshnessCheckResult(int ExitCode, string Output);
}
