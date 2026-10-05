using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BEATNET;

internal sealed class BeatNetExploreScores : IDisposable
{
    private const float CycleTime = 3f;
    private readonly Func<BeatmapEntry, string, CancellationToken, Task<GlobalScores>> fetch;
    private BeatmapEntry? entry;
    private string stamp = string.Empty;
    private string[] difficulties = Array.Empty<string>();
    private GlobalScore[] scores = Array.Empty<GlobalScore>();
    private int index;
    private float cycleAt;
    private float refreshAt;
    private Task<GlobalScores>? pending;
    private CancellationTokenSource? cancellation;

    internal string Error { get; private set; } = string.Empty;
    internal GlobalScore Current => scores.FirstOrDefault(score => score.Difficulty == Difficulty) ?? new GlobalScore { Difficulty = Difficulty };
    internal string Difficulty => difficulties.Length > 0 ? difficulties[index] : string.Empty;
    internal string Label => entry?.DifficultyLabels.TryGetValue(Difficulty, out var label) == true ? label : Difficulty;

    internal BeatNetExploreScores(Func<BeatmapEntry, string, CancellationToken, Task<GlobalScores>> fetch) => this.fetch = fetch;

    internal void Tick(float now, BeatmapEntry? beatmap, string modifiers)
    {
        var next = beatmap == null ? string.Empty : beatmap.Id + ":" + beatmap.Revision.Id + ":" + modifiers;
        if (next != stamp)
        {
            Cancel();
            stamp = next;
            entry = beatmap;
            scores = Array.Empty<GlobalScore>();
            difficulties = beatmap?.Difficulties.Distinct().ToArray() ?? Array.Empty<string>();
            index = 0;
            cycleAt = now + CycleTime;
            refreshAt = now;
            Error = string.Empty;
        }
        if (entry == null) { return; }
        if (difficulties.Length > 1 && now >= cycleAt)
        {
            var steps = (int)Math.Floor((now - cycleAt) / CycleTime) + 1;
            index = (index + steps) % difficulties.Length;
            cycleAt += steps * CycleTime;
        }
        if (pending?.IsCompleted == true)
        {
            try
            {
                var result = pending.GetAwaiter().GetResult();
                if (result.RevisionId != entry.Revision.Id || result.Items == null || result.Items.Length > difficulties.Length
                    || result.Items.Select(score => score.Difficulty).Distinct().Count() != result.Items.Length
                    || result.Items.Any(score => !difficulties.Contains(score.Difficulty) || score.Score < 0
                        || float.IsNaN(score.Accuracy) || float.IsInfinity(score.Accuracy) || score.Accuracy < 0 || score.Accuracy > 1))
                {
                    throw new InvalidDataException("The server returned invalid global highscores");
                }
                scores = result.Items;
                Error = string.Empty;
            }
            catch (Exception error) { Error = error is OnlineException ? error.Message : "Could not load global highscores / try again"; }
            pending = null;
            cancellation?.Dispose();
            cancellation = null;
            refreshAt = now + (Error.Length > 0 ? 10f : 30f);
        }
        if (pending != null || now < refreshAt || difficulties.Length == 0) { return; }
        cancellation = new CancellationTokenSource();
        pending = fetch(entry, modifiers, cancellation.Token);
    }

    private void Cancel()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        if (pending != null)
        {
            _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            pending = null;
        }
    }

    public void Dispose() => Cancel();
}

internal static class BeatNetScoreText
{
    internal static string Difficulty(string value) => value.Length == 0 || value.EndsWith(".", StringComparison.Ordinal) ? value : value + ".";

    internal static string ClearState(bool cleared, bool fullCombo, bool perfectCombo) => !cleared ? string.Empty
        : perfectCombo ? "cleared. / perfect combo." : fullCombo ? "cleared. / full combo." : "cleared.";
}
