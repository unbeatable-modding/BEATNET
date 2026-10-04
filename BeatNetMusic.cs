using System;
using System.Reflection;
using Arcade.UI;
using Arcade.UI.SongSelect;
using HarmonyLib;

namespace BEATNET;

internal sealed class BeatNetMusic : IDisposable
{
    private static readonly FieldInfo Current = AccessTools.Field(typeof(ArcadeBGMManager), "currentItem");
    private static readonly FieldInfo Pending = AccessTools.Field(typeof(ArcadeBGMManager), "itemToPlay");
    private static readonly FieldInfo Song = AccessTools.Field(typeof(ArcadeBGMManager), "<CurrentSong>k__BackingField");
    private static int holds;
    private readonly ArcadeBGMManager? manager;
    private readonly ArcadeSongDatabase.BeatmapItem? current;
    private readonly ArcadeSongDatabase.BeatmapItem? pending;
    private bool held;

    internal static bool BlocksChanges => holds > 0;
    internal ArcadeSongDatabase.BeatmapItem? Item => current ?? pending;

    internal BeatNetMusic()
    {
        manager = ArcadeBGMManager.Instance;
        if (manager == null)
        {
            return;
        }
        current = Current.GetValue(manager) as ArcadeSongDatabase.BeatmapItem;
        pending = Pending.GetValue(manager) as ArcadeSongDatabase.BeatmapItem;
        held = Item != null;
        if (held)
        {
            holds++;
        }
    }

    internal bool Restore(ArcadeSongDatabase.BeatmapItem replacement)
    {
        if (manager == null || manager != ArcadeBGMManager.Instance || replacement.Path != Item?.Path)
        {
            return false;
        }
        if (current != null)
        {
            Current.SetValue(manager, replacement);
            Song.SetValue(null, replacement);
        }
        if (pending != null)
        {
            Pending.SetValue(manager, replacement);
        }
        return true;
    }

    public void Dispose()
    {
        if (held)
        {
            holds--;
            held = false;
        }
    }

    [HarmonyPatch(typeof(ArcadeBGMManager), "OnSelectedSongChanged")]
    private static class SelectionPatch
    {
        private static bool Prefix() => !BlocksChanges;
    }
}
