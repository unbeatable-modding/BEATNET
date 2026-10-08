using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Arcade.UI.YourProfile;
using CrossPlatform;
using CrossPlatform.PlayerProfile;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BEATNET;

internal sealed class BeatNetLeaderboard : LeaderboardProviderBase
{
    private readonly LeaderboardProviderBase native;
    internal static readonly Dictionary<string, JObject> Profiles = new();
    internal BeatNetLeaderboard(LeaderboardProviderBase native) => this.native = native;

    private static PlayerScore ConvertScore(JObject row, string path)
    {
        var id = "beatnet:" + (string)row["userId"]!;
        Profiles[id] = row;
        return new PlayerScore
        {
            UserId = new UserId(id, PlatformTypes.Editor), Rank = (int)row["rank"]!,
            Score = new HighScoreItem
            {
                song = path, score = (int)row["score"]!, accuracy = (float)row["accuracy"]!,
                maxCombo = (int)row["maxCombo"]!, cleared = (int)row["cleared"]! != 0,
                isNoMiss = (int)row["noMiss"]! != 0, isFullCombo = (int)row["fullCombo"]! != 0, isPerfectFullCombo = (int)row["perfectCombo"]! != 0,
                _notes = new Dictionary<string, int>(),
            },
        };
    }

    public override void GetScores(string songPath, int startRank, int scoresCount, string region, LeaderboardSorting sorting,
        Action<LeaderboardPage?> callback, Action<LeaderboardError, string> errorCallback = null!)
    {
        var accounts = Plugin.Accounts;
        var board = accounts?.Board(songPath);
        if (board == null) { native.GetScores(songPath, startRank, scoresCount, region, sorting, callback, errorCallback); return; }
        board["action"] = "leaderboard";
        board["offset"] = Math.Max(0, startRank);
        board["limit"] = Math.Max(1, Math.Min(25, scoresCount));
        board["sorting"] = (int)sorting;
        board["region"] = ResolveRegion(region);
        accounts!.SyncProfile();
        if (accounts!.User != null) { board["key"] = accounts.Key; }
        _ = Fetch();
        async Task Fetch()
        {
            try
            {
                var result = await accounts.Request(board).ConfigureAwait(false);
                accounts.Post(() =>
                {
                    var rows = result["items"] as JArray ?? new JArray();
                    if (Profiles.Count > 1024) { Profiles.Clear(); }
                    var scores = new List<PlayerScore>();
                    foreach (var row in rows) { scores.Add(ConvertScore((JObject)row, songPath)); }
                    if (scores.Count == 0) { errorCallback?.Invoke(LeaderboardError.NoEntries, sorting == LeaderboardSorting.Friends ? "BEATNET accounts have no friend list" : "No BEATNET scores yet"); return; }
                    var offset = (int?)result["offset"] ?? startRank;
                    callback(new LeaderboardPage { LeaderboardId = songPath, StartRank = offset, EndRank = offset + scores.Count - 1, Leaderboard = scores.ToArray() });
                });
            }
            catch (Exception error)
            {
                accounts.Post(() => errorCallback?.Invoke(LeaderboardError.CantConnect, error.Message));
            }
        }
    }

    public override void GetScore(string songPath, UserId userId, string region, Action<PlayerScore?> callback,
        Action<LeaderboardError, string> errorCallback = null!)
    {
        var accounts = Plugin.Accounts;
        var board = accounts?.Board(songPath);
        if (board == null) { native.GetScore(songPath, userId, region, callback, errorCallback); return; }
        if (accounts!.User == null) { errorCallback?.Invoke(LeaderboardError.NoEntries, "Log in to see your BEATNET score"); return; }
        var owner = accounts.UserId;
        var key = accounts.Key;
        board["action"] = "leaderboard";
        board["key"] = key;
        board["limit"] = 1;
        board["region"] = ResolveRegion(region);
        accounts.SyncProfile();
        _ = Fetch();
        async Task Fetch()
        {
            try
            {
                var result = await accounts.Request(board).ConfigureAwait(false);
                accounts.Post(() =>
                {
                    if (accounts.UserId != owner || accounts.Key != key) { callback(null); return; }
                    if (result["own"] is JObject own) { callback(ConvertScore(own, songPath)); }
                    else { errorCallback?.Invoke(LeaderboardError.NoEntries, "No BEATNET score yet"); }
                });
            }
            catch (Exception error) { accounts.Post(() => errorCallback?.Invoke(LeaderboardError.CantConnect, error.Message)); }
        }
    }

