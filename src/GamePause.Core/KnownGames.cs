namespace GamePause.Core;

public static class KnownGames
{
    public static IReadOnlyList<KnownGame> All { get; } =
    [
        new("地府有点忙", 3116700, []),
        new("多少兄弟？", 3934270, ["HowManyDudes"]),
        new("千棋百计", 3509230, []),
        new("黑神话：悟空", 2358720, ["b1-Win64-Shipping", "b1"]),
        new("幻兽帕鲁", 1623730, ["Palworld-Win64-Shipping", "Palworld"])
    ];

    public static string GetDisplayName(string processName)
    {
        var normalized = Path.GetFileNameWithoutExtension(processName);
        return All.FirstOrDefault(game => game.ExpectedProcesses.Any(
            expected => string.Equals(expected, normalized, StringComparison.OrdinalIgnoreCase)))?.DisplayName
            ?? processName;
    }
}
