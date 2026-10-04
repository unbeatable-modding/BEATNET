using System.Reflection;
using Arcade.UI;
using HarmonyLib;
using UnityEngine;

namespace BEATNET;

internal sealed class BeatNetPerspective
{
    private static readonly FieldInfo Intensity = AccessTools.Field(typeof(CanvasMousePerspective), "intensity");
    private static readonly FieldInfo Smoothing = AccessTools.Field(typeof(CanvasMousePerspective), "smoothing");
    private readonly CanvasMousePerspective source;
    private readonly float intensity;
    private readonly float smoothing;
    private float strength = 1f;
    private float target = 1f;
    private float velocity;

    internal BeatNetPerspective(CanvasMousePerspective source)
    {
        this.source = source;
        intensity = (float)Intensity.GetValue(source);
        smoothing = (float)Smoothing.GetValue(source);
    }

    internal void Set(float value, float delay = 1f)
    {
        target = value;
        if (source != null)
        {
            Smoothing.SetValue(source, smoothing * delay);
        }
    }

    internal void Tick()
    {
        if (source != null)
        {
            strength = Mathf.SmoothDamp(strength, target, ref velocity, 0.12f, Mathf.Infinity, Time.unscaledDeltaTime);
            Intensity.SetValue(source, intensity * strength);
        }
    }

    internal void Restore()
    {
        strength = target = 1f;
        velocity = 0f;
        if (source != null)
        {
            Intensity.SetValue(source, intensity);
            Smoothing.SetValue(source, smoothing);
        }
    }
}
