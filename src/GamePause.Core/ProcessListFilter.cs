namespace GamePause.Core;

public static class ProcessListFilter
{
    public static IReadOnlyList<WindowProcessInfo> Apply(
        IEnumerable<WindowProcessInfo> processes,
        string? query,
        bool foregroundOnly,
        int? foregroundProcessId)
    {
        var normalizedQuery = query?.Trim();
        return processes
            .Where(process => !foregroundOnly || process.ProcessId == foregroundProcessId)
            .Where(process => Matches(process, normalizedQuery))
            .OrderByDescending(process => process.ProcessId == foregroundProcessId)
            .ThenBy(process => process.IsProtected)
            .ThenBy(process => KnownGames.GetDisplayName(process.Name), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool Matches(WindowProcessInfo process, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return process.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || process.WindowTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || process.ProcessId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
               || KnownGames.GetDisplayName(process.Name).Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
