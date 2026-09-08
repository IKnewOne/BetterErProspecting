using System;
using BetterErProspecting.Patches;
using ConfigKit;
using ConfigLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using IConfig = ConfigKit.IConfig;

namespace BetterErProspecting.Config;

public static class ConfigManager {
    public static event Action ReloadTools;
    private static ICoreAPI Api => BetterErProspect.Api;

    public static void handle() {
        if (Api.ModLoader.IsModEnabled("configlib")) {
            SubscribeToConfigLib();
        } else if (Api.ModLoader.IsModEnabled("configkit")) {
            // stands down for configlib
            SubscribeToConfigKit();
        } else if (Api.ModLoader.IsModEnabled("integratedmodmanager")) {
            // stands down for configlib-patches.json
            Api.Event.RegisterEventBusListener(HandleIMM, filterByEventName: "imm." + BetterErProspect.ModId);
        }
    }

    private static void HandleIMM(string eventName, ref EnumHandling handling, IAttribute data) {
        BetterErProspect.LoadFileConfig();
        PatchManager.handle();
        ReloadTools?.Invoke();
    }

    private static void SubscribeToConfigKit() {
        ConfigKitModSystem system = Api.ModLoader.GetModSystem<ConfigKitModSystem>();

        system.SettingChanged += (domain, _, setting) => {
            if (domain != BetterErProspect.ModId) return;
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
            var config = (ConfigKit.Config)system.GetConfig(BetterErProspect.ModId)!;
            ApplyConfigChange(config!);

            // When a client modifies settings from his side in MP, we need to reload ~ SettingChanged doesn't seem to capture him
            // nag maltiez to (?) fix this
            config!.ConfigSaved -= ApplyConfigChange;
            config!.ConfigSaved += ApplyConfigChange;
        };

        void ApplyConfigChange(ConfigKit.Config cfg) {
            cfg.AssignSettingsValues(BetterErProspect.Config);
            PatchManager.handle();
            ReloadTools?.Invoke();
        }
    }

    private static void SubscribeToConfigLib() {
        ConfigLibModSystem system = Api.ModLoader.GetModSystem<ConfigLibModSystem>();


        system.SettingChanged += (domain, _, setting) => {
            if (domain != BetterErProspect.ModId) return;
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
            var config = (ConfigLib.Config)system.GetConfig(BetterErProspect.ModId)!;
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
