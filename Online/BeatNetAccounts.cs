using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Arcade.UI.SongSelect;
using CrossPlatform;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BEATNET;

internal sealed partial class BeatNetAccounts : IDisposable
{
    private readonly BeatNetOnlineClient client;
    private readonly BeatmapInstaller installer;
    private readonly CancellationTokenSource cancellation = new();
    private CancellationTokenSource activity;
    private bool playing;
    private readonly ConcurrentQueue<Action> callbacks = new();
    private readonly Dictionary<string, JObject> boards = new();
    private readonly Dictionary<string, string> revisions = new();
    private readonly Dictionary<string, int> revisionNumbers = new();
    private readonly string sessionPath;
    private readonly string queuePath;
    private readonly string revisionsPath;
    private JObject stored;
    private JArray queue;
    private JObject owners;
    private JObject invalid;
    private JObject localVersions;
    private Task? accountTask;
    private Task? scoreTask;
    private Task? revisionTask;
    private Task? profileTask;
    private float nextProfile;
    private string profileStamp = string.Empty;
    private bool profileDirty = true;
    private float nextRestore;
    private float nextUpload;
    private float nextCheck;
    private string revisionStamp = string.Empty;
    private int revisionGeneration;
    private JObject? runBoard;
    private string runSong = string.Empty;
    private bool runInvalid;
    private bool imported;
    private bool disposed;

    private Task? bpTask;
    private string bpOwner = string.Empty;
    private float nextBp;
    private int bpGeneration;
    private readonly HashSet<string> rewardScores = new();
    private string runOwner = string.Empty;

    internal double? Bp { get; private set; }
    internal string BpVersion { get; private set; } = string.Empty;
    internal int BpChanged { get; private set; }
    internal BeatNetBpReward? BpReward { get; private set; }

    private string scoresPath = string.Empty;
    private JObject accountScores = new();
    private string savedOwner = string.Empty;
    private string? scoreOwner;
    private HighScoreList? scoreList;
    private Task? syncTask;
    private string scoreSession = string.Empty;
    private float nextScoreSync;
    internal int ScoreVersion { get; private set; }

    internal JObject? User { get; private set; }
    internal string Name => (string?)User?["username"] ?? string.Empty;
    internal string UserId => (string?)User?["id"] ?? string.Empty;
    internal bool Busy => accountTask != null;
    internal string Error { get; private set; } = string.Empty;
    internal string State => User != null ? "Logged in as " + Name : Busy ? "Connecting account" : "Not logged in";
    internal string Key => (string?)stored["key"] ?? string.Empty;
    internal int RevisionVersion { get; private set; }
    internal BeatNetRatingCache Ratings { get; } = new();

    internal BeatNetAccounts(string dataPath, BeatNetOnlineClient? client = null)
    {
        activity = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        this.client = client ?? new BeatNetOnlineClient(dataPath);
        installer = new BeatmapInstaller(dataPath);
        InitializeDownloads(dataPath);
        var root = Path.Combine(dataPath, "BEATNET_data");
        sessionPath = Path.Combine(root, "account-key.json");
        queuePath = Path.Combine(root, "score-queue.json");
        revisionsPath = Path.Combine(root, "score-revisions.json");
        stored = Read(sessionPath);
        var pending = Read(queuePath);
        queue = pending["queue"] as JArray ?? new JArray();
        owners = pending["owners"] as JObject ?? new JObject();
        var savedRevisions = Read(revisionsPath);
        invalid = savedRevisions["remote"] as JObject ?? new JObject();
        localVersions = savedRevisions["local"] as JObject ?? new JObject();
        InitializeScores(root);
    }

    private static JObject Read(string path)
    {
        try { return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject(); }
        catch (Exception) { CustomSongLoader.Logger?.LogWarning("cannot read beatnet account data"); return new JObject(); }
    }

    internal static string SteamName()
    {
        try { return PlatformManager.Platform?.GetPlayerName() ?? string.Empty; }
        catch (Exception) { return string.Empty; }
    }

