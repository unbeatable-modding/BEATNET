using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BEATNET;

internal sealed class BeatNetRatingCache
{
    private readonly Dictionary<string, Task<JObject>> entries = new();

    internal Task<JObject> Read(string account, Func<Task<JObject>> load)
    {
        if (!entries.TryGetValue(account, out var request)) { entries[account] = request = load(); }
        return request;
    }

    internal bool IsCurrent(string account, Task<JObject> request) =>
        entries.TryGetValue(account, out var current) && ReferenceEquals(current, request);

    internal static JObject ForMap(JObject result, string map) =>
        (result["items"] as JArray)?.OfType<JObject>().FirstOrDefault(item => (string?)item["projectId"] == map)
        ?? new JObject { ["projectId"] = map, ["value"] = null, ["available"] = false };

    internal Task<JObject>? Store(string account, string map, JObject result)
    {
        if (!entries.TryGetValue(account, out var request) || request.Status != TaskStatus.RanToCompletion)
        {
            entries.Remove(account);
            return null;
        }
        var snapshot = (JObject)request.Result.DeepClone();
        var items = snapshot["items"] as JArray;
        if (items == null) { snapshot["items"] = items = new JArray(); }
        foreach (var item in items.OfType<JObject>().Where(item => (string?)item["projectId"] == map).ToArray()) { item.Remove(); }
        items.Add(new JObject { ["projectId"] = map, ["value"] = result["value"]?.DeepClone(), ["available"] = result["available"]?.DeepClone() });
        return entries[account] = Task.FromResult(snapshot);
    }

    internal void Retry(string account)
    {
        if (entries.TryGetValue(account, out var request) && (request.IsFaulted || request.IsCanceled))
        {
            _ = request.Exception;
            entries.Remove(account);
        }
    }

    internal void Invalidate() => entries.Clear();
}
