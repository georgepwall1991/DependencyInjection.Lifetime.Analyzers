using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DependencyInjection.Lifetime.Analyzers.Scan;

return ScanCommand.Run(args, Console.Out, Console.Error);

namespace DependencyInjection.Lifetime.Analyzers.Scan
{
    internal static class ScanCommand
    {
        public const int Success = 0;
        public const int FindingsFound = 1;
        public const int UsageError = 2;
        public const int ScanFailed = 3;

        public static int Run(
            IReadOnlyList<string> args,
            TextWriter output,
            TextWriter error,
            string? analyzerAssemblyPath = null,
            Func<string, string?>? environment = null
        )
        {
            environment ??= Environment.GetEnvironmentVariable;
            if (!ScanOptions.TryParse(args, out var options, out var parseError))
            {
                error.WriteLine(parseError);
                error.WriteLine("Run 'di-scan --help' for usage.");
                return UsageError;
            }

            if (options.ShowHelp)
            {
                output.WriteLine(ScanOptions.Usage);
                return Success;
            }

            var version = ToolVersion;
            if (options.ShowVersion)
            {
                output.WriteLine($"di-scan {version}");
                return Success;
            }

            if (!File.Exists(options.Target) && !Directory.Exists(options.Target))
            {
                error.WriteLine(
                    $"Nothing to scan at '{options.Target}'. Pass a solution, project or directory."
                );
                return UsageError;
            }

            ScanBaseline? baseline = null;
            if (options.BaselinePath is not null)
            {
                try
                {
                    baseline = ScanBaseline.Parse(File.ReadAllText(options.BaselinePath));
                }
                catch (Exception exception)
                    when (exception
                            is IOException
                                or UnauthorizedAccessException
                                or JsonException
                                or FormatException
                    )
                {
                    error.WriteLine(
                        $"Cannot read the baseline '{options.BaselinePath}': {exception.Message}"
                    );
                    return UsageError;
                }
            }

            analyzerAssemblyPath ??= Path.Combine(
                AppContext.BaseDirectory,
                "analyzer",
                "DependencyInjection.Lifetime.Analyzers.dll"
            );
            if (!File.Exists(analyzerAssemblyPath))
            {
                error.WriteLine(
                    $"The DependencyInjection.Lifetime.Analyzers analyzer is missing from the tool at '{analyzerAssemblyPath}'. Reinstall DependencyInjection.Lifetime.Analyzers.Scan."
                );
                return ScanFailed;
            }

            var fixFailed = false;
            if (options.Fix)
            {
                var rules = ScanFixer.SelectRules(options);
                if (rules.Count == 0)
                {
                    error.WriteLine(
                        "None of the rules selected by --rules and --skip-rules has a code fix, so --fix has nothing to apply."
                    );
                    return UsageError;
                }

                fixFailed = !Fix(options, rules, analyzerAssemblyPath, version, output, error);
            }

            output.WriteLine(
                $"Building {Path.GetFullPath(options.Target)} with DI Scan {version}..."
            );
            var result = Scanner.Run(
                options,
                analyzerAssemblyPath,
                options.Verbose ? output.WriteLine : null
            );

            if (result.BuildExitCode != 0)
            {
                if (!options.Verbose)
                    error.WriteLine(BuildErrorExcerpt(result.BuildOutput));
                if (result.ErrorLogCount == 0)
                {
                    error.WriteLine(
                        "The build failed before any project was compiled, so nothing was scanned. Fix the build (run 'dotnet build' to see why) and try again."
                    );
                    return ScanFailed;
                }
                error.WriteLine(
                    RuleErrorHint(result.BuildOutput)
                        ?? "The build failed, so projects after the failure were not scanned. Results below are partial."
                );
            }
            else if (result.ErrorLogCount == 0)
            {
                error.WriteLine(
                    "No C# project was compiled, so nothing was scanned. Point the scanner at a solution or C# project."
                );
                return ScanFailed;
            }

            if (result.UnreadableLogCount > 0)
                error.WriteLine(
                    $"{result.UnreadableLogCount} of {result.ErrorLogCount} compiler logs could not be read, so their projects are missing from the results."
                );

            var report = result.Report.Filter(
                new ScanFilter(options.Rules, options.SkippedRules, options.Excludes)
            );
            if (baseline is not null)
                report = report.ApplyBaseline(baseline);

            if (options.UpdateBaselinePath is not null)
            {
                var baselinePath = Path.GetFullPath(options.UpdateBaselinePath);
                var baselineDirectory = Path.GetDirectoryName(baselinePath);
                if (!string.IsNullOrEmpty(baselineDirectory))
                    Directory.CreateDirectory(baselineDirectory);
                using (var writer = new StreamWriter(baselinePath))
                    report.WriteTextBaseline(writer, version);
                output.WriteLine($"Baseline written: {DisplayPath(baselinePath)}");
            }

            var sarifPath = Path.GetFullPath(options.SarifPath);
            var sarifDirectory = Path.GetDirectoryName(sarifPath);
            if (!string.IsNullOrEmpty(sarifDirectory))
                Directory.CreateDirectory(sarifDirectory);
            using (var stream = File.Create(sarifPath))
                report.WriteSarif(stream, version);

            output.WriteLine();
            output.Write(
                report.RenderText(
                    options.Top,
                    DisplayPath(sarifPath),
                    options.FindingsPerRule,
                    options.Context
                )
            );

            if (options.HtmlPath is not null)
            {
                var htmlPath = Path.GetFullPath(options.HtmlPath);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);
                    File.WriteAllText(
                        htmlPath,
                        HtmlReport.Render(
                            report,
                            version,
                            Path.GetFullPath(options.Target),
                            DateTimeOffset.UtcNow
                        )
                    );
                    output.WriteLine($"HTML report: {DisplayPath(htmlPath)}");
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                    error.WriteLine($"Could not write the HTML report: {exception.Message}");
                }
            }

