using System;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace BetterErProspecting.Patches.Stone;

[HarmonyPatch(typeof(OreMapComponent), MethodType.Constructor, typeof(int), typeof(PropickReading), typeof(OreMapLayer), typeof(ICoreClientAPI), typeof(string))]
[HarmonyPatchCategory(nameof(BetterErProspect.PatchCategory.StoneReadings))]
public class OreMapComponentCtorPatch {
    static bool Prefix(OreMapComponent __instance, int waypointIndex, PropickReading reading, OreMapLayer wpLayer, ICoreClientAPI capi, string filterByOreCode) {
        // Only handle special prefix filters - let original run for normal cases
        if (filterByOreCode != "ore-" && filterByOreCode != "rock-")
            return true;

        // Set all fields that the original constructor would set (including field initializer defaults)
        AccessTools.Field(typeof(OreMapComponent), "waypointIndex").SetValue(__instance, waypointIndex);
        AccessTools.Field(typeof(OreMapComponent), "reading").SetValue(__instance, reading);
        AccessTools.Field(typeof(OreMapComponent), "oreLayer").SetValue(__instance, wpLayer);
        AccessTools.Field(typeof(OreMapComponent), "viewPos").SetValue(__instance, new Vec2f());
        AccessTools.Field(typeof(OreMapComponent), "mvMat").SetValue(__instance, new Matrixf());

        // Also need to set the base class capi field
        AccessTools.Field(typeof(MapComponent), "capi").SetValue(__instance, capi);

        // Calculate the highest reading for the prefix filter
        double highestFactor = 0;

        foreach (var kvp in reading.OreReadings) {
            bool matches = filterByOreCode == "rock-"
                ? kvp.Key.StartsWith("rock-")
                : !kvp.Key.StartsWith("rock-");

            if (matches && kvp.Value.TotalFactor > highestFactor) {
                highestFactor = kvp.Value.TotalFactor;
            }
        }

        int color = GuiStyle.DamageColorGradient[(int)Math.Min(99.0, highestFactor * 150.0)];

        var colorVec = new Vec4f();
        ColorUtil.ToRGBAVec4f(color, ref colorVec);
        colorVec.W = 1f;
        AccessTools.Field(typeof(OreMapComponent), "color").SetValue(__instance, colorVec);

        return false; // Skip original constructor
    }
}
