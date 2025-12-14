using System;
using System.Linq;
using BetterErProspecting.Config;
using BetterErProspecting.Item;
using ConfigLib;
using HarmonyLib;
using Vintagestory.API.Common;

namespace BetterErProspecting;

// I swear I won't change modsystem name anymore
public class BetterErProspect : ModSystem {
	public static ILogger Logger { get; private set; }
	public static ICoreAPI Api { get; private set; }
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
		harmony?.UnpatchAll(Mod.Info.ModID);

		if (ModConfig.Instance.NewDensityMode) {
			// Force linear for new mode because it calculates that linearly anyway
			harmony?.PatchCategory(nameof(PatchCategory.PptTracking));
		}

		if (ModConfig.Instance.LinearDensityScaling || ModConfig.Instance.NewDensityMode) {
			harmony?.PatchCategory(nameof(PatchCategory.LinearDensity));
		}
	}

	public enum PatchCategory {
		LinearDensity,
		PptTracking
	}
}

