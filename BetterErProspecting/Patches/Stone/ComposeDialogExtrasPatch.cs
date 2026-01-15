using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace BetterErProspecting.Patches.Stone;

[HarmonyPatch(typeof(OreMapLayer), nameof(OreMapLayer.ComposeDialogExtras))]
[HarmonyPatchCategory(nameof(BetterErProspect.PatchCategory.Always))]
// Always because we need to handle previously created stone readings. Otherwise they will have ore-rock-{} format and not have a proper dropdown option
public class ComposeDialogExtrasPatch {
    static bool Prefix(OreMapLayer __instance, GuiDialogWorldMap guiDialogWorldMap, GuiComposer compo) {
        var stoneReadingsEnabled = BetterErProspect.Config.StoneSearchCreatesReadings;

        var capi = (ICoreClientAPI)AccessTools.Field(typeof(OreMapLayer), "capi").GetValue(__instance);
        var filterByOreCode = (string)AccessTools.Field(typeof(OreMapLayer), "filterByOreCode").GetValue(__instance);

        string key = "worldmap-layer-" + __instance.LayerGroupCode;
        HashSet<string> orecodes = new HashSet<string>();
        foreach (PropickReading ownPropickReading in __instance.ownPropickReadings) {
            foreach (KeyValuePair<string, OreReading> oreReading in ownPropickReading.OreReadings)
                orecodes.Add(oreReading.Key);
        }

        var valuesList = new List<string> { null, };
        var namesList = new List<string> { Lang.Get("worldmap-ores-everything") };
        if (stoneReadingsEnabled) {
            valuesList.AddRange(["ore-", "rock-"]);
            namesList.AddRange([Lang.Get("bettererprospecting:worldmap-ores-only"), Lang.Get("bettererprospecting:worldmap-rocks-only")]);
        }

        // Build pairs of (value, localizedName) for sorting
        var oreEntries = new List<(string value, string name)>();
        foreach (var code in orecodes) {
            if (code.StartsWith("rock-")) {
                // Crucial continue
                if (!stoneReadingsEnabled) continue;
                oreEntries.Add((code, $"[{Lang.Get("bettererprospecting:R")}]{Lang.Get(code)}"));
            } else {
                oreEntries.Add((code, Lang.Get("ore-" + code)));
            }
        }

        // Sort by localized name, with rocks last
        foreach (var (value, name) in oreEntries.OrderBy(e => e.value.StartsWith("rock-")).ThenBy(e => e.name, StringComparer.CurrentCulture)) {
            valuesList.Add(value);
            namesList.Add(name);
        }

        string[] values = valuesList.ToArray();
        string[] names = namesList.ToArray();

        ElementBounds dlgBounds = ElementStdBounds.AutosizedMainDialog
            .WithFixedPosition(
                (compo.Bounds.renderX + compo.Bounds.OuterWidth) / RuntimeEnv.GUIScale + 10.0,
                compo.Bounds.renderY / RuntimeEnv.GUIScale)
            .WithAlignment(EnumDialogArea.None);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        var filterByOreCodeField = AccessTools.Field(typeof(OreMapLayer), "filterByOreCode");

        void onSelectionChanged(string code, bool selected) {
            filterByOreCodeField.SetValue(__instance, code);
            __instance.RebuildMapComponents();
        }

        guiDialogWorldMap.Composers[key] =
            capi.Gui
                .CreateCompo(key, dlgBounds)
                .AddShadedDialogBG(bgBounds, false)
                .AddDialogTitleBar(Lang.Get("maplayer-prospecting"), () => { guiDialogWorldMap.Composers[key].Enabled = false; })
                .BeginChildElements(bgBounds)
                .AddDropDown(values, names, Math.Max(0, values.IndexOf(filterByOreCode)), onSelectionChanged, ElementBounds.Fixed(0, 30, 160, 35))
                .EndChildElements()
                .Compose()
            ;

        guiDialogWorldMap.Composers[key].Enabled = false;

        return false;
    }
}
