using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Arcade.UI.SongSelect;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace BEATNET;

internal static class ArcadeSelection
{
    private static ConfigFile? config;
    private static ManualLogSource? logger;
    private static ConfigEntry<string>? song;
    private static ConfigEntry<string>? category;
    private static ConfigEntry<string>? difficulty;
    private static bool restoring;
    private static bool dirty;
    private static DateTime saveAt;

    internal static ConfigFile OpenConfig(string folder)
    {
        var path = Path.Combine(folder, "BEATNET.cfg");
        var previous = Path.Combine(folder, Plugin.Id + ".cfg");
        if (!File.Exists(path) && File.Exists(previous))
        {
            File.Move(previous, path);
        }

        return new ConfigFile(path, false);
    }

    internal static void Initialize(ConfigFile settings, ManualLogSource log)
    {
        config = settings;
        config.SaveOnConfigSet = false;
        logger = log;
    }

    private static void LoadProfile(string profile)
    {
        Save();
        song = config?.Bind(profile, "Song", string.Empty);
        category = config?.Bind(profile, "Category", string.Empty);
        difficulty = config?.Bind(profile, "Difficulty", string.Empty);
    }

    private static void SetSelection(string? songName, string categoryName, string difficultyName)
    {
        if (song == null || category == null || difficulty == null)
        {
            return;
        }

        songName ??= song.Value;
        if (song.Value == songName && category.Value == categoryName && difficulty.Value == difficultyName)
        {
            return;
        }

        song.Value = songName;
        category.Value = categoryName;
        difficulty.Value = difficultyName;
        dirty = true;
        saveAt = DateTime.UtcNow.AddMilliseconds(500);
    }

    internal static void Update()
    {
        if (dirty && DateTime.UtcNow >= saveAt)
        {
            Save();
        }
    }

    internal static void Save()
    {
        if (!dirty || config == null)
        {
            return;
        }

        try
        {
            config.Save();
            dirty = false;
        }
        catch (Exception error)
        {
            logger?.LogWarning($"cannot save arcade selection: {error.Message}");
            saveAt = DateTime.UtcNow.AddSeconds(10);
        }
    }

    private static void ReadSelection(ArcadeSongList? list)
    {
        if (restoring || category == null || difficulty == null)
        {
            return;
        }

        var selected = list != null ? list.GetSelectedSong() : null;
        SetSelection(selected?.Song.name, ArcadeSongDatabase.SelectedCategory.Name, ArcadeSongDatabase.SelectedDifficulty);
    }

    [HarmonyPatch(typeof(ArcadeSongDatabase), "Awake")]
    private static class DatabasePatch
    {
        private static void Postfix(ArcadeSongDatabase __instance)
        {
            restoring = true;
            try
            {
                LoadProfile(FileStorage.profile.folder);
                if (difficulty != null && __instance.BeatmapIndex.DifficultyIsSelectable(difficulty.Value))
                {
                    __instance.SetDifficulty(difficulty.Value);
                }

                var selected = __instance.SelectableCategories.FirstOrDefault(item => item.Name == category?.Value);
                if (selected != null)
                {
                    __instance.SetCategory(selected);
                }
            }
            catch (Exception error)
            {
                logger?.LogWarning($"cannot restore arcade selection: {error.Message}");
            }
            finally
            {
                restoring = false;
            }
        }
    }

    [HarmonyPatch(typeof(ArcadeSongList), "GetBaseSongIndex")]
    private static class RestoreSongPatch
    {
        private static bool Prefix(ref int __result)
        {
            var database = ArcadeSongDatabase.Instance;
            if (database == null || string.IsNullOrEmpty(song?.Value))
            {
                return true;
            }

            var index = database.IndexOfSongByName(song!.Value);
            if (index < 0)
            {
                return true;
            }

            __result = index;
            return false;
        }
    }

    [HarmonyPatch(typeof(ArcadeSongList), nameof(ArcadeSongList.SetSelectedSongIndex))]
    private static class SelectedSongPatch
    {
        private static void Postfix(ArcadeSongList __instance)
        {
            if (ArcadeSongDatabase.Instance != null)
            {
                ReadSelection(__instance);
            }
        }
    }

    [HarmonyPatch]
    private static class FiltersPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ArcadeSongDatabase), nameof(ArcadeSongDatabase.SetCategory));
            yield return AccessTools.Method(typeof(ArcadeSongDatabase), nameof(ArcadeSongDatabase.SetDifficulty));
        }

        private static void Postfix()
        {
            ReadSelection(ArcadeSongList.Instance);
        }
    }
}
