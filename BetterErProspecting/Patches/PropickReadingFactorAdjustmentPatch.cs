using BetterErProspecting.Tracking;
using HarmonyLib;
using Vintagestory.GameContent;

namespace BetterErProspecting.Patches;

[HarmonyPatch(typeof(PropickReading), "ToHumanReadable")]
[HarmonyPatchCategory(nameof(BetterErProspect.PatchCategory.PptTracking))]
public class PropickReadingFactorAdjustmentPatch {
	static void Prefix(PropickReading __instance) {
		if (__instance?.OreReadings == null || BetterErProspect.Api == null)
			return;

		var pptTracker = BetterErProspect.Api.ModLoader.GetModSystem<PptTracker>();
		if (pptTracker == null)
			return;

		foreach (var (oreCode, reading) in __instance.OreReadings) {
			if (reading == null)
				continue;
			reading.DepositCode = oreCode;
			pptTracker.AdjustReadingFactor(reading);
		}
	}
}
