using System.Globalization;

namespace DependencyInjection.Lifetime.Analyzers.Scan;

/// <summary>Command-line options for <c>di-scan</c>.</summary>
internal sealed record ScanOptions
{
    public const string DefaultSarifPath = "di-scan.sarif";

    /// <summary>A solution, project or directory to build. Defaults to the current directory.</summary>
    public string Target { get; init; } = ".";

    public string SarifPath { get; init; } = DefaultSarifPath;

    public string? Configuration { get; init; }

    public string? Framework { get; init; }

    public bool NoRestore { get; init; }

    /// <summary>How many files to list under "Most affected files".</summary>
    public int Top { get; init; } = 10;

    /// <summary>How many findings to list under each rule. <see cref="int.MaxValue"/> lists them all; 0 lists none.</summary>
    public int FindingsPerRule { get; init; } = DefaultFindingsPerRule;

    /// <summary>Lines of source shown above and below each finding in the text report.</summary>
    public int Context { get; init; } = DefaultContext;

    public const int DefaultFindingsPerRule = 3;
    public const int DefaultContext = 2;

    /// <summary>Only these rules are reported. Empty reports every rule.</summary>
    public IReadOnlyList<string> Rules { get; init; } = [];

    /// <summary>These rules are left out of the report.</summary>
    public IReadOnlyList<string> SkippedRules { get; init; } = [];

    /// <summary>Globs of files whose findings are left out of the report.</summary>
    public IReadOnlyList<string> Excludes { get; init; } = [];

    /// <summary>
    /// The least severe finding that makes the scan exit with <see cref="ScanCommand.FindingsFound"/>, as a
    /// severity name ("Error", "Warning" or "Info"), or null to exit 0 whatever the scan finds.
    /// </summary>
    public string? FailOn { get; init; }

    /// <summary>
    /// A SARIF report or text baseline from an earlier scan. Findings it already has are not listed
    /// and do not fail the scan.
    /// </summary>
    public string? BaselinePath { get; init; }

    /// <summary>Where to write the report as a self-contained HTML page, if anywhere.</summary>
    public string? HtmlPath { get; init; }

    /// <summary>Where to write the report as Markdown, if anywhere.</summary>
    public string? SummaryPath { get; init; }

    /// <summary>Skip the job summary and annotations the scan adds when it runs in GitHub Actions.</summary>
    public bool NoGitHub { get; init; }

    /// <summary>Apply the rules' code fixes with <c>dotnet format</c> before scanning.</summary>
    public bool Fix { get; init; }

    /// <summary>
    /// Where to write the current findings as a text baseline. The scan still writes SARIF, HTML, and
    /// Markdown, but exits successfully unless the build or a fix failed.
    /// </summary>
    public string? UpdateBaselinePath { get; init; }

    public bool Verbose { get; init; }

    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    public const string Usage = """
        Usage: di-scan [<path>] [options]

        Builds a solution or project with the DependencyInjection.Lifetime.Analyzers analyzers
        injected and reports what they find. Nothing in your repository is changed; the build writes
        its usual bin/ and obj/.

        Arguments:
          <path>                     Solution (.sln/.slnx), project, or directory. Default: current directory.

        Options:
          -o, --sarif <file>         Where to write the SARIF 2.1.0 report. Default: di-scan.sarif
          -c, --configuration <cfg>  Build configuration (passed to dotnet build).
          -f, --framework <tfm>      Target framework to build (passed to dotnet build).
              --no-restore           Skip the implicit restore.
              --rules <ids>          Report only these rules, comma-separated (DI003,DI016).
              --skip-rules <ids>     Leave these rules out, comma-separated.
              --exclude <glob>       Leave out findings in matching files, such as '**/Migrations/**'
                                     or 'tests/**'. Repeat it for more globs. A glob without '/'
                                     matches a file or folder name.
              --fail-on <level>      Exit with code 1 when a finding is at least this severe: error,
                                     warning or info. Default: none (exit 0 whatever the scan finds).
              --baseline <file>      A SARIF report or text baseline from an earlier scan. Only findings
                                     it does not have are listed and count for --fail-on; the new SARIF
                                     report marks each finding new or unchanged.
              --update-baseline <file>
                                     Write the current findings as a text baseline. Implies a successful
                                     scan unless the build or a fix failed.
              --fix                  Apply the rules' code fixes with dotnet format before scanning.
                                     Edits your source files: commit or stash first, and review the diff.
              --html <file>          Also write the report as one HTML page, with the code around each finding.
              --summary <file>       Also write the report as Markdown, for a pull request comment or a wiki.
              --no-github            In GitHub Actions, skip the job summary and the pull request annotations.
              --findings <n|all>     Findings to list under each rule, with the line of code. Default: 3
              --context <n>          Lines of source shown above and below each finding. Default: 2
              --top <n>              Number of files to list under "Most affected files". Default: 10
          -v, --verbose              Show the full dotnet build output.
              --version              Show the version (the analyzer the tool runs has the same version).
          -h, --help                 Show this help.
        """;

