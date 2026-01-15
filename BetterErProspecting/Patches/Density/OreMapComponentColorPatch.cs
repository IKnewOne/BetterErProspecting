using System.Collections.Generic;
using System.Reflection.Emit;
using BetterErProspecting.Tracking;
using HarmonyLib;
using Vintagestory.GameContent;

namespace BetterErProspecting.Patches;

[HarmonyPatch(typeof(OreMapComponent), MethodType.Constructor, typeof(int), typeof(PropickReading), typeof(OreMapLayer), typeof(Vintagestory.API.Client.ICoreClientAPI), typeof(string))]
[HarmonyPatchCategory(nameof(BetterErProspect.PatchCategory.NewDensity))]
public class OreMapComponentColorPatch {
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
		// We're looking for the pattern:
		// color = GuiStyle.DamageColorGradient[(int) Math.Min(99.0, reading.OreReadings[filterByOreCode].TotalFactor * 150.0)];
		// which in IL looks like:
		// ldfld OreReading::TotalFactor
		// ldc.r8 150
		// mul
		//
		// We want to replace ldfld TotalFactor with our helper call

		var matcher = new CodeMatcher(instructions);

		var totalFactorField = AccessTools.Field(typeof(OreReading), nameof(OreReading.TotalFactor));
		var helperMethod = AccessTools.Method(typeof(OreMapComponentColorPatch), nameof(GetAdjustedFactorFromReading));

		matcher.Start()
			.MatchStartForward(
				new CodeMatch(OpCodes.Ldfld, totalFactorField),
				new CodeMatch(OpCodes.Ldc_R8, 150.0)
			);

		if (matcher.IsInvalid) {
			BetterErProspect.Logger?.Error("[OreMapComponentColorPatch] Failed to find TotalFactor * 150.0 pattern");
			return instructions;
		}

		// Replace: ldfld TotalFactor -> call GetAdjustedFactorFromReading
		matcher.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, helperMethod));

		return matcher.InstructionEnumeration();
	}

	private static double GetAdjustedFactorFromReading(OreReading reading) {
		var pptTracker = BetterErProspect.Api.ModLoader.GetModSystem<PptTracker>();
		return pptTracker?.GetAdjustedFactor(reading) ?? reading.TotalFactor;
	}
}
