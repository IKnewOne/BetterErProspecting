using System.Collections.Generic;
using BetterErProspecting.Tracking;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace BetterErProspecting.Patches.Density;

[HarmonyPatch(typeof(OreMapLayer), nameof(OreMapLayer.OnDataFromServer))]
[HarmonyPatchCategory(nameof(BetterErProspect.PatchCategory.NewDensity))]
public class OreMapLayerOnDataFromServerAdjustFactorPatch {
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    static void Prefix(ref byte[] data) {
        if (data == null || BetterErProspect.Api is not ICoreClientAPI capi)
            return;

        var pptTracker = capi.ModLoader.GetModSystem<PptTracker>();
        if (pptTracker == null)
            return;

        var readings = SerializerUtil.Deserialize<List<PropickReading>>(data);
        if (readings == null)
            return;

        pptTracker.AdjustFactor(readings);
        data = SerializerUtil.Serialize(readings);
    }
}
