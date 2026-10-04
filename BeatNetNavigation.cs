using System.Collections.Generic;

namespace BEATNET;

internal readonly struct NavigationPoint
{
    internal readonly float X;
    internal readonly float Y;
    internal readonly bool Available;

    internal NavigationPoint(float x, float y, bool available)
    {
        X = x;
        Y = y;
        Available = available;
    }
}

internal static class BeatNetNavigation
{
    internal static int Find(IReadOnlyList<NavigationPoint> points, int current, int x, int y, bool preferAligned = false)
    {
        if (current < 0 || current >= points.Count || !points[current].Available)
        {
            for (var index = 0; index < points.Count; index++)
            {
                if (points[index].Available)
                {
                    return index;
                }
            }
            return -1;
        }
        var origin = points[current];
        var best = current;
        var score = float.MaxValue;
        var aligned = false;
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (index == current || !point.Available)
            {
                continue;
            }
            var dx = point.X - origin.X;
            var dy = point.Y - origin.Y;
            var forward = dx * x + dy * y;
            var across = dx * y - dy * x;
            if (forward <= 0f)
            {
                continue;
            }
            var inline = System.Math.Abs(across) <= 1f;
            if (preferAligned && aligned && !inline)
            {
                continue;
            }
            var distance = forward * forward + across * across * 4f;
            if (distance < score || preferAligned && inline && !aligned)
            {
                score = distance;
                best = index;
                aligned = inline;
            }
        }
        return best;
    }

    internal static int Step(IReadOnlyList<NavigationPoint> points, int current, int direction)
    {
        if (points.Count == 0)
        {
            return -1;
        }
        if (current < 0)
        {
            current = direction > 0 ? -1 : 0;
        }
        for (var step = 0; step < points.Count; step++)
        {
            current = (current + direction + points.Count) % points.Count;
            if (points[current].Available)
            {
                return current;
            }
        }
        return -1;
    }
}
