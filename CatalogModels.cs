using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BEATNET;

internal sealed class CatalogPage
{
    public BeatmapEntry[] Items { get; set; } = Array.Empty<BeatmapEntry>();
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
}

internal sealed class BeatmapEntry
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string Submitter { get; set; } = string.Empty;
    public double Rating { get; set; }
    public int RatingCount { get; set; }
    public BeatmapRevision Revision { get; set; } = new();
    public BeatmapFile[] Files { get; set; } = Array.Empty<BeatmapFile>();
    public string[] Difficulties { get; set; } = Array.Empty<string>();
    public Dictionary<string, int> Levels { get; set; } = new();
    public Dictionary<string, string> DifficultyLabels { get; set; } = new();
    public BeatmapPreview? Preview { get; set; }
    public string? Cover { get; set; }
    [JsonIgnore]
    public string? CoverPath { get; set; }
    [JsonIgnore]
    public string CoverStamp { get; set; } = string.Empty;
    [JsonIgnore]
    public string LocalPath { get; set; } = string.Empty;
    [JsonIgnore]
    public long InstalledSize { get; set; }
}

internal sealed class RatingBatch
{
    public RatingSummary[] Items { get; set; } = Array.Empty<RatingSummary>();
}

internal sealed class RatingSummary
{
    public string Id { get; set; } = string.Empty;
    public double Average { get; set; }
    public int Count { get; set; }
}

internal sealed class GlobalScores
{
    public string RevisionId { get; set; } = string.Empty;
    public GlobalScore[] Items { get; set; } = Array.Empty<GlobalScore>();
}

internal sealed class GlobalScore
{
    public string Difficulty { get; set; } = string.Empty;
    public int Score { get; set; }
    public float Accuracy { get; set; }
    public bool NoMiss { get; set; }
    public bool Cleared { get; set; }
}

internal sealed class BeatmapPreview
{
    public string Url { get; set; } = string.Empty;
    public float Start { get; set; }
}

internal sealed class BeatmapRevision
{
    public string Id { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Changelog { get; set; } = string.Empty;
}

internal sealed class BeatmapFile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

internal sealed class InstalledBeatmap
{
    public string Id { get; set; } = string.Empty;
    public string RevisionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public long Size { get; set; }
}
