using System;
using System.Collections.Generic;
using System.Linq;

namespace BEATNET;

internal static class BeatNetCatalog
{
    internal static CatalogPage LibraryPage(IEnumerable<BeatmapEntry> entries, string query, string sorting, string[] difficulties, int offset, int limit, string focus)
    {
        var matches = entries.Where(item => (difficulties.Length == 0 || item.Difficulties.Any(difficulties.Contains)) && (query.Length == 0
            || item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || item.Artist.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || item.Creator.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
        var sorted = sorting switch
        {
            "rating" => matches.OrderByDescending(item => item.Rating),
            "rating_low" => matches.OrderBy(item => item.Rating),
            "downloads" => matches.OrderByDescending(item => item.DownloadCount),
            "downloads_low" => matches.OrderBy(item => item.DownloadCount),
            _ => matches.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
        };
        var ordered = sorted.ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        var start = ordered.Length == 0 ? 0 : Math.Min(Math.Max(0, offset), (ordered.Length - 1) / limit * limit);
        var index = Array.FindIndex(ordered, item => item.Id == focus);
        if (index >= 0) { start = index / limit * limit; }
        return new CatalogPage { Items = ordered.Skip(start).Take(limit).ToArray(), Total = ordered.Length, Offset = start, Limit = limit };
    }
}
