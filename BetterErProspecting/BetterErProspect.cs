using System;
using System.Linq;
using System.Reflection;
using BetterErProspecting.Config;
using BetterErProspecting.Item;
using ConfigLib;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace BetterErProspecting;

// I swear I won't change modsystem name anymore
public class BetterErProspect : ModSystem {
	public static ILogger Logger { get; private set; }
	public static ICoreAPI Api { get; private set; }
	public static ModConfig Config => ModConfig.Instance;
	private static Harmony harmony { get; set; }

	public static event Action ReloadTools;

	public override void Start(ICoreAPI api) {
		api.Logger.Debug("[BetterErProspecting] Starting...");
		base.Start(api);

		harmony = new Harmony(Mod.Info.ModID);
		Api = api;
		Logger = Mod.Logger;

		try {
			ModConfig.Instance = api.LoadModConfig<ModConfig>(ModConfig.ConfigName) ?? new ModConfig();
			api.StoreModConfig(ModConfig.Instance, ModConfig.ConfigName);
		} catch (Exception) { ModConfig.Instance = new ModConfig(); }

		if (api.ModLoader.IsModEnabled("configlib")) {
			SubscribeToConfigChange(api);
		}

		PatchUnpatch();
		api.RegisterItemClass("ItemBetterErProspectingPick", typeof(ItemBetterErProspectingPick));
	}



	private void SubscribeToConfigChange(ICoreAPI api) {
		ConfigLibModSystem system = api.ModLoader.GetModSystem<ConfigLibModSystem>();

		system.SettingChanged += (domain, _, setting) => {
			if (domain != "bettererprospecting")
				return;

			setting.AssignSettingValue(ModConfig.Instance);

			string[] settingsToolReload = [nameof(ModConfig.EnableDensityMode), nameof(ModConfig.NewDensityMode), nameof(ModConfig.AddBoreHoleMode), nameof(ModConfig.AddStoneMode), nameof(ModConfig.AddProximityMode)];
			string[] settingsPatch = [nameof(ModConfig.NewDensityMode)];

			if (settingsToolReload.Contains(setting.YamlCode)) {
				ReloadTools?.Invoke();
			}

			if (settingsPatch.Contains(setting.YamlCode)) {
				PatchUnpatch();
			}
		};
	}

	public override void Dispose() {
		harmony?.UnpatchAll(Mod.Info.ModID);
		ModConfig.Instance = null;
		harmony = null;
		Logger = null;
		Api = null;
		base.Dispose();
	}

	private void PatchUnpatch() {
		harmony.UnpatchAll(Mod.Info.ModID);
		harmony.PatchCategory(nameof(PatchCategory.Always));

		if (ModConfig.Instance.NewDensityMode) {
			harmony.PatchCategory(nameof(PatchCategory.NewDensity));
		}

		if (ModConfig.Instance.StoneSearchCreatesReadings) {
			harmony.PatchCategory(nameof(PatchCategory.StoneReadings));
		}

		if (Api.ModLoader.IsModEnabled("prospecttogether")) {
			UnpatchProspectTogether();
			harmony.PatchCategory(nameof(PatchCategory.ProspectTogetherCompat));
		}
	}

	// Maybe some better place ?
	private void UnpatchProspectTogether() {
		var original = AccessTools.Method(typeof(OreMapLayer), nameof(OreMapLayer.OnDataFromServer));
		var ptAssembly = AppDomain.CurrentDomain.GetAssemblies()
			.FirstOrDefault(a => a.GetName().Name == "ProspectTogether");
		if (ptAssembly == null)
			return;

		var patchType = ptAssembly.GetType("ProspectTogether.Client.OreMapLayerPatch");
		var theirPrefix = patchType?.GetMethod("OnDataFromServer", BindingFlags.Static | BindingFlags.NonPublic);
		if (theirPrefix != null) {
			harmony.Unpatch(original, theirPrefix);
		}
	}

	public enum PatchCategory {
		Always,
		NewDensity,
		StoneReadings,
		ProspectTogetherCompat
	}
}

