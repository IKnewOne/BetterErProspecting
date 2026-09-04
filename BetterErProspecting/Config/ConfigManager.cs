using System;
using BetterErProspecting.Patches;
using ConfigLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace BetterErProspecting.Config;

public static class ConfigManager {
    public static event Action ReloadTools;
    private static ICoreAPI Api => BetterErProspect.Api;

    public static void handle() {
        if (Api.ModLoader.IsModEnabled("configlib")) {
            SubscribeToConfigChange();
        }
    }

    private static void SubscribeToConfigChange() {
        ConfigLibModSystem system = Api.ModLoader.GetModSystem<ConfigLibModSystem>();

        system.SettingChanged += (domain, _, setting) => {
            if (domain != "bettererprospecting") return;
            setting.AssignSettingValue(BetterErProspect.Config);

            if (ModConfig.SettingsForceLoad.Contains(setting.YamlCode)) {
                ReloadTools?.Invoke();
            }

            if (ModConfig.SettingsPatch.Contains(setting.YamlCode)) {
                PatchManager.handle();
            }
        };

        if (Api.Side == EnumAppSide.Server || (Api as ICoreClientAPI)?.IsSinglePlayer == true) return;

        // When a client connects to a MP server, it might have outdated values
        system.ConfigsLoaded += () => {
            var config = (ConfigLib.Config)system.GetConfig("bettererprospecting")!;
            ApplyConfigChange(config!);

            // When a client modifies settings from his side in MP, we need to reload ~ SettingChanged doesn't seem to capture him
            // nag maltiez to (?) fix this
            config!.ConfigSaved -= ApplyConfigChange;
            config!.ConfigSaved += ApplyConfigChange;
        };

        void ApplyConfigChange(ConfigLib.Config cfg) {
            cfg.AssignSettingsValues(BetterErProspect.Config);
            PatchManager.handle();
            ReloadTools?.Invoke();
        }
    }
}
