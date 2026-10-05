using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Arcade.UI;
using Arcade.UI.SongSelect;
using BepInEx.Logging;
using HarmonyLib;
using Rhythm;
using UnityEngine;

namespace BEATNET;

internal static class CustomSongLoader
{
    private static readonly List<UnityEngine.Object> Covers = new();
    private static readonly object Marker = new();
    private static ConditionalWeakTable<MetadataInfo, object> labels = new();

    internal static ManualLogSource? Logger { get; set; }

    internal static void Reload()
    {
        var database = ArcadeSongDatabase.Instance;
        if (database == null)
        {
            return;
        }
        var previous = database.SongDatabase.Values.Select(item => item.Beatmap).Distinct().ToArray();
        using var music = new BeatNetMusic();
        var preview = music.Item;
        var listedPreview = preview != null && !music.IsBackground && database.SongDatabase.ContainsKey(preview.Path);
        try
        {
            database.LoadDatabase();
            database.RefreshSongList();
            if (preview != null && listedPreview && ArcadeBGMManager.Instance != null)
            {
                var replacement = database.GetBeatmapItemByPath(preview.Path)
                    ?? database.SongDatabase.Values.Where(item => item.Unlocked)
                        .OrderByDescending(item => item.CustomSong)
                        .ThenByDescending(item => item.BeatmapInfo.difficulty == ArcadeSongDatabase.SelectedDifficulty)
                        .FirstOrDefault();
                if (replacement != null)
                {
                    if (!music.Restore(replacement))
                    {
                        SelectPreview(replacement);
                    }
                }
                else
                {
                    ArcadeBGMManager.Instance.StopSongPreview();
                }
            }
            var retained = new HashSet<Beatmap>(database.SongDatabase.Values.Select(item => item.Beatmap));
            if (!listedPreview && preview != null)
            {
                retained.Add(preview.Beatmap);
            }
            foreach (var beatmap in previous)
            {
                if (!retained.Contains(beatmap))
                {
                    UnityEngine.Object.Destroy(beatmap);
                }
            }
        }
        catch
        {
            Logger?.LogWarning("cannot refresh songs / reopen arcade mode");
        }
    }

