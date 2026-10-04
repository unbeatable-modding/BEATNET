using System;
using Arcade.UI.MenuStates;
using Arcade.UI.SongSelect;
using BepInEx;
using HarmonyLib;
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

    private void Awake()
    {
        CustomSongLoader.Logger = Logger;
        Downloads = new BeatNetDownloads(Application.persistentDataPath);
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

    [HarmonyPatch(typeof(ArcadeSongListView), "Update")]
    private static class SongScrollPatch
    {
        private static bool Prefix() => !BeatNetPanel.BlocksGameInput;
    }
}
