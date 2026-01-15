using System.Collections.Generic;
using System.Reflection.Emit;
using BetterErProspecting.Tracking;
using HarmonyLib;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace BetterErProspecting.Patches;

[HarmonyPatch(typeof(PropickReading), "HighestReading", MethodType.Getter)]
[HarmonyPatchCategory(nameof(BetterErProspect.PatchCategory.NewDensity))]
public class PropickReadingHighestReadingPatch {
	static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
		// We're looking for the pattern:
		// a = GameMath.Max(a, oreReading.Value.TotalFactor);
		// which in IL looks like:
		// ldloca.s (KeyValuePair local)
		// call KeyValuePair.get_Value
		// ldfld OreReading::TotalFactor
		// call GameMath.Max
		//
		// We want to replace it with:
		// ldloca.s (KeyValuePair local)
		// ldobj KeyValuePair
		// call GetAdjustedFactorFromKvp
		// call GameMath.Max

		var matcher = new CodeMatcher(instructions);

		var getValueMethod = AccessTools.PropertyGetter(typeof(KeyValuePair<string, OreReading>), nameof(KeyValuePair<string, OreReading>.Value));
		var totalFactorField = AccessTools.Field(typeof(OreReading), nameof(OreReading.TotalFactor));
		var helperMethod = AccessTools.Method(typeof(PropickReadingHighestReadingPatch), nameof(GetAdjustedFactorFromKvp));

		matcher.Start()
			.MatchStartForward(
				new CodeMatch(OpCodes.Call, getValueMethod),
				new CodeMatch(OpCodes.Ldfld, totalFactorField)
			);

		if (matcher.IsInvalid) {
			BetterErProspect.Logger?.Error("[PropickReadingHighestReadingPatch] Failed to find Value.TotalFactor pattern");
			return instructions;
		}

		// Replace: call get_Value -> ldobj KeyValuePair
		matcher.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Ldobj, typeof(KeyValuePair<string, OreReading>)));

		// Replace: ldfld TotalFactor -> call GetAdjustedFactorFromKvp
		matcher.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, helperMethod));

		return matcher.InstructionEnumeration();
	}

	private static double GetAdjustedFactorFromKvp(KeyValuePair<string, OreReading> kvp) {
		kvp.Value.DepositCode = kvp.Key;

		var pptTracker = BetterErProspect.Api.ModLoader.GetModSystem<PptTracker>();
		return pptTracker.GetAdjustedFactor(kvp.Value);
	}
}
