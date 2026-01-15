using System.Collections.Generic;
using System.Reflection.Emit;
using BetterErProspecting.Item;
using HarmonyLib;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace BetterErProspecting.Patches;

[HarmonyPatch(typeof(PropickReading), "ToHumanReadable")]
[HarmonyPatchCategory(nameof(BetterErProspect.PatchCategory.Always))]
public class ReadingToHumanReadableStone {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        // We want to insert before source.Add(...) at line 70:
        //   source.Add(new KeyValuePair<double, string>(oreReading2.TotalFactor, str2));
        //
        // IL pattern for source.Add:
        //   ldloc.s      source
        //   ldloc.s      oreReading2
        //   ldfld        OreReading::TotalFactor
        //   ldloc.s      str2
        //   newobj       KeyValuePair<double, string>
        //   callvirt     List.Add
        //
        // We insert before this: str2 = ProcessStoneReading(languageCode, pageCodes, oreReading1, str2)

        var matcher = new CodeMatcher(instructions);

        var totalFactorField = AccessTools.Field(typeof(OreReading), nameof(OreReading.TotalFactor));
        var kvpCtor = AccessTools.Constructor(typeof(KeyValuePair<double, string>), new[] { typeof(double), typeof(string) });
        var listAddMethod = AccessTools.Method(typeof(List<KeyValuePair<double, string>>), nameof(List<KeyValuePair<double, string>>.Add));
        var helperMethod = AccessTools.Method(typeof(ReadingToHumanReadableStone), nameof(ProcessStoneReading));

        // Find the pattern for source.Add(new KeyValuePair<double, string>(oreReading2.TotalFactor, str2))
        // IL pattern at line 70:
        // IL_016a: ldloc.0      // source
        // IL_016b: ldloc.s      oreReading2
        // IL_016d: ldfld        OreReading::TotalFactor
        // IL_0172: ldloc.s      str2
        // IL_0174: newobj       KeyValuePair<double, string>
        // IL_0179: callvirt     List.Add
        var getKeyMethod = AccessTools.PropertyGetter(typeof(KeyValuePair<string, OreReading>), nameof(KeyValuePair<string, OreReading>.Key));

        // First find oreReading1 local by searching for call to get_Key
        matcher.Start()
            .MatchStartForward(
                new CodeMatch(OpCodes.Ldloca_S),
                new CodeMatch(OpCodes.Call, getKeyMethod)
            );

        if (matcher.IsInvalid) {
            BetterErProspect.Logger?.Error("[PropickReadingRockDisplayPatch] Failed to find oreReading1 local");
            return instructions;
        }

        var oreReading1LocalOperand = matcher.Operand;

        // Now find the source.Add pattern: ldloc.0 -> ldloc.s -> ldfld TotalFactor
        matcher.Start()
            .MatchStartForward(
                new CodeMatch(i => i.opcode == OpCodes.Ldloc_0 || i.opcode == OpCodes.Ldloc || i.opcode == OpCodes.Ldloc_S),
                new CodeMatch(i => i.opcode == OpCodes.Ldloc_S || i.opcode == OpCodes.Ldloc),
                new CodeMatch(OpCodes.Ldfld, totalFactorField),
                new CodeMatch(i => i.opcode == OpCodes.Ldloc_S || i.opcode == OpCodes.Ldloc),
                new CodeMatch(OpCodes.Newobj, kvpCtor),
                new CodeMatch(OpCodes.Callvirt, listAddMethod)
            );

        if (matcher.IsInvalid) {
            BetterErProspect.Logger?.Error("[PropickReadingRockDisplayPatch] Failed to find source.Add pattern");
            return instructions;
        }

        // Get str2 local from the ldloc.s before newobj (position 3 in pattern)
        matcher.Advance(3);
        var str2LocalOperand = matcher.Operand;

        // Go back to start of pattern (ldloc.0 source) to insert before it
        matcher.Advance(-3);

        // Insert: str2 = ProcessStoneReading(languageCode, oreReading1, str2)
        matcher.Insert(
            new CodeInstruction(OpCodes.Ldarg_1), // languageCode
            new CodeInstruction(OpCodes.Ldloca_S, oreReading1LocalOperand),
            new CodeInstruction(OpCodes.Ldobj, typeof(KeyValuePair<string, OreReading>)),
            new CodeInstruction(OpCodes.Ldloc_S, str2LocalOperand),
            new CodeInstruction(OpCodes.Call, helperMethod),
            new CodeInstruction(OpCodes.Stloc_S, str2LocalOperand)
        );