    /// <summary>Parses arguments, or returns an error message for the first bad one.</summary>
    public static bool TryParse(
        IReadOnlyList<string> args,
        out ScanOptions options,
        out string? error
    )
    {
        options = new ScanOptions();
        error = null;
        string? target = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help" or "-?":
                    options = options with { ShowHelp = true };
                    break;
                case "--version":
                    options = options with { ShowVersion = true };
                    break;
                case "-v" or "--verbose":
                    options = options with { Verbose = true };
                    break;
                case "--no-restore":
                    options = options with { NoRestore = true };
                    break;
                case "--fix":
                    options = options with { Fix = true };
                    break;
                case "--update-baseline":
                    if (!TryTakeValue(args, ref i, out var updateBaselinePath, out error))
                        return false;
                    options = options with { UpdateBaselinePath = updateBaselinePath };
                    break;
                case "-o" or "--sarif":
                    if (!TryTakeValue(args, ref i, out var sarif, out error))
                        return false;
                    options = options with { SarifPath = sarif };
                    break;
                case "-c" or "--configuration":
                    if (!TryTakeValue(args, ref i, out var configuration, out error))
                        return false;
                    options = options with { Configuration = configuration };
                    break;
                case "-f" or "--framework":
                    if (!TryTakeValue(args, ref i, out var framework, out error))
                        return false;
                    options = options with { Framework = framework };
                    break;
                case "--rules" or "--skip-rules":
                    if (!TryTakeValue(args, ref i, out var ruleText, out error))
                        return false;
                    if (!TryParseRules(arg, ruleText, out var ruleIds, out error))
                        return false;
                    options =
                        arg == "--rules"
                            ? options with
                            {
                                Rules = [.. options.Rules, .. ruleIds],
                            }
                            : options with
                            {
                                SkippedRules = [.. options.SkippedRules, .. ruleIds],
                            };
                    break;
                case "--exclude":
                    if (!TryTakeValue(args, ref i, out var glob, out error))
                        return false;
                    options = options with { Excludes = [.. options.Excludes, glob] };
                    break;
                case "--fail-on":
                    if (!TryTakeValue(args, ref i, out var level, out error))
                        return false;
                    switch (level.ToLowerInvariant())
                    {
                        case "error":
                            options = options with { FailOn = "Error" };
                            break;
                        case "warning":
                            options = options with { FailOn = "Warning" };
                            break;
                        case "info" or "note":
                            options = options with { FailOn = "Info" };
                            break;
                        case "none":
                            options = options with { FailOn = null };
                            break;
                        default:
                            error =
                                $"--fail-on expects error, warning, info or none, got '{level}'.";
                            return false;
                    }
                    break;
                case "--html":
                    if (!TryTakeValue(args, ref i, out var htmlPath, out error))
                        return false;
                    options = options with { HtmlPath = htmlPath };
                    break;
                case "--summary":
                    if (!TryTakeValue(args, ref i, out var summaryPath, out error))
                        return false;
                    options = options with { SummaryPath = summaryPath };
                    break;
                case "--no-github":
                    options = options with { NoGitHub = true };
                    break;
                case "--baseline":
                    if (!TryTakeValue(args, ref i, out var baselinePath, out error))
                        return false;
                    options = options with { BaselinePath = baselinePath };
                    break;
                case "--findings":
                    if (!TryTakeValue(args, ref i, out var findingsText, out error))
                        return false;
                    if (string.Equals(findingsText, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        options = options with { FindingsPerRule = int.MaxValue };
                        break;
                    }
                    if (!TryParseNonNegativeInt(findingsText, out var findingsPerRule))
                    {
                        error =
                            $"--findings expects a whole number or 'all', got '{findingsText}'.";
                        return false;
                    }
                    options = options with { FindingsPerRule = findingsPerRule };
                    break;
                case "--context":
                    if (!TryTakeValue(args, ref i, out var contextText, out error))
                        return false;
                    if (!TryParseNonNegativeInt(contextText, out var context))
                    {
                        error = $"--context expects a whole number, got '{contextText}'.";
                        return false;
                    }
                    options = options with { Context = context };
                    break;
                case "--top":
                    if (!TryTakeValue(args, ref i, out var topText, out error))
                        return false;
                    if (!TryParseNonNegativeInt(topText, out var top))
                    {
                        error = $"--top expects a whole number, got '{topText}'.";
                        return false;
                    }
                    options = options with { Top = top };
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"Unknown option '{arg}'.";
                        return false;
                    }
                    if (target is not null)
                    {
                        error =
                            $"Only one path can be scanned at a time; got '{target}' and '{arg}'.";
                        return false;
                    }
                    target = arg;
                    break;
            }
        }

        if (target is not null)
            options = options with { Target = target };

        if (options.BaselinePath is not null && options.UpdateBaselinePath is not null)
        {
            error = "--baseline reads an existing baseline; --update-baseline writes a new one. Pass one.";
            return false;
        }

        return true;
    }

    private static bool TryParseNonNegativeInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool TryParseRules(
        string option,
        string text,
        out IReadOnlyList<string> ruleIds,
        out string? error
    )
    {
        var parts = text.Split(',');
        var ids = parts
            .Select(id => id.Trim().ToUpperInvariant())
            .ToList();
        var bad = ids.FirstOrDefault(id => id.Length == 0 || !RuleCatalog.IsReportedRule(id));
        if (ids.Count == 0 || bad is not null)
        {
            ruleIds = [];
            error = $"{option} expects DI rule IDs such as DI003, got '{bad ?? text}'.";
            return false;
        }

        ruleIds = ids;
        error = null;
        return true;
    }

    private static bool TryTakeValue(
        IReadOnlyList<string> args,
        ref int index,
        out string value,
        out string? error
    )
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith('-'))
        {
            value = string.Empty;
            error = $"{args[index]} needs a value.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }
}
