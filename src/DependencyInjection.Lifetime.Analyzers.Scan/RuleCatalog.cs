namespace DependencyInjection.Lifetime.Analyzers.Scan;

/// <summary>
/// The shipped DI rules the scanner reports, with the metadata the terminal, Markdown and HTML
/// reports need. A test keeps this list equal to the analyzer's public descriptors, so a new rule
/// cannot be added without the scanner injecting and documenting it.
/// </summary>
internal static class RuleCatalog
{
    public const string DocumentationSiteUri =
        "https://georgepwall1991.github.io/DependencyInjection.Lifetime.Analyzers/";

    public const string RuleCatalogUri = DocumentationSiteUri + "rules/";

    public const string DocumentationRootUri =
        "https://github.com/georgepwall1991/DependencyInjection.Lifetime.Analyzers/blob/main/docs/rules/";

    /// <summary>
    /// Every shipped DI rule, in ID order. The order is the catalog order and is also what the
    /// scan regression baselines sort by.
    /// </summary>
    public static readonly IReadOnlyList<RuleInfo> Rules =
    [
        Rule("DI001", "Service scope must be disposed", "Warning"),
        Rule("DI002", "Scoped service escapes scope", "Warning"),
        Rule("DI003", "Captive dependency detected", "Warning"),
        Rule("DI004", "Service used after scope disposed", "Warning"),
        Rule("DI005", "Use CreateAsyncScope in async methods", "Warning"),
        Rule("DI006", "Avoid caching IServiceProvider in static members", "Warning"),
        Rule("DI007", "Avoid service locator anti-pattern", "Info"),
        Rule("DI008", "Transient service implements IDisposable", "Warning"),
        Rule("DI009", "Open generic captive dependency", "Warning"),
        Rule("DI010", "Constructor has too many dependencies", "Info"),
        Rule("DI011", "Avoid injecting IServiceProvider or IServiceScopeFactory", "Info"),
        Rule("DI012", "TryAdd registration will be ignored", "Info"),
        Rule("DI012b", "Duplicate service registration", "Info"),
        Rule("DI013", "Implementation type mismatch", "Error"),
        Rule("DI014", "Root service provider not disposed", "Warning"),
        Rule("DI015", "Unresolvable dependency detected", "Warning"),
        Rule("DI016", "Avoid BuildServiceProvider during service registration", "Warning"),
        Rule("DI017", "Circular dependency detected", "Warning"),
        Rule("DI018", "Non-instantiable implementation type", "Warning"),
        Rule("DI019", "Scoped service resolved from root provider", "Warning"),
        Rule("DI020", "Middleware captures scoped dependency in constructor", "Warning"),
        Rule(
            "DI021",
            "Non-thread-safe service shared across concurrent handler invocations",
            "Warning"
        ),
        Rule("DI022", "Service instance reused across handler invocations", "Info"),
        Rule("DI023", "Do not capture a using scope in fire-and-forget background work", "Warning"),
        Rule("DI024", "Create a scope per iteration in hosted service execution loops", "Warning"),
        Rule(
            "DI025",
            "Unsubscribe from longer-lived publishers before the subscriber is released",
            "Warning"
        ),
        Rule(
            "DI026",
            "Unsubscribe from a scoped publisher before the subscriber is released",
            "Info"
        ),
        Rule(
            "DI027",
            "Dispose the subscription returned by Subscribe on a longer-lived observable",
            "Warning"
        ),
        Rule(
            "DI028",
            "Dispose the registration returned when registering a callback on a longer-lived source",
            "Warning"
        ),
        Rule("DI029", "HttpClient lifetime misuse", "Warning"),
        Rule("DI030", "Bound the growth of a cache held for the lifetime of the process", "Info"),
        Rule("DI031", "Shared implementation registered under several service types", "Info"),
        Rule("DI032", "Service implements only IAsyncDisposable", "Warning"),
        Rule("DI033", "Container will not dispose a pre-built instance", "Info"),
        Rule("DI034", "Do not use HttpContext in fire-and-forget background work", "Warning"),
        Rule("DI035", "Do not share a non-thread-safe service across concurrent tasks", "Warning"),
        Rule("DI036", "Registration added after the provider was built", "Warning"),
        Rule("DI037", "Un-awaited task escapes the scope that created it", "Warning"),
    ];

    private static readonly Dictionary<string, RuleInfo> ById = Rules.ToDictionary(
        rule => rule.Id,
        StringComparer.OrdinalIgnoreCase
    );

    public static IReadOnlyList<string> ReportedRules { get; } =
        Rules.Select(rule => rule.Id).ToList();

    public static bool IsReportedRule(string? ruleId) =>
        ruleId is not null && ById.ContainsKey(ruleId);

    public static RuleInfo Get(string ruleId) =>
        ById.TryGetValue(ruleId, out var rule)
            ? rule
            : new RuleInfo(ruleId, ruleId, "Warning", null, null, "DependencyInjection");

    private static RuleInfo Rule(string id, string title, string severity) =>
        new(id, title, severity, DocumentationRootUri + id + ".md", null, "DependencyInjection");
}