    internal static void SelectAfterRemoval(string folder)
    {
        var database = ArcadeSongDatabase.Instance;
        using var music = new BeatNetMusic();
        var preview = music.Item;
        var selected = ArcadeSongList.Instance?.GetSelectedSong();
        if (database == null || folder.Length == 0)
        {
            return;
        }
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = root + Path.DirectorySeparatorChar;
        bool Removed(ArcadeSongDatabase.BeatmapItem? item)
        {
            if (item == null || !item.CustomSong)
            {
                return false;
            }
            var path = Path.GetFullPath(item.Song.CustomPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        if (!Removed(preview) && !Removed(selected))
        {
            return;
        }
        var replacement = preview?.Unlocked == true && !Removed(preview) ? preview
            : database.SongDatabase.Values.Where(item => item.Unlocked && !Removed(item))
            .OrderByDescending(item => item.CustomSong)
            .ThenByDescending(item => item.BeatmapInfo.difficulty == ArcadeSongDatabase.SelectedDifficulty)
            .ThenBy(item => item.internalIndex)
            .FirstOrDefault();
        if (replacement == null)
        {
            ArcadeBGMManager.Instance?.StopSongPreview();
            return;
        }
        SelectPreview(replacement, !music.Restore(replacement));
    }

    private static void SelectPreview(ArcadeSongDatabase.BeatmapItem song, bool play = true)
    {
        var database = ArcadeSongDatabase.Instance;
        database.SetCategory(database.SelectableCategories.FirstOrDefault(item => item.Name == (song.CustomSong ? "custom" : "all")));
        database.SetDifficulty(song.BeatmapInfo.difficulty);
        var index = database.IndexOfSong(song.Song);
        if (index >= 0 && ArcadeSongList.Instance != null)
        {
            ArcadeSongList.Instance.SetSelectedSongIndex(index);
        }
        if (play)
        {
            ArcadeBGMManager.Instance?.PlaySongPreview(song, song.Song.PreviewStartTime);
        }
    }

    internal static void Clear()
    {
        foreach (var cover in Covers)
        {
            UnityEngine.Object.Destroy(cover);
        }

        Covers.Clear();
        labels = new ConditionalWeakTable<MetadataInfo, object>();
    }

    private static List<BeatmapIndex.Song> Load(string root, BeatmapIndex.Category category, BeatmapParserEngine.SectionTypes sections)
    {
        Clear();
        var songs = new List<BeatmapIndex.Song>();
        if (!Directory.Exists(root))
        {
            return songs;
        }

        var difficulties = BeatmapIndex.defaultIndex.Difficulties;
        foreach (var folder in ChartFiles.GetSongFolders(root))
        {
            var song = LoadFolder(root, folder, category, sections, difficulties);
            if (song != null)
            {
                songs.Add(song);
            }
        }

        return songs;
    }

    private static BeatmapIndex.Song? LoadFolder(
        string root,
        string folder,
        BeatmapIndex.Category category,
        BeatmapParserEngine.SectionTypes sections,
        string[] difficulties)
    {
        var beatmaps = new List<Beatmap>();
        Texture2D? texture = null;
        Sprite? cover = null;
        var loaded = false;
        BeatmapIndex.Song? song = null;
        try
        {
            var charts = ChartFiles.GetCharts(folder);
            if (charts.Count == 0)
            {
                return null;
            }

            var coverPath = ChartFiles.GetCover(folder);
            if (coverPath != null)
            {
                texture = new Texture2D(2, 2);
                if (!texture.LoadImage(File.ReadAllBytes(coverPath)))
                {
                    throw new InvalidDataException("cannot read cover image");
                }

                cover = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), Vector2.zero);
            }

            var video = Path.Combine(folder, "video.webm");
            if (!File.Exists(video))
            {
                video = Path.Combine(folder, "video.mp4");
            }

            foreach (var chart in charts)
            {
                var beatmap = BeatmapParser.ParseBeatmap(File.ReadAllText(chart.Path), sections);
                if (beatmap == null)
                {
                    throw new InvalidDataException($"cannot parse chart {Path.GetFileName(chart.Path)}");
                }

                beatmaps.Add(beatmap);
                try
                {
                    var slot = ChartFiles.ResolveSlot(chart.Path, beatmap.metadata.version, difficulties);
                    if (slot == null)
                    {
                        throw new InvalidDataException("unknown difficulty");
                    }

                    if (song != null && song.Difficulties.Contains(slot))
                    {
                        throw new InvalidDataException($"duplicate difficulty {slot}");
                    }

                    var candidate = song ?? BeatmapIndex.Song.CreateCustomSong(
                        beatmap, category, folder, cover, File.Exists(video) ? video : null);
                    candidate.AddBeatmapToCustomSong(beatmap, chart.Path, slot);
                    song = candidate;
                }
                catch (Exception error) when (chart.Slot == null)
                {
                    Logger?.LogWarning($"cannot load {Path.GetFileName(chart.Path)}: {error.Message}");
                }
            }

            if (song != null)
            {
                song.name = "CUSTOM_" + Path.GetRelativePath(root, folder).Replace('\\', '/');
                if (texture != null)
                {
                    Covers.Add(texture);
                }

                if (cover != null)
                {
                    Covers.Add(cover);
                }

                loaded = true;
            }
        }
        catch (Exception error)
        {
            Logger?.LogWarning($"cannot load {Path.GetRelativePath(root, folder)}: {error.Message}");
        }
        finally
        {
            foreach (var beatmap in beatmaps)
            {
                UnityEngine.Object.Destroy(beatmap);
            }

            if (!loaded)
            {
                if (cover != null)
                {
                    UnityEngine.Object.Destroy(cover);
                }

                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        return loaded ? song : null;
    }

    [HarmonyPatch(typeof(ArcadeSongDatabase), nameof(ArcadeSongDatabase.LoadDatabase))]
    private static class DatabasePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ArcadeSongDatabase __instance, ref bool ____loadCustomSongs, BeatmapIndex.Category ___customCategory)
        {
            if (!Directory.Exists(Path.Combine(Application.persistentDataPath, "CustomSongs")))
            {
                return;
            }

            ____loadCustomSongs = true;
            if (!__instance.SelectableCategories.Contains(___customCategory))
            {
                __instance.SelectableCategories.Add(___customCategory);
            }
        }

        private static void Postfix(ArcadeSongDatabase __instance)
        {
            var count = 0;
            foreach (var item in __instance.SongDatabase.Values)
            {
                if (item.CustomSong)
                {
                    count++;
                    labels.GetValue(item.Beatmap.metadata, _ => Marker);
                }
            }
            Logger?.LogInfo($"custom database {count} difficulties");
            Plugin.Accounts?.RegisterSongs();
        }
    }

    [HarmonyPatch(typeof(ArcadeSongDatabase), "LoadCustoms")]
    private static class CustomsPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            BeatmapIndex.Category ___customCategory,
            BeatmapParserEngine.SectionTypes ____beatmapSectionsToRead,
            ref List<BeatmapIndex.Song> __result)
        {
            try
            {
                __result = Load(Path.Combine(Application.persistentDataPath, "CustomSongs"), ___customCategory, ____beatmapSectionsToRead);
                Logger?.LogInfo($"loaded {__result.Count} custom songs");
            }
            catch (Exception error)
            {
                Clear();
                Logger?.LogWarning($"cannot scan songs: {error.Message}");
                __result = new List<BeatmapIndex.Song>();
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MetadataInfo), nameof(MetadataInfo.GetDifficulty))]
    private static class LabelPatch
    {
        private static bool Prefix(MetadataInfo __instance, string baseDifficulty, ref string __result)
        {
            if (!labels.TryGetValue(__instance, out _))
            {
                return true;
            }

            __result = string.IsNullOrEmpty(__instance.version) ? baseDifficulty : __instance.version;
            return false;
        }
    }
}
