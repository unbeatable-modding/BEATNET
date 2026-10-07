using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BEATNET;

internal sealed partial class BeatNetAccounts
{
    private string downloadPath = string.Empty;
    private JArray downloadQueue = new();
    private Task? downloadTask;
    private float nextDownload;
    internal Dictionary<string, long> DownloadCounts { get; } = new();
    internal int DownloadVersion { get; private set; }

    private void InitializeDownloads(string dataPath)
    {
        downloadPath = Path.Combine(dataPath, "BEATNET_data", "download-queue.json");
        downloadQueue = Read(downloadPath)["queue"] as JArray ?? new JArray();
    }

    internal Action<BeatmapEntry>? TrackDownload()
    {
        if (disposed || User == null || Key.Length == 0) { return null; }
        var owner = UserId;
        return beatmap =>
        {
            if (disposed || downloadQueue.OfType<JObject>().Any(item => (string?)item["owner"] == owner
                && (string?)item["projectId"] == beatmap.Id)) { return; }
            downloadQueue.Add(new JObject { ["owner"] = owner, ["projectId"] = beatmap.Id, ["revisionId"] = beatmap.Revision.Id });
            SaveDownloads();
            nextDownload = 0f;
        };
    }

    private void SaveDownloads()
    {
        try { BeatNetOnlineClient.Save(downloadPath, new JObject { ["queue"] = downloadQueue }); }
        catch (Exception) { CustomSongLoader.Logger?.LogWarning("cannot save download queue"); }
    }

    private void UploadDownloads()
    {
        if (downloadTask?.IsCompleted == true) { _ = downloadTask.Exception; downloadTask = null; }
        if (playing || downloadTask != null || User == null || Busy || Time.unscaledTime < nextDownload) { return; }
        var entry = downloadQueue.OfType<JObject>().FirstOrDefault(item => (string?)item["owner"] == UserId);
        if (entry == null) { return; }
        nextDownload = Time.unscaledTime + 1f;
        var key = Key;
        var id = (string)entry["projectId"]!;
        var input = new JObject { ["action"] = "download", ["key"] = key,
            ["projectId"] = id, ["revisionId"] = entry["revisionId"]!.DeepClone() };
        var token = activity.Token;
        downloadTask = Task.Run(async () =>
        {
            try
            {
                var result = await Request(input, token).ConfigureAwait(false);
                var count = (long?)result["downloadCount"] ?? -1;
                if (count < 0) { throw new InvalidDataException("Invalid download count"); }
                callbacks.Enqueue(() =>
                {
                    DownloadCounts[id] = count;
                    DownloadVersion++;
                    entry.Remove();
                    SaveDownloads();
                });
            }
            catch (OnlineException error) when (error.Code == "beatmap_removed" || error.Code == "invalid_revision")
            {
                callbacks.Enqueue(() => { entry.Remove(); SaveDownloads(); });
            }
            catch (OnlineException error) when (error.Code == "unauthorized")
            {
                callbacks.Enqueue(() => { if (Key == key) { User = null; nextRestore = 0f; } });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { callbacks.Enqueue(() => nextDownload = Time.unscaledTime + 30f); }
        });
    }
}
