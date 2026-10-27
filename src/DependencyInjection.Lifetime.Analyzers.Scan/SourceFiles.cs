namespace DependencyInjection.Lifetime.Analyzers.Scan;

/// <summary>Reads the scanned source files once each, for the lines the report shows and fingerprints.</summary>
internal sealed class SourceFiles(string rootDirectory)
{
    private readonly Dictionary<string, string[]?> _files = new(StringComparer.Ordinal);

    /// <summary>The 1-based <paramref name="line"/> of the file at <paramref name="path"/>, or null when it cannot be read.</summary>
    public string? Line(string path, int line)
    {
        if (line <= 0)
            return null;

        if (!_files.TryGetValue(path, out var lines))
        {
            try
            {
                lines = File.ReadAllLines(Path.Combine(rootDirectory, path));
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or ArgumentException
                            or NotSupportedException
                )
            {
                lines = null;
            }

            _files[path] = lines;
        }

        return lines is not null && line <= lines.Length ? lines[line - 1] : null;
    }
}
