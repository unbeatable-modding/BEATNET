using System;
using System.Collections.Generic;
using System.Reflection;
using Arcade.Progression;
using Arcade.UI.YourProfile;
using Arcade.Utils;
using CrossPlatform;
using CrossPlatform.Platforms;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace BEATNET;

internal static class BeatNetProfile
{
    private static readonly FieldInfo Container = AccessTools.Field(typeof(MainProgressionContainer), "_instance");
    private static readonly FieldInfo Accuracy = AccessTools.Field(typeof(PlayerStatsHelper), "_avgAccuracy");
    private static readonly FieldInfo Progression = AccessTools.Field(typeof(PlayerStatsHelper), "_challengeProgress");
    private static readonly FieldInfo Rank = AccessTools.Field(typeof(PlayerStatsHelper), "_playerRank");
    private static JObject? captured;
    private static readonly Dictionary<string, float> Numbers = new();

    internal static void Capture(YourProfileDisplay.ProfileData data)
    {
        if (data == null || PlatformManager.Platform == null || data.playerId != PlatformManager.Platform.GetLocalUserId()) { return; }
        var next = new JObject
        {
            ["topTitle"] = data.topTitle ?? string.Empty, ["middleTitle"] = data.middleTitle ?? string.Empty,
            ["bottomTitle"] = data.bottomTitle ?? string.Empty, ["badgeTitle"] = data.badgeTitle ?? string.Empty,
            ["playerAccuracy"] = data.playerAccuracy, ["playerProgression"] = data.playerProgression,
            ["playerRank"] = data.playerRank,
            ["region"] = data.playerRegion ?? string.Empty,
        };
        if (JToken.DeepEquals(captured, next)) { return; }
        captured = next;
        Plugin.Accounts?.ProfileChanged();
    }

    internal static JObject? Read()
    {
        var platform = PlatformManager.Platform;
        if (platform == null) { return null; }
        var result = captured == null ? new JObject() : (JObject)captured.DeepClone();
        result.Remove("region");
        var config = (Container?.GetValue(null) as MainProgressionContainer)?.YourProfileConfig;
        if (config != null)
        {
            result["topTitle"] = config.TopTitle ?? string.Empty;
            result["middleTitle"] = config.MiddleTitle ?? string.Empty;
            result["bottomTitle"] = config.BottomTitle ?? string.Empty;
            result["badgeTitle"] = config.BadgeTitle ?? string.Empty;
        }
        SetNumber(result, "playerAccuracy", Accuracy);
        SetNumber(result, "playerProgression", Progression);
        SetNumber(result, "playerRank", Rank);
        var region = platform.Region?.ToLowerInvariant() ?? string.Empty;
        if (System.Text.RegularExpressions.Regex.IsMatch(region, "^[a-z0-9_-]{2,16}$")
            && region != "global" && region != "regional" && region != "local") { result["region"] = region; }
        return result.Count == 0 ? null : result;
    }

    private static void SetNumber(JObject result, string name, FieldInfo field)
    {
        if (field?.GetValue(null) is float value && !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f) { result[name] = value; }
    }

    [HarmonyPatch(typeof(YourProfileDisplay.ProfileData), nameof(YourProfileDisplay.ProfileData.GetLocalProfile))]
    private static class LocalPatch
    {
        private static void Postfix(YourProfileDisplay.ProfileData __result) => Capture(__result);
    }

    [HarmonyPatch]
    private static class TitlePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "TopTitle", "MiddleTitle", "BottomTitle", "BadgeTitle" })
            {
                yield return AccessTools.PropertySetter(typeof(YourProfileConfig), name);
            }
        }

        private static void Prefix(YourProfileConfig __instance, MethodBase __originalMethod, out string? __state)
        {
            __state = (string?)AccessTools.Property(typeof(YourProfileConfig), __originalMethod.Name.Substring(4)).GetValue(__instance);
        }

        private static void Postfix(YourProfileConfig __instance, string value, string? __state)
        {
            if (value != __state && ReferenceEquals(__instance, (Container?.GetValue(null) as MainProgressionContainer)?.YourProfileConfig))
            {
                Plugin.Accounts?.ProfileChanged();
            }
        }
    }

    [HarmonyPatch]
    private static class StatsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlayerStatsHelper), nameof(PlayerStatsHelper.GetPlayerAvgAccuracy));
            yield return AccessTools.Method(typeof(PlayerStatsHelper), nameof(PlayerStatsHelper.GetChallengeProgress));
            yield return AccessTools.Method(typeof(PlayerStatsHelper), nameof(PlayerStatsHelper.GetPlayerRank));
        }

        private static void Postfix(MethodBase __originalMethod, float __result)
        {
            if (Numbers.TryGetValue(__originalMethod.Name, out var previous) && previous.Equals(__result)) { return; }
            Numbers[__originalMethod.Name] = __result;
            Plugin.Accounts?.ProfileChanged();
        }
    }

    [HarmonyPatch(typeof(MainProgressionContainer), nameof(MainProgressionContainer.LoadSave))]
    private static class LoadPatch
    {
        private static void Postfix()
        {
            captured = null;
            Numbers.Clear();
            Plugin.Accounts?.ProfileChanged();
        }
    }

    [HarmonyPatch]
    private static class RegionPatch
    {
        private static MethodBase TargetMethod() => AccessTools.PropertySetter(typeof(PlatformBase), nameof(PlatformBase.Region));

        private static void Prefix(PlatformBase __instance, out string __state) => __state = __instance.Region;

        private static void Postfix(PlatformBase __instance, string value, string __state)
        {
            if (value == __state || !ReferenceEquals(__instance, PlatformManager.Platform)) { return; }
            var accounts = Plugin.Accounts;
            accounts?.Post(accounts.ProfileChanged);
        }
    }
}
