using System;
using System.IO;
using Arcade.UI.SongSelect;
using HarmonyLib;
using UnityEngine;

namespace BEATNET;

internal static class BeatNetPlay
{
    internal static bool CanPlay(string path, ArcadeSongDatabase.BeatmapItem? item = null)
    {
        var accounts = Plugin.Accounts;
        var board = accounts?.Board(path);
        var separator = path.IndexOf('\\');
        var songPath = separator < 0 ? path : path.Substring(0, separator);
        item ??= ArcadeSongDatabase.Instance?.GetBeatmapItemByPath(songPath);
        if (accounts?.User == null && (board != null || songPath.StartsWith("CUSTOM_BEATNET_beatmaps/", StringComparison.Ordinal)))
        {
            var installer = new BeatmapInstaller(Application.persistentDataPath);
            var folder = item?.Song.CustomPath;
            if (string.IsNullOrEmpty(folder))
            {
                var end = songPath.LastIndexOf('/');
                folder = Path.Combine(Application.persistentDataPath, "CustomSongs", end > 7 ? songPath.Substring(7, end - 7) : songPath);
            }
            var id = (string?)board?["projectId"] ?? installer.LibraryId(folder!);
            if (id.Length == 0) { return true; }
            BeatNetUpdateMenu.Open(id, true);
            return false;
        }
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
            if (!CanPlay(beatmapItem.Path, beatmapItem)) { return false; }
            Plugin.Accounts?.BeginRun(beatmapItem.Path);
            return true;
        }
    }
}
