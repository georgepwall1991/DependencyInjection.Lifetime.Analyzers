using System.Text.Json;
using System.Text.RegularExpressions;

namespace DependencyInjection.Lifetime.Analyzers.Scan;

/// <summary>
/// The findings of an earlier scan, read from the SARIF report it wrote or from the text baseline
/// the scan regression harness keeps. A SARIF finding is known when the baseline has its
/// fingerprint, which does not change when code above it moves the finding to another line. A text
/// baseline is matched by rule and exact location, because that is what a committed baseline
/// records.
/// </summary>
internal sealed class ScanBaseline
{
    private static readonly Regex TextLinePattern = new(
        @"^(?<rule>DI\d{3}[a-z]?)\s+(?<path>.+?):(?<line>\d+):(?<column>\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private readonly HashSet<string> _fingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<(string RuleId, string Path, string Message)> _unfingerprinted = [];
    private readonly HashSet<(string RuleId, string Path, int Line, int Column)> _locations = [];

    public int Count { get; private set; }

    public bool Contains(Finding finding, string fingerprint) =>
        _fingerprints.Contains(fingerprint)
        || _unfingerprinted.Contains(
            (NormalizeRuleId(finding.RuleId), NormalizePath(finding.Path), finding.Message)
        )
        || _locations.Contains(
            (
                NormalizeRuleId(finding.RuleId),
                NormalizePath(finding.Path),
                finding.Line,
                finding.Column
            )
        );

    /// <summary>
    /// Reads a SARIF or text baseline. A JSON document is parsed as SARIF; anything else is parsed
    /// as <c>DIxxx path:line:column</c> lines with <c>#</c> comments, the format the scan
    /// regression harness writes.
    /// </summary>
    /// <exception cref="JsonException">The file starts like JSON but is not a SARIF log.</exception>
    /// <exception cref="FormatException">A text baseline has a line that is not a finding.</exception>
    public static ScanBaseline Parse(string content)
    {
        var trimmed = content.TrimStart();
        return trimmed.StartsWith('{') ? ParseSarif(content) : ParseText(content);
    }

    private static ScanBaseline ParseSarif(string json)
    {
        var baseline = new ScanBaseline();
        using var document = JsonDocument.Parse(json);
        if (
            !document.RootElement.TryGetProperty("runs", out var runs)
            || runs.ValueKind != JsonValueKind.Array
        )
            throw new JsonException("The file has no SARIF 'runs'.");

        foreach (var run in runs.EnumerateArray())
        {
            if (
                !run.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array
            )
                continue;

            foreach (var result in results.EnumerateArray())
            {
                if (
                    result.TryGetProperty("baselineState", out var state)
                    && state.GetString() == "absent"
                )
                    continue;

                baseline.Count++;
                if (
                    result.TryGetProperty("partialFingerprints", out var fingerprints)
                    && fingerprints.ValueKind == JsonValueKind.Object
                    && fingerprints.TryGetProperty(ScanReport.FingerprintKey, out var fingerprint)
                    && fingerprint.ValueKind == JsonValueKind.String
                )
                {
                    baseline._fingerprints.Add(fingerprint.GetString()!);
                    continue;
                }

                var ruleId = result.TryGetProperty("ruleId", out var id) ? id.GetString() : null;
                var message =
                    result.TryGetProperty("message", out var messageElement)
                    && messageElement.TryGetProperty("text", out var text)
                        ? text.GetString()
                        : null;
                var path = RelativeUri(result);
                if (ruleId is not null && message is not null && path is not null)
                    baseline._unfingerprinted.Add(
                        (NormalizeRuleId(ruleId), NormalizePath(path), message)
                    );
            }
        }

        return baseline;
    }

    private static ScanBaseline ParseText(string content)
    {
        var baseline = new ScanBaseline();
        var number = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            number++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var match = TextLinePattern.Match(line);
            if (!match.Success)
                throw new FormatException(
                    $"Line {number} is not a 'DIxxx path:line:column' finding: {line}"
                );

            baseline.Count++;
            baseline._locations.Add(
                (
                    NormalizeRuleId(match.Groups["rule"].Value),
                    NormalizePath(match.Groups["path"].Value),
                    int.Parse(
                        match.Groups["line"].Value,
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                    int.Parse(
                        match.Groups["column"].Value,
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                )
            );
        }

        return baseline;
    }

    private static string NormalizeRuleId(string ruleId) => ruleId.ToUpperInvariant();

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    private static string? RelativeUri(JsonElement result)
    {
        if (
            !result.TryGetProperty("locations", out var locations)
            || locations.ValueKind != JsonValueKind.Array
            || locations.GetArrayLength() == 0
            || !locations[0].TryGetProperty("physicalLocation", out var physical)
            || !physical.TryGetProperty("artifactLocation", out var artifact)
            || !artifact.TryGetProperty("uri", out var uri)
            || uri.ValueKind != JsonValueKind.String
        )
        {
            return null;
        }

        return Uri.UnescapeDataString(uri.GetString()!);
    }
}
