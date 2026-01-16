using BetterErProspecting.Tracking;
using HarmonyLib;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace BetterErProspecting.Patches;

[HarmonyPatch(typeof(PropickReading), "ToHumanReadable")]
[HarmonyPatchCategory(nameof(BetterErProspect.PatchCategory.NewDensity))]
public class ReadingToHumanReadableOreAdjustFactor {
	static void Prefix(PropickReading __instance) {
		if (__instance?.OreReadings == null || BetterErProspect.Api == null)
			return;

		var pptTracker = BetterErProspect.Api.ModLoader.GetModSystem<PptTracker>();
		pptTracker.AdjustFactor(__instance.OreReadings);
	}
}
