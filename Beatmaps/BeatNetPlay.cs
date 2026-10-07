using Arcade.UI.SongSelect;
using HarmonyLib;

namespace BEATNET;

internal static class BeatNetPlay
{
    internal static bool CanPlay(string path)
    {
        var accounts = Plugin.Accounts;
        var board = accounts?.Board(path);
        if (board == null || !accounts!.IsStale(board)) { return true; }
        BeatNetUpdateMenu.Open((string)board["projectId"]!);
        return false;
    }

    [HarmonyPatch(typeof(ArcadeSongDatabase), nameof(ArcadeSongDatabase.PlaySong))]
    private static class PlayPatch
    {
        private static bool Prefix(ArcadeSongDatabase.BeatmapItem beatmapItem)
        {
            if (beatmapItem == null) { return true; }
            if (!CanPlay(beatmapItem.Path)) { return false; }
            Plugin.Accounts?.BeginRun(beatmapItem.Path);
            return true;
        }
    }
}
