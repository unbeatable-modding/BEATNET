using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BEATNET;

internal sealed class BeatNetDownloads : IDisposable
{
    private readonly Queue<BeatmapEntry> queue = new();
    private readonly HashSet<string> ids = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly BeatNetClient client = new("http://92.5.175.72");
    private readonly BeatmapInstaller installer;
    private Task<List<BeatmapEntry>>? pending;
    private volatile string progress = string.Empty;
    private volatile float fraction;

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
        if (pending?.IsCompleted == true)
        {
            var completed = Active!;
            try
            {
                Entries = pending.GetAwaiter().GetResult();
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
                CustomSongLoader.Logger?.LogWarning("beatnet download failed");
            }
            ids.Remove(completed.Id);
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
        progress = "Starting download";
        fraction = 0f;
        var token = cancellation.Token;
        pending = Task.Run(async () =>
        {
            var details = await client.Get(entry.Id, token).ConfigureAwait(false);
            await installer.Install(client, details, value => progress = value, token,
                value => fraction = value).ConfigureAwait(false);
            return installer.Library();
        }, token);
    }

    public void Dispose()
    {
        cancellation.Cancel();
        queue.Clear();
        ids.Clear();
        var task = pending ?? Task.CompletedTask;
        _ = task.ContinueWith(done =>
        {
            _ = done.Exception;
            client.Dispose();
            cancellation.Dispose();
        }, TaskScheduler.Default);
    }
}