        // Second injection: after str4 = String.Format(...) in the "Miniscule amounts" section
        // IL pattern:
        // IL_02c1: call         string String::Format(string, object, object)
        // IL_02c6: stloc.s      str4
        // IL_02c8: ldloc.s      stringBuilder2
        // IL_02ca: ldloc.s      str4
        // IL_02cc: callvirt     StringBuilder::Append(string)
        var stringFormatMethod = AccessTools.Method(typeof(string), nameof(string.Format), new[] { typeof(string), typeof(object), typeof(object) });
        var stringBuilderAppendMethod = AccessTools.Method(typeof(System.Text.StringBuilder), nameof(System.Text.StringBuilder.Append), new[] { typeof(string) });
        var traceHelperMethod = AccessTools.Method(typeof(ReadingToHumanReadableStone), nameof(ProcessTraceReading));

        matcher.Start()
            .MatchStartForward(
                new CodeMatch(OpCodes.Call, stringFormatMethod),
                new CodeMatch(i => i.opcode == OpCodes.Stloc_S || i.opcode == OpCodes.Stloc),
                new CodeMatch(i => i.opcode == OpCodes.Ldloc_S || i.opcode == OpCodes.Ldloc),
                new CodeMatch(i => i.opcode == OpCodes.Ldloc_S || i.opcode == OpCodes.Ldloc),
                new CodeMatch(OpCodes.Callvirt, stringBuilderAppendMethod)
            );

        if (matcher.IsInvalid) {
            BetterErProspect.Logger?.Error("[PropickReadingRockDisplayPatch] Failed to find str4 assignment pattern");
            return matcher.InstructionEnumeration();
        }

        // Get str4 local (position 1 - the stloc.s after String.Format)
        matcher.Advance(1);
        var str4LocalOperand = matcher.Operand;

        // Get key local (position 3 - the ldloc.s str4 before Append)
        // Actually we need the 'key' variable - search for it in the pattern
        // Looking at IL: ldloc.s key is at IL_02b0, before String.Concat
        // We need to find it separately - search for "ore-" string concatenation
        var stringConcatMethod = AccessTools.Method(typeof(string), nameof(string.Concat), new[] { typeof(string), typeof(string) });

        var keySearchMatcher = new CodeMatcher(instructions);
        keySearchMatcher.Start()
            .MatchStartForward(
                new CodeMatch(OpCodes.Ldstr, "ore-"),
                new CodeMatch(i => i.opcode == OpCodes.Ldloc_S || i.opcode == OpCodes.Ldloc),
                new CodeMatch(OpCodes.Call, stringConcatMethod)
            );

        if (keySearchMatcher.IsInvalid) {
            BetterErProspect.Logger?.Error("[PropickReadingRockDisplayPatch] Failed to find key local");
            return matcher.InstructionEnumeration();
        }

        keySearchMatcher.Advance(1);
        var keyLocalOperand = keySearchMatcher.Operand;

        // Move past stloc.s str4 to insert after it
        matcher.Advance(1);

        // Insert: str4 = ProcessTraceReading(languageCode, key, str4)
        matcher.Insert(
            new CodeInstruction(OpCodes.Ldarg_1), // languageCode
            new CodeInstruction(OpCodes.Ldloc_S, keyLocalOperand), // key
            new CodeInstruction(OpCodes.Ldloc_S, str4LocalOperand), // str4
            new CodeInstruction(OpCodes.Call, traceHelperMethod),
            new CodeInstruction(OpCodes.Stloc_S, str4LocalOperand)
        );

        return matcher.InstructionEnumeration();
    }

    private static string ProcessTraceReading(string languageCode, string key, string currentValue) {
        if (!key.StartsWith("rock-"))
            return currentValue;

        return ItemBetterErProspectingPick.getHandbookLinkOrName(BetterErProspect.Api.World, languageCode, key);
    }

    private static string ProcessStoneReading(string languageCode, KeyValuePair<string, OreReading> oreReading1, string currentValue) {
        if (!oreReading1.Key.StartsWith("rock-"))
            return currentValue;

        var reading = oreReading1.Value;

        var oreName = Lang.GetL(languageCode, reading.DepositCode);
        var percent = reading.TotalFactor.ToString("0.##%");

        return $"{percent} {oreName}";
    }
}
