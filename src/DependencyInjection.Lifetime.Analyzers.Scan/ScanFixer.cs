using System.Text.RegularExpressions;

namespace DependencyInjection.Lifetime.Analyzers.Scan;

internal sealed record FixResult(
    int ExitCode,
    string Output,
    IReadOnlyList<string> ChangedFiles,
    int RestoredFiles
);

/// <summary>
/// Applies the DependencyInjection.Lifetime.Analyzers code fixes with
/// <c>dotnet format analyzers</c>. dotnet format only loads the analyzers a project references, so
/// the scan's MSBuild file goes in through the <c>CustomAfterMicrosoftCommonTargets</c> environment
/// variable, which MSBuild reads as a property in the design-time builds dotnet format runs.
/// </summary>
internal static class ScanFixer
{
    /// <summary>
    /// The rules with a code fix. A test keeps this list equal to the shipped code-fix providers.
    /// </summary>
    public static readonly IReadOnlyList<string> FixableRules =
    [
        "DI001",
        "DI002",
        "DI003",
        "DI004",
        "DI005",
        "DI006",
        "DI008",
        "DI009",
        "DI012",
        "DI013",
        "DI014",
        "DI015",
        "DI019",
        "DI021",
        "DI025",
    ];

    /// <summary>The fixable rules that <c>--rules</c> and <c>--skip-rules</c> select.</summary>
    public static IReadOnlyList<string> SelectRules(ScanOptions options) =>
        FixableRules
            .Where(rule =>
                options.Rules.Count == 0
                || options.Rules.Contains(rule, StringComparer.OrdinalIgnoreCase)
            )
            .Where(rule => !options.SkippedRules.Contains(rule, StringComparer.OrdinalIgnoreCase))
            .ToList();

    public static FixResult Run(
        ScanOptions options,
        IReadOnlyList<string> rules,
        string analyzerAssemblyPath,
        string reportRoot,
        Action<string>? onOutput = null
    )
    {
        var target = Path.GetFullPath(options.Target);
        var rootDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target)!;

        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            "di-scan-fix-" + Guid.NewGuid().ToString("N")
        );
        var logDirectory = Path.Combine(workDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        try
        {
            var targetsPath = ScanTargets.Write(workDirectory, analyzerAssemblyPath, logDirectory);

            var excludes = options.Excludes.Select(ScanFilter.GlobToRegex).ToList();
            var before = Snapshot(reportRoot, excludes);

            var (exitCode, output) = Scanner.RunDotnet(
                FormatArguments(options, target, rules),
                rootDirectory,
                onOutput,
                new Dictionary<string, string>
                {
                    ["CustomAfterMicrosoftCommonTargets"] = targetsPath,
                }
            );

            var changed = new List<string>();
            var restored = 0;
            foreach (
                var file in Directory.EnumerateFiles(
                    reportRoot,
                    "*.cs",
                    SearchOption.AllDirectories
                )
            )
            {
                var relative = RelativePath(reportRoot, file);
                if (IsBuildOutput(relative))
                    continue;

                var info = new FileInfo(file);
                if (
                    before.TryGetValue(relative, out var old)
                    && old.Length == info.Length
                    && old.LastWriteTimeUtc == info.LastWriteTimeUtc
                )
                    continue;

                // A fix can land in an excluded file (or a rule's fix can edit another file); put it
                // back as it was.
                if (old?.Content is not null)
                {
                    File.WriteAllBytes(file, old.Content);
                    restored++;
                    continue;
                }

                changed.Add(relative);
            }

            changed.Sort(StringComparer.Ordinal);
            return new FixResult(exitCode, output, changed, restored);
        }
        finally
        {
            Scanner.TryDelete(workDirectory);
        }
    }

    /// <summary>
    /// The <c>dotnet format</c> arguments. <c>--severity info</c> takes every finding the scan would
    /// report, since dotnet format otherwise skips the info-severity rules; a rule set to <c>none</c>
    /// in .editorconfig stays off.
    /// </summary>
    internal static IReadOnlyList<string> FormatArguments(
        ScanOptions options,
        string target,
        IReadOnlyList<string> rules
    )
    {
        var arguments = new List<string>
        {
            "format",
            "analyzers",
            target,
            "--severity",
            "info",
            "--diagnostics",
        };
        arguments.AddRange(rules);
        if (options.NoRestore)
            arguments.Add("--no-restore");
        arguments.AddRange(["--verbosity", options.Verbose ? "normal" : "quiet"]);
        return arguments;
    }

    private sealed record FileState(long Length, DateTime LastWriteTimeUtc, byte[]? Content);

    private static Dictionary<string, FileState> Snapshot(
        string root,
        IReadOnlyList<Regex> excludes
    )
    {
        var files = new Dictionary<string, FileState>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = RelativePath(root, file);
            if (IsBuildOutput(relative))
                continue;

            var info = new FileInfo(file);
            var excluded = excludes.Any(exclude => exclude.IsMatch(relative));
            files[relative] = new FileState(
                info.Length,
                info.LastWriteTimeUtc,
                excluded ? File.ReadAllBytes(file) : null
            );
        }

        return files;
    }

    private static string RelativePath(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsBuildOutput(string relative) =>
        relative.Split('/') is var parts
        && parts.Take(parts.Length - 1).Any(part => part is "bin" or "obj" or ".git");
}
