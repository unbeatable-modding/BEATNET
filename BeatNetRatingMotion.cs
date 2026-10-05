using System;

namespace BEATNET;

internal sealed class BeatNetRatingMotion
{
    private const float Duration = 0.18f;
    private float start = 1f;
    private float target = 1f;
    private float elapsed = Duration;

    internal float Position { get; private set; } = 1f;
    internal bool IsAnimating => elapsed < Duration;

    internal static int Nearest(float value) => Math.Max(1, Math.Min(10, (int)Math.Round(value, MidpointRounding.AwayFromZero)));

    internal void Reset(float value)
    {
        Position = start = target = Math.Max(1f, Math.Min(10f, value));
        elapsed = Duration;
    }

    internal void Snap(int value)
    {
        var next = Nearest(value);
        if (next == target && elapsed < Duration) { return; }
        start = Position;
        target = next;
        elapsed = 0f;
    }

    internal void Tick(float seconds)
    {
        if (elapsed >= Duration) { return; }
        elapsed = Math.Min(Duration, elapsed + Math.Max(0f, seconds));
        var remaining = 1f - elapsed / Duration;
        Position = target + (start - target) * remaining * remaining * remaining;
    }
}

internal static class BeatNetRatingText
{
    internal static string Describe(double percentage) => percentage >= 100 ? "UNBEATABLE!"
        : percentage >= 90 ? "Incredible!" : percentage >= 80 ? "Awesome!"
        : percentage >= 70 ? "Great!" : percentage >= 60 ? "Good!" : percentage >= 50 ? "Okay!"
        : percentage >= 40 ? "Mediocre!" : percentage >= 30 ? "Poor!"
        : percentage >= 20 ? "Bad!" : percentage >= 10 ? "Terrible!"
        : percentage >= 5 ? "Awful!" : "Horrendous!";

    internal static string Vote(int? value, bool available, bool loaded, bool loggedIn) => !available
        ? !loggedIn ? "Log in to rate" : !loaded ? "Loading rating" : "Play a difficulty to rate"
        : value.HasValue ? Describe(value.Value * 10) : "Drag to rate";
}
