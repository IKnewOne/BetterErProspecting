using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace BetterErProspecting.Patches.Stone;

[HarmonyPatch(typeof(OreMapLayer), nameof(OreMapLayer.RebuildMapComponents))]
[HarmonyPatchCategory(nameof(BetterErProspect.PatchCategory.Always))]
// Always because we want to be able to manipulate and hide rock reading components when they get disabled after being enabled and created
public class RebuildMapComponentsPatch {
    private static readonly MethodInfo GetTotalFactorMethod = AccessTools.Method(typeof(PropickReading), "GetTotalFactor");

    private static double GetTotalFactor(PropickReading reading, string code) {
        return (double)GetTotalFactorMethod.Invoke(reading, new object[] { code });
    }

    static bool Prefix(OreMapLayer __instance) {
        var mapSink = (IWorldMapManager)AccessTools.Field(typeof(OreMapLayer), "mapSink").GetValue(__instance);
        if (!mapSink.IsOpened)
            return false;

        var api = (ICoreClientAPI)AccessTools.Field(typeof(MapLayer), "api").GetValue(__instance);
        var filterByOreCode = (string)AccessTools.Field(typeof(OreMapLayer), "filterByOreCode").GetValue(__instance);
        var wayPointComponents = AccessTools.Field(typeof(OreMapLayer), "wayPointComponents").GetValue(__instance) as System.Collections.Generic.List<MapComponent>;
        var tmpWayPointComponents = AccessTools.Field(typeof(OreMapLayer), "tmpWayPointComponents").GetValue(__instance) as System.Collections.Generic.List<MapComponent>;

        foreach (var comp in tmpWayPointComponents)
            wayPointComponents.Remove(comp);

        foreach (var comp in wayPointComponents)
            comp.Dispose();

        wayPointComponents.Clear();

        for (int index = 0; index < __instance.ownPropickReadings.Count; ++index) {
            PropickReading reading = __instance.ownPropickReadings[index];

            bool shouldShow = ShouldShowReading(reading, filterByOreCode);
            if (shouldShow) {
                wayPointComponents.Add(new OreMapComponent(index, reading, __instance, api, filterByOreCode)); // We postfix this constructor
            }
        }

        wayPointComponents.AddRange(tmpWayPointComponents);

        return false;
    }

    private static bool ShouldShowReading(PropickReading reading, string filterByOreCode) {
        // We don't create readings with mixed rock and ore data, so rock readings are always exclusively rock-
        if (!BetterErProspect.Config.StoneSearchCreatesReadings && reading.OreReadings.Keys.All(k => k.StartsWith("rock-"))) {
            return false;
        }

        return filterByOreCode switch {
            // null = show everything
            null => true,
            // Special prefix filters
            "ore-" => reading.OreReadings.Keys.Any(k => !k.StartsWith("rock-") && GetTotalFactor(reading, k) > PropickReading.MentionThreshold),
            "rock-" => reading.OreReadings.Keys.Any(k => k.StartsWith("rock-") && GetTotalFactor(reading, k) > PropickReading.MentionThreshold),
            // Normal filter - exact key match
            _ => GetTotalFactor(reading, filterByOreCode) > PropickReading.MentionThreshold
        };
    }
}
