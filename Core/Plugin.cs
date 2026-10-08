using System;
using Arcade.UI.MenuStates;
using Arcade.UI.SongSelect;
using BepInEx;
using HarmonyLib;
using Rhythm;
using UnityEngine;
using UnityEngine.UI;

namespace BEATNET;

[BepInPlugin(Id, "BEATNET", "1.0.0")]
[DefaultExecutionOrder(-100)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "splash02.beatnet";

    private BeatNetButton? openButton;
    private float nextScan;
    private Harmony? patches;
    internal static BeatNetDownloads? Downloads { get; private set; }
    internal static BeatNetAccounts? Accounts { get; private set; }

    private void Awake()
    {
        CustomSongLoader.Logger = Logger;
        Downloads = new BeatNetDownloads(Application.persistentDataPath);
        Accounts = new BeatNetAccounts(Application.persistentDataPath);
        Downloads.Started = () => Accounts?.TrackDownload();
        var settings = ArcadeSelection.OpenConfig(Paths.ConfigPath);
        ArcadeSelection.Initialize(settings, Logger);
        settings.Save();
        patches = new Harmony(Id);
        patches.PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo("BEATNET loaded");
    }

    private void Update()
    {
        Downloads?.Tick();
        Accounts?.Tick();
        if (Downloads?.NeedsReload == true
            && ArcadeMenuStateMachine.Instance?.CurrentState?.StateName == EArcadeMenuStates.SongSelect)
        {
            if (ArcadeSongDatabase.Instance != null)
            {
                CustomSongLoader.Reload();
                Downloads.NeedsReload = false;
            }
        }
        ArcadeSelection.Update();
        if (openButton != null)
        {
            openButton.HandleInput();
            BeatNetUpdateMenu.Tick(openButton);
            openButton.OpenBpReward();
            return;
        }

        var menu = ArcadeMenuStateMachine.Instance;
        if (menu == null || Time.unscaledTime < nextScan)
        {
            return;
        }

        nextScan = Time.unscaledTime + 1f;
        foreach (var button in menu.GetComponentsInChildren<Button>(true))
        {
            if (button.name != "LeaderboardButton" || button.transform.parent.name != "SongSelect")
            {
                continue;
            }

            try
            {
                openButton = button.transform.parent.gameObject.AddComponent<BeatNetButton>();
                openButton.Initialize(button, this);
            }
            catch (Exception error)
            {
                if (openButton != null)
                {
                    Destroy(openButton);
                    openButton = null;
                }

                Logger.LogError($"cannot add shortcut: {error.Message}");
            }

            break;
        }
    }

    private void OnDestroy()
    {
        Downloads?.Dispose();
        Accounts?.Dispose();
        Accounts = null;
        BeatNetUpdateMenu.Clear();
        Downloads = null;
        ArcadeSelection.Save();
        patches?.UnpatchSelf();
        CustomSongLoader.Clear();
        if (openButton != null)
        {
            Destroy(openButton);
        }
    }

    private void OnApplicationQuit()
    {
        ArcadeSelection.Save();
    }

    private static void SetPlaying(bool value)
    {
        Accounts?.SetPlaying(value);
        Downloads?.SetPlaying(value);
    }

    [HarmonyPatch(typeof(Rhythm.RhythmController), "Awake")]
    private static class GameplayPatch
    {
        private static void Prefix() => SetPlaying(true);
    }

    [HarmonyPatch(typeof(Rhythm.RhythmController), nameof(Rhythm.RhythmController.InitializeAndPlay))]
    private static class StartGameplayPatch
    {
        private static void Prefix() => SetPlaying(true);
    }

    private static bool Retry(ArcadeProgression progression)
    {
        var path = progression.GetBeatmapPath();
        if (!BeatNetPlay.CanPlay(path)) { progression.Back(); return false; }
        Accounts?.BeginRun(path);
        return true;
    }

    [HarmonyPatch(typeof(ArcadeProgression), nameof(ArcadeProgression.Retry))]
    private static class RetryPatch
    {
        private static bool Prefix(ArcadeProgression __instance) => Retry(__instance);
    }

    [HarmonyPatch(typeof(Rhythm.RhythmController), nameof(Rhythm.RhythmController.RestartBeatmap))]
    private static class RestartPatch
    {
        private static bool Prefix() => JeffBezosController.rhythmProgression is not ArcadeProgression progression || Retry(progression);
    }

    [HarmonyPatch(typeof(Rhythm.RhythmController), "OnDestroy")]
    private static class ExitGameplayPatch
    {
        private static void Postfix() => SetPlaying(false);
    }

    [HarmonyPatch(typeof(HighScoreScreenArcade), "Awake")]
    private static class ResultsPatch
    {
        private static void Prefix() => SetPlaying(false);
    }

    [HarmonyPatch(typeof(ArcadeMenuStateMachine), "Awake")]
    private static class MenuPatch
    {
        private static void Postfix() => SetPlaying(false);
    }

    [HarmonyPatch(typeof(ArcadeSongListView), "Update")]
    private static class SongScrollPatch
    {
        private static bool Prefix() => !BeatNetPanel.BlocksGameInput;
    }
}
