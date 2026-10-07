using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BEATNET;

internal sealed class BeatNetDownloads : IDisposable
{
    private readonly Queue<BeatmapEntry> queue = new();
    private readonly HashSet<string> ids = new();
    private readonly CancellationTokenSource cancellation = new();
    private CancellationTokenSource checks = new();
    private bool playing;
    private readonly BeatNetClient client = new("http://92.5.175.72");
    private readonly BeatmapInstaller installer;
    private Task<(List<BeatmapEntry> Entries, BeatmapEntry Beatmap)>? pending;
    private Action<BeatmapEntry>? completedDownload;
    internal Func<Action<BeatmapEntry>?>? Started { get; set; }
    private volatile string progress = string.Empty;
    private volatile float fraction;
    private readonly Dictionary<string, RevisionJob> revisionJobs = new();

    private sealed class RevisionJob
    {
        internal string Revision = string.Empty;
        internal Task<BeatmapEntry>? Task;
        internal float RetryAt;
    }

    internal BeatNetDownloads(string dataPath) => installer = new BeatmapInstaller(dataPath);

    internal BeatmapEntry? Active { get; private set; }
    internal List<BeatmapEntry> Entries { get; private set; } = new();
    internal int Version { get; private set; }
    internal string Error { get; private set; } = string.Empty;
    internal bool NeedsReload { get; set; }
    internal int Count => ids.Count;
    internal string Progress => progress;
    internal float Fraction => fraction;
    internal bool Contains(string id) => ids.Contains(id);
    internal Dictionary<string, BeatmapEntry> Latest { get; } = new();
    internal int RevisionVersion { get; private set; }

    internal void CacheRevision(BeatmapEntry entry)
    {
        if (Latest.TryGetValue(entry.Id, out var cached)
            && (cached.Revision.Number > entry.Revision.Number || cached.Revision.Id == entry.Revision.Id)) { return; }
        Latest[entry.Id] = entry;
        RevisionVersion++;
    }

    internal void ObserveRevision(string id, string revision)
    {
        if (cancellation.IsCancellationRequested || revision.Length == 0
            || Latest.TryGetValue(id, out var cached) && cached.Revision.Id == revision) { return; }
        if (!revisionJobs.TryGetValue(id, out var job))
        {
            job = new RevisionJob();
            revisionJobs[id] = job;
        }
        if (job.Revision == revision) { return; }
        job.Revision = revision;
        job.RetryAt = 0f;
    }

    internal void ClearRevision(string id)
    {
        if (!revisionJobs.TryGetValue(id, out var job)) { return; }
        job.Revision = string.Empty;
        if (job.Task == null) { revisionJobs.Remove(id); }
    }

    private void CheckRevisions()
    {
        foreach (var pair in revisionJobs.ToArray())
        {
            var job = pair.Value;
            if (job.Task?.IsCompleted != true) { continue; }
            if (job.Revision.Length == 0)
            {
                _ = job.Task.Exception;
                revisionJobs.Remove(pair.Key);
                continue;
            }
            try
            {
                var entry = job.Task.GetAwaiter().GetResult();
                if (entry.Revision.Id == job.Revision)
                {
                    CacheRevision(entry);
                    revisionJobs.Remove(pair.Key);
                }
                else { job.RetryAt = Time.unscaledTime + 1f; }
            }
            catch (OperationCanceledException) { job.RetryAt = 0f; }
            catch (Exception) { job.RetryAt = Time.unscaledTime + 30f; }
            job.Task = null;
        }
        var active = revisionJobs.Values.Count(job => job.Task != null);
        foreach (var pair in revisionJobs)
        {
            if (active >= 4) { break; }
            var job = pair.Value;
            if (job.Task != null || Time.unscaledTime < job.RetryAt) { continue; }
            var id = pair.Key;
            var token = checks.Token;
            job.Task = Task.Run(() => client.Get(id, token), token);
            active++;
        }
    }

    internal int Position(string id)
    {
        var position = 1;
        foreach (var entry in queue)
        {
            if (entry.Id == id)
            {
                return position;
            }
            position++;
        }
        return 0;
    }

    internal bool Add(BeatmapEntry entry)
    {
        if (cancellation.IsCancellationRequested || !ids.Add(entry.Id))
        {
            return false;
        }
        queue.Enqueue(entry);
        return true;
    }

    internal void Tick()
    {
        if (playing) { return; }
        if (!cancellation.IsCancellationRequested) { CheckRevisions(); }
        if (pending?.IsCompleted == true)
        {
            var completed = Active!;
            try
            {
                var result = pending.GetAwaiter().GetResult();
                Entries = result.Entries;
                completedDownload?.Invoke(result.Beatmap);
                NeedsReload = true;
                Error = string.Empty;
            }
            catch (OperationCanceledException)
            {
                Error = $"Download cancelled / {completed.Title}";
            }
            catch (Exception)
            {
                Error = $"Download failed / {completed.Title}";
            }
            ids.Remove(completed.Id);
            completedDownload = null;
            pending = null;
            Active = null;
            Version++;
        }
        if (pending != null || queue.Count == 0 || cancellation.IsCancellationRequested)
        {
            return;
        }
        var entry = queue.Dequeue();
        Active = entry;
        completedDownload = Started?.Invoke();
        progress = "Starting download";
        fraction = 0f;
        var token = cancellation.Token;
        pending = Task.Run(async () =>
        {
            var details = await client.Get(entry.Id, token).ConfigureAwait(false);
            await installer.Install(client, details, value => progress = value, token,
                value => fraction = value).ConfigureAwait(false);
            return (installer.Library(), details);
        }, token);
    }

    internal void SetPlaying(bool value)
    {
        if (playing == value) { return; }
        playing = value;
        if (value) { checks.Cancel(); return; }
        checks.Dispose();
        checks = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
    }

    public void Dispose()
    {
        cancellation.Cancel();
        checks.Cancel();
        queue.Clear();
        ids.Clear();
        var task = Task.WhenAll(revisionJobs.Values.Select(job => job.Task).Where(job => job != null).Cast<Task>()
            .Append((Task?)pending ?? Task.CompletedTask));
        _ = task.ContinueWith(done =>
        {
            _ = done.Exception;
            client.Dispose();
            checks.Dispose();
            cancellation.Dispose();
        }, TaskScheduler.Default);
    }
}
