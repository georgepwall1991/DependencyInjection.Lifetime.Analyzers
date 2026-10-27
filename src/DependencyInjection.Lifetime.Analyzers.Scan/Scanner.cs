using System.Diagnostics;
using System.Security;
using System.Text.Json;

namespace DependencyInjection.Lifetime.Analyzers.Scan;

internal sealed record ScanResult(
    ScanReport Report,
    int BuildExitCode,
    string BuildOutput,
    int ErrorLogCount,
    int UnreadableLogCount
);

/// <summary>Runs <c>dotnet build</c> with the analyzers injected and collects what they report.</summary>
internal static class Scanner
{
    public static ScanResult Run(
        ScanOptions options,
        string analyzerAssemblyPath,
        Action<string>? onBuildOutput = null
    )
    {
        var target = Path.GetFullPath(options.Target);
        var rootDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target)!;

        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            "di-scan-" + Guid.NewGuid().ToString("N")
        );
        var logDirectory = Path.Combine(workDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        try
        {
            var targetsPath = ScanTargets.Write(workDirectory, analyzerAssemblyPath, logDirectory);

            var (exitCode, output) = RunDotnet(
                BuildArguments(options, target, targetsPath),
                rootDirectory,
                onBuildOutput
            );

            var findings = new List<Finding>();
            var rules = new Dictionary<string, RuleInfo>(StringComparer.Ordinal);
            var logs = Directory.GetFiles(logDirectory, "*.sarif");
            var unreadable = 0;
            foreach (var log in logs)
            {
                try
                {
                    SarifReader.Read(File.ReadAllText(log), findings, rules);
                }
                catch (JsonException)
                {
                    // A compiler that crashed mid-write leaves a truncated log; report the others.
                    unreadable++;
                }
            }

            var reportRoot = FindRepositoryRoot(rootDirectory) ?? rootDirectory;
            return new ScanResult(
                new ScanReport(reportRoot, findings, rules),
                exitCode,
                output,
                logs.Length,
                unreadable
            );
        }
        finally
        {
            TryDelete(workDirectory);
        }
    }

    /// <summary>
    /// The build arguments. Analyzers are forced on (some repositories turn them off for local builds), the
    /// SDK's own CA and IDE analyzers are turned off (the report ignores them, and they slow the build), and
    /// warnings are not errors, so one project's findings cannot stop the projects that depend on it from
    /// being built and scanned. <c>--no-incremental</c> recompiles projects that are already up to date,
    /// since an up-to-date project never runs the compiler and so never runs the analyzers.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(
        ScanOptions options,
        string target,
        string targetsPath
    )
    {
        var arguments = new List<string>
        {
            "build",
            target,
            "--no-incremental",
            "-nologo",
            "-p:CustomAfterMicrosoftCommonTargets=" + targetsPath,
            "-p:RunAnalyzers=true",
            "-p:RunAnalyzersDuringBuild=true",
            "-p:EnableNETAnalyzers=false",
            "-p:EnforceCodeStyleInBuild=false",
            "-p:TreatWarningsAsErrors=false",
            "-p:MSBuildTreatWarningsAsErrors=false",
            "-p:CodeAnalysisTreatWarningsAsErrors=false",
            "-p:WarningsAsErrors=",
            "-p:GeneratePackageOnBuild=false",
            "-p:GenerateFullPaths=true",
        };

        if (options.Configuration is not null)
            arguments.AddRange(["-c", options.Configuration]);
        if (options.Framework is not null)
            arguments.AddRange(["-f", options.Framework]);
        if (options.NoRestore)
            arguments.Add("--no-restore");
        arguments.Add(options.Verbose ? "-v:normal" : "-v:quiet");

        return arguments;
    }

    internal static (int ExitCode, string Output) RunDotnet(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onOutput,
        IReadOnlyDictionary<string, string>? environment = null
    )
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // Keep the build from leaving MSBuild node processes behind once the scan is done.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        // When the scanner itself runs under MSBuild (a build task, `dotnet test`), these point the child build
        // at the parent's MSBuild and SDK instead of the one the scanned solution selects.
        foreach (
            var variable in new[]
            {
                "MSBUILD_EXE_PATH",
                "MSBuildExtensionsPath",
                "MSBuildSDKsPath",
                "MSBuildLoadMicrosoftTargetsReadOnly",
            }
        )
            startInfo.Environment.Remove(variable);
        // The indexer throws for a missing key, so a plain ??= would crash wherever the variable is unset.
        if (!startInfo.Environment.ContainsKey("DOTNET_CLI_TELEMETRY_OPTOUT"))
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            startInfo.Environment[name] = value;

        using var process = new Process { StartInfo = startInfo };
        var output = new System.Text.StringBuilder();
        var gate = new object();
        void Collect(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
                return;
            lock (gate)
                output.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        }

        process.OutputDataReceived += Collect;
        process.ErrorDataReceived += Collect;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return (process.ExitCode, output.ToString());
    }

    /// <summary>
    /// The enclosing git work tree, if any. Reporting paths relative to it keeps them the same as the paths
    /// in the repository, which is what GitHub code scanning expects when the SARIF file is uploaded.
    /// </summary>
    internal static string? FindRepositoryRoot(string directory)
    {
        for (
            var current = new DirectoryInfo(directory);
            current is not null;
            current = current.Parent
        )
        {
            var git = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return current.FullName;
        }

        return null;
    }

    internal static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