    private static async Task<string> SteamAvatar(string id, CancellationToken token)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9]{17}$"))
        {
            return string.Empty;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var response = await http.GetAsync("https://steamcommunity.com/profiles/" + id + "?xml=1", token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (xml.Length > 262144)
            {
                return string.Empty;
            }
            using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader).Root?.Element("avatarFull")?.Value ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    internal void Login(string name, string password, bool register)
    {
        if (Busy) { return; }
        Error = string.Empty;
        if (string.IsNullOrWhiteSpace(name) || UnicodeLength(name) > 32)
        {
            Error = "Use a username with 1 to 32 Unicode characters";
            return;
        }
        name = name.Normalize(System.Text.NormalizationForm.FormC);
        var steamId = PlatformManager.Platform?.GetLocalUserId().PlatformId ?? string.Empty;
        var token = cancellation.Token;
        var previousKey = Key;
        accountTask = Task.Run(async () =>
        {
            var hash = BeatNetOnlineClient.Password(name, password);
            var avatar = await SteamAvatar(steamId, token).ConfigureAwait(false);
            var result = await client.Send(new JObject { ["action"] = register ? "register" : "login", ["username"] = name, ["password"] = hash, ["avatar"] = avatar }, token).ConfigureAwait(false);
            callbacks.Enqueue(() =>
            {
                BeatNetOnlineClient.Save(sessionPath, result);
                stored = result;
                User = result["user"] as JObject;
                ProfileChanged();
                imported = false;
                Error = string.Empty;
            });
            if (previousKey.Length > 0)
            {
                try { await client.Send(new JObject { ["action"] = "logout", ["key"] = previousKey }, token).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }, token);
    }

    internal void Logout()
    {
        if (Busy) { return; }
        var key = Key;
        accountTask = Task.Run(async () =>
        {
            if (key.Length > 0) { await client.Send(new JObject { ["action"] = "logout", ["key"] = key }, cancellation.Token).ConfigureAwait(false); }
            callbacks.Enqueue(() =>
            {
                stored = new JObject();
                BeatNetOnlineClient.Save(sessionPath, stored);
                User = null;
                Error = string.Empty;
            });
        });
    }

    private static int UnicodeLength(string value)
    {
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) { return int.MaxValue; }
                i++;
            }
            else if (char.IsLowSurrogate(value[i])) { return int.MaxValue; }
            count++;
        }
        return count;
    }

    internal void Post(Action action) => callbacks.Enqueue(action);
    internal async Task<JObject> Request(JObject input, CancellationToken token = default)
    {
        if (((string?)input["action"] == "leaderboard" || (string?)input["action"] == "bp_leaderboard") && profileTask != null) { await profileTask.ConfigureAwait(false); }
        if (!token.CanBeCanceled) { return await client.Send(input, cancellation.Token).ConfigureAwait(false); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation.Token);
        return await client.Send(input, linked.Token).ConfigureAwait(false);
    }

    internal void SyncProfile()
    {
        if (profileTask?.IsCompleted == true) { _ = profileTask.Exception; profileTask = null; }
        if (playing || !profileDirty || profileTask != null || User == null || Busy || Time.unscaledTime < nextProfile) { return; }
        profileDirty = false;
        JObject? profile;
        try { profile = BeatNetProfile.Read(); }
        catch (Exception) { return; }
        if (profile == null) { return; }
        var key = Key;
        var stamp = key + profile.ToString(Newtonsoft.Json.Formatting.None);
        if (stamp == profileStamp) { return; }
        profileTask = Task.Run(async () =>
        {
            try
            {
                var result = await client.Send(new JObject { ["action"] = "profile", ["key"] = key, ["profile"] = profile }, cancellation.Token).ConfigureAwait(false);
                callbacks.Enqueue(() =>
                {
                    if (Key != key) { return; }
                    User = result["user"] as JObject;
                    profileStamp = stamp;
                });
            }
            catch (Exception)
            {
                callbacks.Enqueue(() =>
                {
                    if (Key != key) { return; }
                    profileDirty = true;
                    nextProfile = Time.unscaledTime + 30f;
                });
            }
        });
    }

    internal void ProfileChanged()
    {
        profileDirty = true;
        nextProfile = 0f;
    }

    internal void Tick()
    {
        if (playing) { return; }
        while (callbacks.TryDequeue(out var callback))
        {
            try { callback(); }
            catch (Exception) { Error = "Could not save BEATNET data / please try again"; }
        }
        if (accountTask?.IsCompleted == true)
        {
            try { accountTask.GetAwaiter().GetResult(); }
            catch (Exception error)
            {
                Error = error is OnlineException ? error.Message : "Could not connect to BEATNET / try again";
                if (error is OnlineException online && online.Code == "unauthorized")
                {
                    stored = new JObject();
                    BeatNetOnlineClient.Save(sessionPath, stored);
                    User = null;
                }
            }
            accountTask = null;
        }
        if (!Busy && User == null && Key.Length > 0 && Time.unscaledTime >= nextRestore)
        {
            nextRestore = Time.unscaledTime + 30f;
            var key = Key;
            accountTask = Task.Run(async () =>
            {
                var result = await Request(new JObject { ["action"] = "me", ["key"] = key }).ConfigureAwait(false);
                callbacks.Enqueue(() => { User = result["user"] as JObject; ProfileChanged(); Error = string.Empty; });
            });
        }
        SyncProfile();
        SyncBeatpoints();
        SyncDownloads();
        UploadDownloads();
        if (User != null && !Busy)
        {
            var key = Key;
            _ = Ratings.Read(key, () => Request(new JObject { ["action"] = "ratings", ["key"] = key }));
        }
        if (ArcadeSongDatabase.Instance == null) { return; }
        SyncScores();
        if (User != null && !imported)
        {
            imported = true;
            foreach (var score in FileStorage.highscores._highScores.Values.ToArray())
            {
                if ((string?)owners[score.song] == UserId) { Capture(score, true, false); }
            }
        }
        Upload();
        CheckRevisions();
    }

    internal void RegisterSongs()
    {
        downloadSession = string.Empty;
        boards.Clear();
        var entries = installer.Library().Where(e => BeatNetClient.IsId(e.Id)).ToArray();
        revisions.Clear();
        revisionNumbers.Clear();
        foreach (var entry in entries)
        {
            revisions[entry.Id] = entry.Revision.Id;
            revisionNumbers[entry.Id] = entry.Revision.Number;
        }
        foreach (var item in ArcadeSongDatabase.Instance.SongDatabase.Values.Where(i => i.CustomSong))
        {
            var path = Path.GetFullPath(item.Song.CustomPath);
            var entry = entries.FirstOrDefault(e => path.Equals(e.LocalPath, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(e.LocalPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (entry == null) { continue; }
            boards[item.Path] = new JObject
            {
                ["projectId"] = entry.Id, ["revisionId"] = entry.Revision.Id,
                ["chart"] = BeatNetOnlineClient.Hash(item.BeatmapInfo.text.TrimStart('\uFEFF')),
                ["difficulty"] = item.BeatmapInfo.difficulty,
            };
        }
        foreach (var entry in entries)
        {
            if ((string?)runBoard?["projectId"] == entry.Id && (string?)runBoard["revisionId"] != entry.Revision.Id) { runInvalid = true; }
            if (localVersions[entry.Id] is JValue previous && (string?)previous != entry.Revision.Id)
            {
                ClearScores(entry.Id);
                if ((string?)invalid[entry.Id] == (string?)previous || (string?)invalid[entry.Id] == entry.Revision.Id) { invalid.Remove(entry.Id); }
            }
            localVersions[entry.Id] = entry.Revision.Id;
            if (invalid[entry.Id] is JValue revision && (string?)revision != entry.Revision.Id)
            {
                Plugin.Downloads?.ObserveRevision(entry.Id, (string?)revision ?? string.Empty);
                ClearScores(entry.Id);
            }
            if (!HasUpdate(entry.Id)) { Plugin.Downloads?.ClearRevision(entry.Id); }
        }
        SaveRevisions();
        imported = false;
        nextCheck = 0f;
        revisionStamp = string.Empty;
        revisionGeneration++;
        RevisionVersion++;
        SyncScores(true);
    }

    internal JObject? Board(string path)
    {
        var separator = path.IndexOf('\\');
        var song = separator < 0 ? path : path.Substring(0, separator);
        if (!boards.TryGetValue(song, out var board)) { return null; }
        var result = (JObject)board.DeepClone();
        result["modifiers"] = separator < 0 ? "\\Classic" : path.Substring(separator);
        return result;
    }

    internal bool IsStale(JObject board) => HasUpdate((string)board["projectId"]!)
        || invalid[(string)board["projectId"]!] is JValue value && (string?)value != (string?)board["revisionId"];
    internal bool HasUpdate(string id)
    {
        if (!revisions.TryGetValue(id, out var installed)) { return false; }
        if (invalid[id] is JValue remote && (string?)remote != installed) { return true; }
        return Plugin.Downloads?.Latest.TryGetValue(id, out var entry) == true && entry.Revision.Number > revisionNumbers[id];
    }

    internal void BeginRun(string path)
    {
        SetPlaying(true);
        Plugin.Downloads?.SetPlaying(true);
        var separator = path.IndexOf('\\');
        runSong = separator < 0 ? path : path.Substring(0, separator);
        runBoard = Board(path);
        runInvalid = runBoard != null && IsStale(runBoard);
        runOwner = UserId + "/" + Key;
        rewardScores.Clear();
        BpReward = null;
    }

    internal void SetPlaying(bool value)
    {
        if (playing == value) { return; }
        playing = value;
        revisionGeneration++;
        revisionStamp = string.Empty;
        nextCheck = nextUpload = 0f;
        if (value) { activity.Cancel(); return; }
        Ratings.Invalidate();
        var previous = activity;
        activity = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        _ = Task.WhenAll(scoreTask ?? Task.CompletedTask, revisionTask ?? Task.CompletedTask)
            .ContinueWith(done => { _ = done.Exception; previous.Dispose(); }, TaskScheduler.Default);
    }

    internal JObject? ScoreBoard(string path)
    {
        var current = Board(path);
        var separator = path.IndexOf('\\');
        var song = separator < 0 ? path : path.Substring(0, separator);
        if (runBoard == null || song != runSong) { return current; }
        if (runInvalid || current == null || (string?)current["revisionId"] != (string?)runBoard["revisionId"] || IsStale(runBoard))
        {
            runInvalid = true;
            return null;
        }
        var result = (JObject)runBoard.DeepClone();
        result["modifiers"] = current["modifiers"]?.DeepClone();
        return result;
    }

    internal bool CanSaveScore(string path)
    {
        if (IsCustomScore(path) && User == null) { return false; }
        if (Board(path) == null) { return runBoard == null || !path.StartsWith(runSong + "\\", StringComparison.Ordinal) && path != runSong; }
        var board = ScoreBoard(path);
        return board != null && !IsStale(board);
    }

    internal void Capture(HighScoreItem score, bool ownsLocal = false, bool fromRun = true)
    {
        if (!CanSaveScore(score.song)) { return; }
        var board = fromRun ? ScoreBoard(score.song) : Board(score.song);
        if (ownsLocal && UserId.Length > 0 && IsCustomScore(score.song))
        {
            owners[score.song] = UserId;
            var local = FileStorage.highscores?._highScores.TryGetValue(score.song, out var best) == true ? best : score;
            CacheScore(UserId, local, (string?)board?["revisionId"] ?? string.Empty);
            SaveAccountScores();
            ScoreVersion++;
        }
        if (board == null || IsStale(board) || !HighScoreList.IsScoreSaveable(score.modifierMask)) { return; }
        var owner = UserId.Length > 0 ? UserId : (string?)stored["user"]?["id"] ?? string.Empty;
        if (owner.Length == 0)
        {
            if (ownsLocal) { owners[score.song] = string.Empty; SaveQueue(); }
            return;
        }
        score._notes ??= score.notes?.ToDictionary(note => note.timing, note => note.count) ?? new Dictionary<string, int>();
        board["action"] = "submit";
        board["score"] = score.score;
        board["accuracy"] = score.accuracy;
        board["maxCombo"] = score.maxCombo;
        board["cleared"] = score.cleared;
        board["noMiss"] = score.IsNoMiss();
        board["fullCombo"] = score.IsFullCombo();
        board["perfectCombo"] = score.IsPerfectFullCombo();
        if (ownsLocal) { owners[score.song] = owner; }
        var currentRun = fromRun && runBoard != null && (string?)board["projectId"] == (string?)runBoard["projectId"]
            && (string?)board["difficulty"] == (string?)runBoard["difficulty"] && runOwner == UserId + "/" + Key;
        var existing = queue.OfType<JObject>().FirstOrDefault(q => (string?)q["owner"] == owner && (string?)q["song"] == score.song
            && (string?)q["payload"]?["revisionId"] == (string?)board["revisionId"]);
        var reward = false;
        if (existing != null)
        {
            var old = (JObject)existing["payload"]!;
            if ((bool)old["cleared"]! && !score.cleared || (bool)old["cleared"]! == score.cleared && (int)old["score"]! > score.score) { return; }
            if ((bool)old["cleared"]! == score.cleared && (int)old["score"]! == score.score && (float)old["accuracy"]! >= score.accuracy)
            {
                if (currentRun) { rewardScores.Add((string)existing["id"]!); }
                SaveQueue();
                return;
            }
            reward = rewardScores.Remove((string)existing["id"]!);
            existing.Remove();
        }
        var queuedId = Guid.NewGuid().ToString("N");
        queue.Add(new JObject { ["id"] = queuedId, ["owner"] = owner, ["song"] = score.song, ["payload"] = board });
        if (reward || currentRun)
        {
            rewardScores.Add(queuedId);
        }
        SaveQueue();
    }

    private void SaveQueue() => BeatNetOnlineClient.Save(queuePath, new JObject { ["queue"] = queue, ["owners"] = owners });

    private void Upload()
    {
        if (scoreTask?.IsCompleted == true) { _ = scoreTask.Exception; scoreTask = null; }
        if (playing || scoreTask != null || User == null || Busy || Time.unscaledTime < nextUpload) { return; }
        var entry = queue.OfType<JObject>().FirstOrDefault(q => (string?)q["owner"] == UserId);
        if (entry == null) { return; }
        nextUpload = Time.unscaledTime + 1f;
        var input = (JObject)entry["payload"]!.DeepClone();
        input["key"] = Key;
        var id = (string)entry["id"]!;
        var generation = revisionGeneration;
        var previous = (string?)invalid[(string)input["projectId"]!];
        var token = activity.Token;
        scoreTask = Task.Run(async () =>
        {
            try
            {
                if (!await CheckScoreRevision(input, generation, previous, token).ConfigureAwait(false))
                {
                    callbacks.Enqueue(() => { queue.OfType<JObject>().FirstOrDefault(q => (string?)q["id"] == id)?.Remove(); SaveQueue(); });
                    return;
                }
                var result = await client.Send(input, token).ConfigureAwait(false);
                callbacks.Enqueue(() =>
                {
                    queue.OfType<JObject>().FirstOrDefault(q => (string?)q["id"] == id)?.Remove();
                    SaveQueue();
                    Ratings.Invalidate();
                    ReadBpGain(result, input, id);
                    RefreshBp();
                });
            }
            catch (OnlineException error) when (error.Code == "revision_changed" || error.Code == "beatmap_removed" || error.Code == "invalid_chart")
            {
                callbacks.Enqueue(() => { queue.OfType<JObject>().FirstOrDefault(q => (string?)q["id"] == id)?.Remove(); SaveQueue(); nextCheck = 0f; });
                try { await CheckScoreRevision(input, generation, previous, token).ConfigureAwait(false); }
                catch (Exception) { }
            }
            catch (OnlineException error) when (error.Code == "unauthorized")
            {
                callbacks.Enqueue(() => { if (Key == (string?)input["key"]) { User = null; nextRestore = 0f; } });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { callbacks.Enqueue(() => nextUpload = Time.unscaledTime + 30f); }
        });
    }

    private async Task<bool> CheckScoreRevision(JObject input, int generation, string? previous, CancellationToken token)
    {
        var id = (string)input["projectId"]!;
        var result = await client.Send(new JObject { ["action"] = "revisions", ["version"] = string.Empty, ["ids"] = new JArray(id) }, token).ConfigureAwait(false);
        var current = (result["items"] as JArray)?.OfType<JObject>().FirstOrDefault(item => (string?)item["id"] == id);
        var revision = (string?)current?["revisionId"] ?? string.Empty;
        if (revision == (string?)input["revisionId"]) { return true; }
        callbacks.Enqueue(() =>
        {
            if (generation == revisionGeneration && (string?)invalid[id] == previous) { ObserveRevision(id, revision); }
            nextCheck = 0f;
        });
        return false;
    }

    private void CheckRevisions()
    {
        if (revisionTask?.IsCompleted == true) { _ = revisionTask.Exception; revisionTask = null; }
        if (playing || revisionTask != null || Time.unscaledTime < nextCheck) { return; }
        if (revisions.Count == 0) { nextCheck = Time.unscaledTime + 1f; return; }
        var entries = revisions.Keys.Take(1024).ToArray();
        var stamp = revisionStamp;
        var generation = revisionGeneration;
        var token = activity.Token;
        revisionTask = Task.Run(async () =>
        {
            try
            {
                var result = await client.Send(new JObject { ["action"] = "revisions", ["version"] = stamp, ["ids"] = new JArray(entries) }, token).ConfigureAwait(false);
                callbacks.Enqueue(() =>
                {
                    if (generation != revisionGeneration) { return; }
                    revisionStamp = (string?)result["version"] ?? string.Empty;
                    var version = RevisionVersion;
                    var remote = (result["items"] as JArray ?? new JArray()).OfType<JObject>()
                        .ToDictionary(entry => (string)entry["id"]!, entry => (string)entry["revisionId"]!);
                    foreach (var id in entries)
                    {
                        ObserveRevision(id, remote.TryGetValue(id, out var revision) ? revision : string.Empty, false);
                    }
                    if (version != RevisionVersion) { SaveRevisions(); }
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { callbacks.Enqueue(() => { if (generation == revisionGeneration) { nextCheck = Time.unscaledTime + 30f; } }); }
        });
    }

    internal void ObserveRevision(string id, string revision, bool save = true)
    {
        if (!revisions.TryGetValue(id, out var installed) || installed == revision && invalid[id] == null) { return; }
        if ((string?)invalid[id] != revision)
        {
            if (installed == revision) { invalid.Remove(id); }
            else { invalid[id] = revision; }
            if ((string?)runBoard?["projectId"] == id && (string?)runBoard["revisionId"] != revision) { runInvalid = true; }
            if (save) { SaveRevisions(); }
            RevisionVersion++;
            Ratings.Invalidate();
            if (installed != revision)
            {
                Plugin.Downloads?.ObserveRevision(id, revision);
                ClearScores(id);
            }
            else { Plugin.Downloads?.ClearRevision(id); }
        }
    }

    private void SaveRevisions() => BeatNetOnlineClient.Save(revisionsPath, new JObject { ["remote"] = invalid, ["local"] = localVersions });

    private void ClearScores(string id)
    {
        var prefix = "CUSTOM_BEATNET_beatmaps/" + id;
        bool Matches(string key) => key.StartsWith(prefix + "/", StringComparison.Ordinal) || key.StartsWith(prefix + "\\", StringComparison.Ordinal);
        ClearAccountScores(Matches);
        var scores = FileStorage.highscores;
        var changed = false;
        foreach (var key in scores._highScores.Keys.Where(Matches).ToArray()) { scores._highScores.Remove(key); owners.Remove(key); changed = true; }
        foreach (var item in ArcadeSongDatabase.Instance.SongDatabase.Values.Where(i => Matches(i.Path))) { item.Highscore.Clear(); }
        foreach (var item in queue.OfType<JObject>().Where(q => (string?)q["payload"]?["projectId"] == id).ToArray()) { item.Remove(); }
        if (changed) { FileStorage.SaveHighscores(); HighScoreList.HighScoresUpdated?.Invoke(); ScoreVersion++; }
        ClearProfileFiles(id);
        SaveQueue();
    }

    private void ClearProfileFiles(string id)
    {
        var root = Path.Combine(Application.persistentDataPath, "PROFILES");
        if (!Directory.Exists(root)) { return; }
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            foreach (var file in new[] { "arcade-highscores.json", "old_arcade-highscores.json", "temp_arcade-highscores.json" })
            {
                var path = Path.Combine(folder, file);
                if (!File.Exists(path)) { continue; }
                try
                {
                    BeatmapInstaller.CheckParents(path);
                    var data = JObject.Parse(File.ReadAllText(path));
                    if (data["highScores"] is not JArray rows) { continue; }
                    var prefix = "CUSTOM_BEATNET_beatmaps/" + id + "/";
                    var old = rows.OfType<JObject>().Where(row => ((string?)row["song"] ?? "").StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                    foreach (var row in old) { row.Remove(); }
                    if (old.Length > 0) { BeatNetOnlineClient.Save(path, data); }
                }
                catch (Exception) { CustomSongLoader.Logger?.LogWarning("cannot clear old beatnet scores"); }
            }
        }
    }

    internal void FinishBpReward(BeatNetBpReward? reward)
    {
        if (BpReward == reward) { BpReward = null; }
    }

    private void ReadBpGain(JObject result, JObject input, string id)
    {
        if (User == null || Key != (string?)input["key"]) { return; }
        var points = (double?)result["bp"];
        var before = (double?)result["bpBefore"];
        var gain = (double?)result["bpGain"];
        var version = (string?)result["version"];
        if (!Valid(points) || !Valid(before) || !Valid(gain) || string.IsNullOrEmpty(version)) { return; }
        bpGeneration++;
        Bp = points;
        BpVersion = version!;
        BpChanged++;
        if (rewardScores.Remove(id) && runOwner == UserId + "/" + Key && gain > 0 && points > before)
        {
            BpReward = new BeatNetBpReward((string)input["projectId"]!, (string)input["difficulty"]!, before!.Value, points!.Value, runOwner);
        }
    }

    private static bool Valid(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && value >= 0;

    internal void RefreshBp() => nextBp = 0f;

    private void SyncBeatpoints()
    {
        var owner = UserId + "/" + Key;
        if (bpOwner != owner)
        {
            bpOwner = owner;
            Bp = null;
            BpVersion = string.Empty;
            nextBp = 0f;
            BpChanged++;
            bpGeneration++;
            BpReward = null;
            rewardScores.Clear();
        }
        if (bpTask?.IsCompleted == true) { _ = bpTask.Exception; bpTask = null; }
        if (User == null || Busy || bpTask != null || Time.unscaledTime < nextBp) { return; }
        nextBp = Time.unscaledTime + 30f;
        var key = Key;
        var generation = bpGeneration;
        bpTask = Task.Run(async () =>
        {
            try
            {
                var result = await Request(new JObject { ["action"] = "beatpoints", ["key"] = key }).ConfigureAwait(false);
                var points = (double?)result["bp"];
                var version = (string?)result["version"];
                if (!points.HasValue || double.IsNaN(points.Value) || double.IsInfinity(points.Value) || points < 0 || string.IsNullOrEmpty(version))
                {
                    throw new InvalidOperationException("Invalid beatpoints response");
                }
                Post(() =>
                {
                    if (UserId + "/" + Key != owner || bpGeneration != generation) { return; }
                    if (Bp != points || BpVersion != version) { BpChanged++; }
                    Bp = points;
                    BpVersion = version!;
                });
            }
            catch (Exception) { }
        });
    }

    private bool IsCustomScore(string path)
    {
        var separator = path.IndexOf('\\');
        return boards.ContainsKey(separator < 0 ? path : path.Substring(0, separator));
    }

    private void InitializeScores(string root)
    {
        scoresPath = System.IO.Path.Combine(root, "account-scores.json");
        accountScores = Read(scoresPath);
        savedOwner = (string?)stored["user"]?["id"] ?? string.Empty;
    }

    private void SaveAccountScores() => BeatNetOnlineClient.Save(scoresPath, accountScores);

    private bool CacheScore(string owner, HighScoreItem score, string revision)
    {
        if (owner.Length == 0) { owner = "legacy"; }
        var rows = accountScores[owner] as JObject;
        if (rows == null) { rows = new JObject(); accountScores[owner] = rows; }
        if (rows[score.song] is JObject old && (string?)old["revision"] == revision && old["score"] is JObject previous
            && ((bool?)previous["cleared"] == true && !score.cleared
                || (bool?)previous["cleared"] == score.cleared && (int?)previous["score"] > score.score)) { return false; }
        score._notes ??= score.notes?.ToDictionary(note => note.timing, note => note.count) ?? new Dictionary<string, int>();
        rows[score.song] = new JObject { ["revision"] = revision, ["score"] = JObject.FromObject(score) };
        return true;
    }

    private void StoreDisplayedScores()
    {
        var list = scoreList ?? FileStorage.highscores;
        if (list == null) { return; }
        foreach (var score in list._highScores.Values.Where(score => IsCustomScore(score.song)))
        {
            var owner = (string?)owners[score.song] ?? scoreOwner ?? savedOwner;
            CacheScore(owner, score, (string?)Board(score.song)?["revisionId"] ?? string.Empty);
        }
        SaveAccountScores();
    }

    private void SyncScores(bool refresh = false)
    {
        if (FileStorage.highscores == null || ArcadeSongDatabase.Instance == null) { return; }
        if (scoreOwner != UserId || scoreList != FileStorage.highscores || refresh)
        {
            StoreDisplayedScores();
            scoreOwner = UserId;
            scoreList = FileStorage.highscores;
            ApplyAccountScores();
        }
        LoadAccountScores();
    }

    private void ApplyAccountScores()
    {
        var list = FileStorage.highscores;
        if (list == null || ArcadeSongDatabase.Instance == null) { return; }
        foreach (var key in list._highScores.Keys.Where(IsCustomScore).ToArray()) { list._highScores.Remove(key); }
        if (UserId.Length > 0 && accountScores[UserId] is JObject rows)
        {
            foreach (var row in rows.Properties())
            {
                var board = Board(row.Name);
                if (board != null && (IsStale(board) || (string?)row.Value["revision"] != (string?)board["revisionId"])) { continue; }
                if (!IsCustomScore(row.Name) || row.Value["score"] is not JObject value) { continue; }
                try
                {
                    var score = value.ToObject<HighScoreItem>();
                    if (score == null || score.song != row.Name) { continue; }
                    list._highScores[row.Name] = score;
                    owners[row.Name] = UserId;
                }
                catch (Exception) { CustomSongLoader.Logger?.LogWarning("cannot restore beatnet score"); }
            }
        }
        foreach (var item in ArcadeSongDatabase.Instance.SongDatabase.Values.Where(item => IsCustomScore(item.Path)))
        {
            item.Highscore = list.GetAllScores(item.Path);
        }
        SaveQueue();
        FileStorage.SaveHighscores();
        HighScoreList.HighScoresUpdated?.Invoke();
        using var music = new BeatNetMusic();
        ArcadeSongList.Instance?.RefreshSelected();
        ScoreVersion++;
    }

    private void LoadAccountScores()
    {
        if (syncTask?.IsCompleted == true) { _ = syncTask.Exception; syncTask = null; }
        var session = Key + "/" + revisionGeneration;
        if (playing || User == null || Busy || syncTask != null || scoreSession == session || Time.unscaledTime < nextScoreSync) { return; }
        scoreSession = session;
        var key = Key;
        var owner = UserId;
        var generation = revisionGeneration;
        var token = activity.Token;
        syncTask = Task.Run(async () =>
        {
            try
            {
                var rows = new List<JObject>();
                for (var offset = 0; ; offset += 250)
                {
                    var result = await client.Send(new JObject { ["action"] = "scores", ["key"] = key, ["offset"] = offset, ["limit"] = 250 }, token).ConfigureAwait(false);
                    if (result["items"] is not JArray items || (int?)result["total"] is not int total || total < 0 || total > 100000
                        || items.Count > 250 || items.Count == 0 && offset < total) { throw new InvalidOperationException("invalid account scores"); }
                    rows.AddRange(items.OfType<JObject>());
                    if (offset + items.Count >= total) { break; }
                }
                callbacks.Enqueue(() =>
                {
                    if (UserId != owner || Key != key || revisionGeneration != generation) { return; }
                    StoreDisplayedScores();
                    foreach (var row in rows)
                    {
                        foreach (var board in boards.Where(pair => (string?)pair.Value["projectId"] == (string?)row["projectId"]
                            && (string?)pair.Value["revisionId"] == (string?)row["revisionId"] && (string?)pair.Value["chart"] == (string?)row["chart"]
                            && (string?)pair.Value["difficulty"] == (string?)row["difficulty"]))
                        {
                            if (IsStale(board.Value)) { continue; }
                            var modifiers = (string?)row["modifiers"] ?? string.Empty;
                            if (!System.Text.RegularExpressions.Regex.IsMatch(modifiers, @"^\\(?:Classic|[A-Za-z]+(?:&[A-Za-z]+)*)$")) { continue; }
                            var mask = default(Modifiers);
                            if (modifiers != "\\Classic" && !Enum.TryParse(modifiers.Substring(1).Replace('&', ','), out mask)) { continue; }
                            CacheScore(owner, new HighScoreItem
                            {
                                song = board.Key + modifiers, score = (int)row["score"]!, accuracy = (float)row["accuracy"]!,
                                maxCombo = (int)row["maxCombo"]!, cleared = (int)row["cleared"]! != 0,
                                isNoMiss = (int)row["noMiss"]! != 0, isFullCombo = (int)row["fullCombo"]! != 0,
                                isPerfectFullCombo = (int)row["perfectCombo"]! != 0, modifierMask = mask, _notes = new Dictionary<string, int>(),
                            }, (string)row["revisionId"]!);
                        }
                    }
                    SaveAccountScores();
                    ApplyAccountScores();
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                callbacks.Enqueue(() => { if (scoreSession == session) { scoreSession = string.Empty; } });
            }
            catch (Exception)
            {
                callbacks.Enqueue(() =>
                {
                    if (UserId != owner || Key != key || revisionGeneration != generation) { return; }
                    scoreSession = string.Empty;
                    nextScoreSync = Time.unscaledTime + 30f;
                });
            }
        }, token);
    }

    private void ClearAccountScores(Func<string, bool> matches)
    {
        foreach (var rows in accountScores.Properties().Select(property => property.Value).OfType<JObject>())
        {
            foreach (var row in rows.Properties().Where(row => matches(row.Name)).ToArray()) { row.Remove(); }
        }
        SaveAccountScores();
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        cancellation.Cancel();
        var tasks = new[] { accountTask, scoreTask, revisionTask, profileTask, downloadTask, downloadScan, syncTask, bpTask }.Where(t => t != null).Cast<Task>();
        _ = Task.WhenAll(tasks).ContinueWith(done => { _ = done.Exception; client.Dispose(); activity.Dispose(); cancellation.Dispose(); }, TaskScheduler.Default);
    }

    [HarmonyPatch(typeof(HighScoreList), nameof(HighScoreList.ReplaceHighScore))]
    private static class ScorePatch
    {
        private static bool Prefix(string song)
        {
            return Plugin.Accounts?.CanSaveScore(song) != false;
        }

        private static void Postfix(string song, int score, float accuracy, int maxCombo, bool cleared, Dictionary<string, int> notes, Modifiers modifierMask, int level, bool __result)
        {
            try { Plugin.Accounts?.Capture(new HighScoreItem(song, score, accuracy, maxCombo, cleared, notes, modifierMask, level), __result); }
            catch (Exception) { CustomSongLoader.Logger?.LogWarning("cannot queue beatnet score"); }
        }
    }
}

internal sealed class BeatNetBpReward
{
    internal readonly string Project;
    internal readonly string Difficulty;
    internal readonly double Before;
    internal readonly double After;
    internal readonly string Owner;
    internal bool Opened { get; set; }

    internal BeatNetBpReward(string project, string difficulty, double before, double after, string owner)
    {
        Project = project;
        Difficulty = difficulty;
        Before = before;
        After = after;
        Owner = owner;
    }
}