            WriteGitHubOutput(options, report, output, error, environment);

            if (result.BuildExitCode != 0 || fixFailed)
                return ScanFailed;

            // Updating a baseline is an explicit adoption action; it succeeds unless the build or a
            // fix failed, so the command can be scripted without a second pass.
            var failing =
                options.UpdateBaselinePath is not null || options.FailOn is null
                    ? 0
                    : report.CountAtLeast(options.FailOn);
            if (failing > 0)
            {
                error.WriteLine(
                    $"Failing: {failing} finding{(failing == 1 ? " is" : "s are")} {options.FailOn!.ToLowerInvariant()} severity or higher (--fail-on {options.FailOn.ToLowerInvariant()})."
                );
                return FindingsFound;
            }

            return Success;
        }

        /// <summary>
        /// Applies the code fixes and lists the files they changed. Returns false when dotnet format
        /// failed; the fixes it applied before failing stay, and the scan that follows reports what
        /// is left either way.
        /// </summary>
        private static bool Fix(
            ScanOptions options,
            IReadOnlyList<string> rules,
            string analyzerAssemblyPath,
            string version,
            TextWriter output,
            TextWriter error
        )
        {
            var target = Path.GetFullPath(options.Target);
            var rootDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target)!;
            var reportRoot = Scanner.FindRepositoryRoot(rootDirectory) ?? rootDirectory;

            output.WriteLine($"Fixing {target} with DI Scan {version} (dotnet format)...");
            var result = ScanFixer.Run(
                options,
                rules,
                analyzerAssemblyPath,
                reportRoot,
                options.Verbose ? output.WriteLine : null
            );

            if (result.ChangedFiles.Count == 0)
            {
                output.WriteLine("No finding had a fix to apply.");
            }
            else
            {
                output.WriteLine(
                    $"Applied fixes to {result.ChangedFiles.Count} file{(result.ChangedFiles.Count == 1 ? "" : "s")}. Review them with 'git diff' before you commit:"
                );
                foreach (var file in result.ChangedFiles.Take(MaxFixedFilesListed))
                    output.WriteLine("  " + file);
                if (result.ChangedFiles.Count > MaxFixedFilesListed)
                    output.WriteLine(
                        $"  ...and {result.ChangedFiles.Count - MaxFixedFilesListed} more."
                    );
            }

            if (result.RestoredFiles > 0)
                output.WriteLine(
                    $"Put back {result.RestoredFiles} file{(result.RestoredFiles == 1 ? "" : "s")} that --exclude leaves out."
                );

            output.WriteLine();
            if (result.ExitCode == 0)
                return true;

            if (!options.Verbose)
                error.WriteLine(
                    BuildErrorExcerpt(result.Output)
                        .Replace("dotnet build", "dotnet format", StringComparison.Ordinal)
                );
            error.WriteLine(
                $"dotnet format failed (exit code {result.ExitCode}), so some fixes may not have been applied."
            );
            return false;
        }

        private const int MaxFixedFilesListed = 20;

        /// <summary>
        /// Writes the Markdown report to <c>--summary</c>, and in GitHub Actions also appends it to the job summary and
        /// annotates the most severe findings, unless <c>--no-github</c> is set.
        /// </summary>
        private static void WriteGitHubOutput(
            ScanOptions options,
            ScanReport report,
            TextWriter output,
            TextWriter error,
            Func<string, string?> environment
        )
        {
            var inActions =
                !options.NoGitHub
                && string.Equals(
                    environment("GITHUB_ACTIONS"),
                    "true",
                    StringComparison.OrdinalIgnoreCase
                );
            var stepSummary = inActions ? environment("GITHUB_STEP_SUMMARY") : null;
            if (options.SummaryPath is null && string.IsNullOrEmpty(stepSummary) && !inActions)
                return;

            var blobUri = inActions ? GitHubOutput.BlobUri(environment) : null;
            var markdown = GitHubOutput.Markdown(report, options.FindingsPerRule, blobUri);
            // GitHub rejects a job summary over 1 MiB, which --findings all can reach on a large solution.
            var jobSummary =
                markdown.Length > GitHubOutput.MaxJobSummaryLength
                    ? GitHubOutput.Markdown(
                        report,
                        Math.Min(options.FindingsPerRule, ScanOptions.DefaultFindingsPerRule),
                        blobUri
                    )
                    : markdown;
            try
            {
                if (options.SummaryPath is not null)
                {
                    var summaryPath = Path.GetFullPath(options.SummaryPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(summaryPath)!);
                    File.WriteAllText(summaryPath, markdown);
                    output.WriteLine($"Markdown report: {DisplayPath(summaryPath)}");
                }
                if (!string.IsNullOrEmpty(stepSummary))
                    File.AppendAllText(stepSummary, jobSummary);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"Could not write the Markdown report: {exception.Message}");
            }

            if (inActions)
            {
                foreach (var annotation in GitHubOutput.Annotations(report))
                    output.WriteLine(annotation);
            }
        }

        internal static string ToolVersion =>
            typeof(ScanCommand)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion.Split('+')[0]
            ?? typeof(ScanCommand).Assembly.GetName().Version?.ToString(3)
            ?? "unknown";

        /// <summary>
        /// Explains a build that failed on a finding: a rule set to <c>error</c> in <c>.editorconfig</c> fails that
        /// project's build, and the projects that depend on it are not built or scanned. The scan cannot lower those
        /// severities, so it says which rules to set to <c>warning</c>.
        /// </summary>
        internal static string? RuleErrorHint(string buildOutput)
        {
            var rules = Regex
                .Matches(buildOutput, @": error (DI\d{3}[a-z]?):")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            return rules.Count == 0
                ? null
                : $"The build failed because {string.Join(", ", rules)} {(rules.Count == 1 ? "is" : "are")} set to error severity, so projects that depend on a failing project were not scanned. Results below are partial. Set {(rules.Count == 1 ? "it" : "them")} to warning in .editorconfig to scan everything.";
        }

        /// <summary>A path relative to the current directory when it is inside it, otherwise the full path.</summary>
        internal static string DisplayPath(string fullPath)
        {
            var relative = Path.GetRelativePath(Environment.CurrentDirectory, fullPath);
            return
                relative == ".."
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(relative)
                ? fullPath
                : relative;
        }

        /// <summary>The error lines of a failed quiet build, which is all a user needs to see why it failed.</summary>
        internal static string BuildErrorExcerpt(string buildOutput)
        {
            var errors = buildOutput
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Contains(": error ", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(20)
                .ToList();

            return errors.Count > 0
                ? "dotnet build reported errors:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, errors.Select(line => "  " + line))
                : "dotnet build failed. Run again with --verbose to see its output.";
        }
    }
}
