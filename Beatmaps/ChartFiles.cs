using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BEATNET;

internal static class ChartFiles
{
    private static readonly string[] Slots = { "Beginner", "Easy", "Normal", "Hard", "UNBEATABLE", "Star" };
    //FOR THE LOVE OF GOD D-CELL JUST ADD JPG SUPPORT IT'S ONE LINE OF CODE
    private static readonly string[] CoverNames = { "cover.png", "cover.jpg", "cover.jpeg" };
    //here, this is all it took :ijusttriedtosobreactthismessage:
    private static string? GetSlot(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var start = name.LastIndexOf('[');
        if (start < 0 || !name.EndsWith("]", StringComparison.Ordinal))
        {
            return null;
        }

        var value = name.Substring(start + 1, name.Length - start - 2).Trim();
        if (value.Length == 0 || value.IndexOf(']') >= 0)
        {
            return null;
        }

        if (string.Equals(value, "Expert", StringComparison.OrdinalIgnoreCase))
        {
            return "Hard";
        }

        foreach (var slot in Slots)
        {
            if (string.Equals(value, slot, StringComparison.OrdinalIgnoreCase))
            {
                return slot;
            }
        }

        return "Star";
    }

    internal static string? ResolveSlot(string path, string? version, string[] nativeSlots)
    {
        var slot = GetSlot(path);
        if (slot != null)
        {
            return slot;
        }

        var filename = Path.GetFileName(path);
        slot = nativeSlots.FirstOrDefault(name => filename.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        if (slot != null)
        {
            return slot;
        }

        var label = version?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            return null;
        }

        if (string.Equals(label, "Expert", StringComparison.OrdinalIgnoreCase))
        {
            return "Hard";
        }

        return nativeSlots.FirstOrDefault(name => string.Equals(name, label, StringComparison.OrdinalIgnoreCase)) ?? "Star";
    }

    internal static List<(string Path, string? Slot)> GetCharts(string folder)
    {
        var charts = new List<(string Path, string? Slot)>();
        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(folder).Where(IsChart).OrderBy(path => path, StringComparer.Ordinal))
        {
            var slot = GetSlot(path);
            if (slot != null)
            {
                if (assigned.TryGetValue(slot, out var previous))
                {
                    throw new InvalidDataException($"duplicate difficulty {slot}: {Path.GetFileName(previous)} and {Path.GetFileName(path)}");
                }

                assigned.Add(slot, path);
            }

            charts.Add((path, slot));
        }

        return charts.OrderBy(chart => chart.Slot == null).ToList();
    }

    internal static string? GetCover(string folder)
    {
        var files = Directory.EnumerateFiles(folder).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        foreach (var name in CoverNames)
        {
            var path = files.FirstOrDefault(file => string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase));
            if (path != null)
            {
                return path;
            }
        }

        return null;
    }

    internal static string[] GetDifficulties(string root, bool recursive = true)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in recursive ? new[] { root }.Concat(GetSongFolders(root)) : new[] { root })
        {
            foreach (var chart in GetCharts(folder))
            {
                var slot = chart.Slot;
                if (slot == null && new FileInfo(chart.Path).Length <= 16_777_216)
                {
                    var metadata = false;
                    string? version = null;
                    foreach (var raw in File.ReadLines(chart.Path))
                    {
                        var line = raw.Trim();
                        if (line.StartsWith("[", StringComparison.Ordinal))
                        {
                            metadata = line.Equals("[Metadata]", StringComparison.OrdinalIgnoreCase);
                        }
                        else if (metadata && line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                        {
                            version = line.Substring("Version:".Length).Trim();
                            break;
                        }
                    }
                    slot = ResolveSlot(chart.Path, version, Slots);
                }
                if (slot != null)
                {
                    found.Add(slot);
                }
            }
        }
        return Slots.Where(found.Contains).ToArray();
    }

    internal static IEnumerable<string> GetSongFolders(string root)
    {
        return Directory.EnumerateDirectories(root, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).OrderBy(path => path, StringComparer.Ordinal);
    }

    internal static IEnumerable<string> GetCustomFolders(string root)
    {
        var folders = Directory.EnumerateDirectories(root, "*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).OrderBy(path => path, StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            yield return folder;
            if (!Path.GetFileName(folder).Equals("BEATNET_beatmaps", StringComparison.OrdinalIgnoreCase)) { continue; }
            foreach (var child in GetSongFolders(folder)) { yield return child; }
        }
    }

    private static bool IsChart(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".osu", StringComparison.OrdinalIgnoreCase);
    }
}
