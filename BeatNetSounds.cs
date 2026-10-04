using Arcade.UI;
using FMOD;
using FMODUnity;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

internal enum BeatNetSound
{
    None,
    Confirm,
    Back,
    Hover,
    Up,
    Down,
}

internal static class BeatNetSounds
{
    private static readonly EventReference Confirm = Event("5b79acf7-ab8c-4795-b5bf-cb81ce722b2a");
    private static readonly EventReference Back = Event("d77dd4d1-febb-425a-9068-c0fed9924d92");
    private static readonly EventReference Hover = Event("758f31c1-7877-4f09-ad44-680faae63eeb");
    private static readonly EventReference Up = Event("2beec968-931d-459b-a9e6-e2c647a97ff8");
    private static readonly EventReference Down = Event("853ccfd4-3998-4e70-8d08-5a507161fcfd");

    private static EventReference Event(string id) => new() { Guid = GUID.Parse(id) };

    internal static void Play(BeatNetSound sound)
    {
        var reference = sound == BeatNetSound.Confirm ? Confirm : sound == BeatNetSound.Back ? Back
            : sound == BeatNetSound.Hover ? Hover : sound == BeatNetSound.Up ? Up
            : sound == BeatNetSound.Down ? Down : default;
        if (!reference.IsNull)
        {
            RuntimeManager.PlayOneShot(reference);
        }
    }

    internal static void Move(Vector2Int direction, int step = 0)
    {
        Play(direction.y > 0 || step < 0 ? BeatNetSound.Up : BeatNetSound.Down);
    }

    internal static void Mute(AnimatedSelectable control)
    {
        foreach (var field in new[] { "buttonPressedEvent", "buttonHoveredEvent", "selectableSubmitEvent",
            "onNavLeftSound", "onNavUpSound", "onNavRightSound", "onNavDownSound" })
        {
            AccessTools.Field(typeof(AnimatedSelectable), field).SetValue(control, default(EventReference));
        }
    }
}

public sealed class BeatNetFocusSound : MonoBehaviour, IPointerClickHandler
{
    internal bool Click { get; set; }

    public void OnPointerClick(PointerEventData data)
    {
        if (Click && data.button == PointerEventData.InputButton.Left && GetComponent<Selectable>().IsInteractable())
        {
            BeatNetSounds.Play(BeatNetSound.Confirm);
        }
    }
}