    public override void SetScore(HighScoreItem score, Action<bool, string> callback, Action<LeaderboardError, string> errorCallback = null!)
    {
        if (Plugin.Accounts?.CanSaveScore(score.song) == false)
        {
            callback?.Invoke(false, "BEATNET score discarded / beatmap updated");
            return;
        }
        if (Plugin.Accounts?.Board(score.song) == null) { native.SetScore(score, callback, errorCallback); return; }
        Plugin.Accounts.Capture(score);
        callback?.Invoke(true, "BEATNET score queued");
    }

    private static string ResolveRegion(string region)
    {
        if (string.IsNullOrEmpty(region) || region.Equals("global", StringComparison.OrdinalIgnoreCase)) { return "global"; }
        if (region.Equals("regional", StringComparison.OrdinalIgnoreCase) || region.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return PlatformManager.Platform?.Region?.ToLowerInvariant() ?? string.Empty;
        }
        return region.ToLowerInvariant();
    }

    public override void GetPlayerStarRankings(bool local, int startRank, int scoresCount, LeaderboardSorting sorting,
        Action<LeaderboardPage?> callback, Action<LeaderboardError, string> errorCallback = null!) => native.GetPlayerStarRankings(local, startRank, scoresCount, sorting, callback, errorCallback);

    public override void GetPlayerStarRank(UserId? userId, Action<int> localCallback, Action<int> globalCallback,
        Action<LeaderboardError, string> errorCallback = null!) => native.GetPlayerStarRank(userId, localCallback, globalCallback, errorCallback);

    [HarmonyPatch(typeof(PlatformManager), "get_Leaderboard")]
    private static class ProviderPatch
    {
        private static void Postfix(ref LeaderboardProviderBase __result)
        {
            if (__result != null && __result is not BeatNetLeaderboard) { __result = new BeatNetLeaderboard(__result); }
        }
    }
}

internal sealed class BeatNetProfiles : PlayerProfileProviderBase
{
    private readonly PlayerProfileProviderBase native;
    private static readonly Dictionary<string, Texture2D> Images = new();
    private static readonly Dictionary<string, Task<byte[]>> Pending = new();
    private static Texture2D? placeholder;
    internal BeatNetProfiles(PlayerProfileProviderBase native) => this.native = native;

