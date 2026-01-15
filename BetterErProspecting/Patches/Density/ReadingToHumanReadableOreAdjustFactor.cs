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
		foreach (var (oreCode, reading) in __instance.OreReadings) {
			if (reading == null)
				continue;
			reading.DepositCode = oreCode;
			reading.TotalFactor = pptTracker.GetAdjustedFactor(reading);
		}
	}
}
