using System;
using System.Collections.Generic;
using System.Linq;

namespace BEATNET;

internal static class BeatNetCatalog
{
    internal static CatalogPage LibraryPage(IEnumerable<BeatmapEntry> entries, string query, string sorting, bool descending, string[] difficulties, int offset, int limit, string focus)
    {
        var matches = entries.Where(item => (difficulties.Length == 0 || item.Difficulties.Any(difficulties.Contains)) && (query.Length == 0
            || item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || item.Artist.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || item.Creator.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));


        IOrderedEnumerable<BeatmapEntry> sorted;
        if (descending)
        {
            sorted = sorting switch
            {
                "rating" => matches.OrderBy(item => item.Rating),
                "downloads" => matches.OrderBy(item => item.DownloadCount),
                "created" => matches.OrderBy(item => item.CreatedAt),
                _ => matches.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase),
            };
        }
        else
        {
            sorted = sorting switch
            {
                "rating" => matches.OrderByDescending(item => item.Rating),
                "downloads" => matches.OrderByDescending(item => item.DownloadCount),
                "created" => matches.OrderByDescending(item => item.CreatedAt),
                _ => matches.OrderByDescending(item => item.Title, StringComparer.OrdinalIgnoreCase),
            };
        }
        var ordered = sorted.ToArray();
        var start = ordered.Length == 0 ? 0 : Math.Min(Math.Max(0, offset), (ordered.Length - 1) / limit * limit);
        var index = Array.FindIndex(ordered, item => item.Id == focus);
        if (index >= 0) { start = index / limit * limit; }
        return new CatalogPage { Items = ordered.Skip(start).Take(limit).ToArray(), Total = ordered.Length, Offset = start, Limit = limit };
    }
}