    public override void GetPlayerProfile(UserId? userId, Action<YourProfileDisplay.ProfileData> callback)
    {
        if (userId == null || userId.Value.PlatformId?.StartsWith("beatnet:", StringComparison.Ordinal) != true)
        {
            native.GetPlayerProfile(userId, data => { BeatNetProfile.Capture(data); callback?.Invoke(data); });
            return;
        }
        if (!BeatNetLeaderboard.Profiles.TryGetValue(userId.Value.PlatformId, out var row)) { callback(null!); return; }
        if (placeholder == null)
        {
            placeholder = new Texture2D(2, 2);
            placeholder.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray });
            placeholder.Apply();
        }
        var metadata = row["profile"] as JObject ?? new JObject();
        var profile = new YourProfileDisplay.ProfileData
        {
            playerId = userId.Value, playerName = (string)row["username"]!, playerImage = placeholder,
            playerRegion = (string?)metadata["region"] ?? string.Empty,
            topTitle = (string?)metadata["topTitle"] ?? string.Empty,
            middleTitle = (string?)metadata["middleTitle"] ?? string.Empty,
            bottomTitle = (string?)metadata["bottomTitle"] ?? string.Empty,
            badgeTitle = (string?)metadata["badgeTitle"] ?? string.Empty,
            playerAccuracy = (float?)metadata["playerAccuracy"] ?? 0f,
            playerProgression = (float?)metadata["playerProgression"] ?? 0f,
            playerRank = (float?)metadata["playerRank"] ?? 0f,
        };
        var avatar = (string?)row["avatar"] ?? string.Empty;
        if (Images.TryGetValue(avatar, out var texture)) { profile.playerImage = texture; }
        callback(profile);
        if (profile.playerImage != placeholder || !System.Text.RegularExpressions.Regex.IsMatch(avatar, @"^https://avatars\.(?:(?:akamai|fastly)\.)?steamstatic\.com/[0-9a-f]{40}(?:_(?:full|medium))?\.jpg$")) { return; }
        var accounts = Plugin.Accounts;
        if (accounts == null) { return; }
        if (!Pending.TryGetValue(avatar, out var task))
        {
            task = DownloadAvatar(avatar);
            Pending[avatar] = task;
        }
        _ = Complete();
        async Task Complete()
        {
            try
            {
                var bytes = await task.ConfigureAwait(false);
                accounts.Post(() =>
                {
                    Pending.Remove(avatar);
                    if (!Images.TryGetValue(avatar, out var image))
                    {
                        image = new Texture2D(2, 2);
                        if (!image.LoadImage(bytes)) { UnityEngine.Object.Destroy(image); return; }
                        Images[avatar] = image;
                        if (Images.Count > 128)
                        {
                            foreach (var old in Images.Values) { if (old != image) { UnityEngine.Object.Destroy(old); } }
                            Images.Clear();
                            Images[avatar] = image;
                        }
                    }
                    profile.playerImage = image;
                    callback(profile);
                });
            }
            catch (Exception)
            {
                accounts.Post(() => Pending.Remove(avatar));
            }
        }
    }

    private static async Task<byte[]> DownloadAvatar(string url)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var bytes = new System.IO.MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > 262144) { throw new System.IO.InvalidDataException("Avatar is too large"); }
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }

    public override void UpdatePlayerProfile(YourProfileDisplay.ProfileData profile, List<PlayerStats.StatInfos> statsData,
        Action<bool, string> callback)
    {
        BeatNetProfile.Capture(profile);
        native.UpdatePlayerProfile(profile, statsData, callback);
    }

    [HarmonyPatch(typeof(PlatformManager), "get_PlayerProfile")]
    private static class ProfilePatch
    {
        private static void Postfix(ref PlayerProfileProviderBase __result)
        {
            if (__result != null && __result is not BeatNetProfiles) { __result = new BeatNetProfiles(__result); }
        }
    }

    [HarmonyPatch(typeof(YourProfileDisplay), nameof(YourProfileDisplay.Fill))]
    private static class NamePatch
    {
        private sealed class Style
        {
            internal bool RichText;
            internal bool Backplate;
        }

        private static readonly ConditionalWeakTable<YourProfileDisplay, Style> Styles = new();

        private static void Prefix(YourProfileDisplay __instance, YourProfileDisplay.ProfileData data,
            TextMeshProUGUI ___playerNameText, Image ___middleTitleBackplate)
        {
            if (data == null) { return; }
            if (data.playerId.PlatformId?.StartsWith("beatnet:", StringComparison.Ordinal) == true)
            {
                if (!Styles.TryGetValue(__instance, out _))
                {
                    Styles.Add(__instance, new Style { RichText = ___playerNameText.richText, Backplate = ___middleTitleBackplate.enabled });
                }
                ___playerNameText.richText = false;
                ___middleTitleBackplate.enabled = !string.IsNullOrEmpty(data.middleTitle);
            }
            else if (Styles.TryGetValue(__instance, out var style))
            {
                ___playerNameText.richText = style.RichText;
                ___middleTitleBackplate.enabled = style.Backplate;
                Styles.Remove(__instance);
            }
        }
    }
}
