namespace DependencyInjection.Lifetime.Analyzers.Scan;

/// <summary>
/// Resolves symbolic links in a path. The compiler reports physical paths (for example, macOS resolves
/// <c>/var</c> to <c>/private/var</c>), while a caller may pass the same project through a symlinked path;
/// relative report paths need both sides to use the same spelling.
/// </summary>
internal static class PathUtil
{
    public static string Canonicalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
            return fullPath;

        var current = root;
        foreach (
            var part in fullPath[root.Length..]
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries
                )
        )
        {
            current = Path.Combine(current, part);
            try
            {
                FileSystemInfo info = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                    current = target.FullName;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                // A path that cannot be inspected stays as the caller wrote it.
            }
        }

        return current;
    }
}
