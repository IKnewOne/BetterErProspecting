using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BetterErProspecting.Prospecting;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace BetterErProspecting.Tracking;

[ProtoContract]
public class PptData {
	[ProtoMember(1)]
	public double MinPpt = 1000.0;
	[ProtoMember(2)]
	public double MaxPpt = 0.0;

	public void Update(double ppt) {
		if (ppt < MinPpt) MinPpt = ppt;
		if (ppt > MaxPpt) MaxPpt = ppt;
	}
}

public class PptTracker : ModSystem {
	private readonly ConcurrentDictionary<string, PptData> oreData = new();
	private const string SaveKey = "betterErProspectingPptData";
	private const string ChannelName = "bettererprospecting_ppt";

	private IServerNetworkChannel serverChannel;
	private IClientNetworkChannel clientChannel;
	private ICoreServerAPI sapi;
	private ICoreClientAPI capi;


	public override void StartServerSide(ICoreServerAPI api) {
		base.StartServerSide(api);
		sapi = api;

		sapi.ChatCommands.GetOrCreate("btrpr")
			.RequiresPrivilege(Privilege.controlserver)
			.BeginSub("oreData")
				.RequiresPlayer()
				.WithDesc("Dumps all ores data from memory and file storage and rewrites from all existing ore readings. Best to reprospect first. May cause lags.")
				.WithExamples("/btrpr oreData")
				.HandleWith(DumpAndReload)
			.EndSub();

		serverChannel = api.Network.RegisterChannel(ChannelName)
			.RegisterMessageType<PptDataPacket>()
			.RegisterMessageType<PptDataUpdatePacket>();

		api.Event.PlayerJoin += OnPlayerJoin;
		api.Event.SaveGameLoaded += OnSaveGameLoaded;
		api.Event.GameWorldSave += OnSaveGameGettingSaved;
	}

	public override void StartClientSide(ICoreClientAPI api) {
		base.StartClientSide(api);
		capi = api;

		clientChannel = api.Network.RegisterChannel(ChannelName)
			.RegisterMessageType<PptDataPacket>()
			.RegisterMessageType<PptDataUpdatePacket>()
			.SetMessageHandler<PptDataPacket>(OnClientReceivedFullData)
			.SetMessageHandler<PptDataUpdatePacket>(OnClientReceivedUpdate);
	}

	private void OnSaveGameLoaded() {
		oreData.Clear();

		byte[] savedData = sapi.WorldManager.SaveGame.GetData(SaveKey);
		if (savedData != null) {
			var loaded = SerializerUtil.Deserialize<Dictionary<string, PptData>>(savedData);
			if (loaded == null) return;
			foreach (var kvp in loaded) {
				oreData[kvp.Key] = kvp.Value;
			}
			Mod.Logger.Debug($"[BetterErProspecting] Loaded ppt data for {loaded.Count} ore codes from save");
		} else {
			// Absolute cold start. Lets normalize all readings
			sapi.ModLoader.GetModSystem<ProspectingSystem>().ReprospectTask(null, null).Wait();
		}

		// We need the page codes and ppws delays to async RunGame phase action
		sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
		{
			ScheduleBackfillWhenReady();
		});
	}

	private void OnSaveGameGettingSaved() {
		if (oreData.IsEmpty) {
			Mod.Logger.Debug("[BetterErProspecting] No data to save");
			return;
		}

		using var ms = new FastMemoryStream();
		var dataToSave = new Dictionary<string, PptData>(oreData);
		sapi.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(dataToSave, ms));

		Mod.Logger.Debug($"[BetterErProspecting] Saved ppt data for {dataToSave.Count} ore codes");
		oreData.Clear();
	}

	private void OnClientReceivedFullData(PptDataPacket packet) {
		if (packet?.AllData == null)
			return;

		oreData.Clear();
		foreach (var kvp in packet.AllData) {
			oreData[kvp.Key] = kvp.Value;
		}

		Mod.Logger.Debug($"[BetterErProspecting] Client received ppt data for {packet.AllData.Count} ore codes");
	}

	private void OnClientReceivedUpdate(PptDataUpdatePacket packet) {
		if (packet == null || string.IsNullOrEmpty(packet.OreCode) || packet.Data == null)
			return;

		oreData[packet.OreCode] = packet.Data;
		Mod.Logger.Debug($"[BetterErProspecting] Client received ppt data update for {packet.OreCode}");
	}

	private void OnPlayerJoin(IServerPlayer byPlayer) {
		if (oreData.IsEmpty)
			return;

		var packet = new PptDataPacket(new Dictionary<string, PptData>(oreData));
		serverChannel?.SendPacket(packet, byPlayer);
	}

	public void UpdatePpt(string oreCode, double ppt) {

		if (string.IsNullOrEmpty(oreCode))
			return;

		var data = oreData.GetOrAdd(oreCode, _ => new PptData());
		data.Update(ppt);

		SendUpdateToClients(oreCode, data);
	}

	private void SendUpdateToClients(string oreCode, PptData data) {
		if (serverChannel == null)
			return;

		var packet = new PptDataUpdatePacket(oreCode, data);
		serverChannel.BroadcastPacket(packet);
	}

	public void AdjustReadingFactor(OreReading reading) {
		reading.TotalFactor = GetAdjustedFactor(reading);
	}

	public double GetAdjustedFactor(OreReading reading) {
		if (reading?.DepositCode == null) {
			return reading?.TotalFactor ?? 0.0;
		}

		if (!oreData.TryGetValue(reading.DepositCode, out var data) || Math.Abs(data.MaxPpt - data.MinPpt) < 0.0001) {
			// First reading
			return 1.0;
		}

		double normalizedValue = (reading.PartsPerThousand - data.MinPpt) / (data.MaxPpt - data.MinPpt);
		double adjustedFactor = 0.15 + (normalizedValue * 0.85);
		return Math.Clamp(adjustedFactor, 0.15, 1.0);
	}

	private TextCommandResult DumpAndReload(TextCommandCallingArgs args) {
		Mod.Logger.Notification($"[BetterErProspecting] Starting dump and reload of all ore readings data. Initiated by {args.Caller.Player.PlayerName}...");
		oreData.Clear();

		using (var ms = new FastMemoryStream()) {
			var emptyDict = new Dictionary<string, PptData>();
			sapi.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(emptyDict, ms));
		}

		BackfillMissingOreCodes();

		Mod.Logger.Notification($"[BetterErProspecting] Dump and reload complete. Tracked {oreData.Count} ore codes.");
		return TextCommandResult.Success($"Successfully reloaded ore readings data. Now tracking {oreData.Count} ore codes.");
	}

	private void ScheduleBackfillWhenReady(int attemptCount = 0) {
		const int maxAttempts = 30;
		const int delayMs = 1000;

		sapi.Event.RegisterCallback((dt) =>
		{
			var ppws = ObjectCacheUtil.TryGet<ProPickWorkSpace>(sapi, "propickworkspace");
			if (ppws?.pageCodes is { Count: > 0 }) {
				Mod.Logger.Debug($"[BetterErProspecting] ProPickWorkSpace pageCodes ready with {ppws.pageCodes.Count} entries, starting backfill");
				BackfillMissingOreCodes();
			} else if (attemptCount < maxAttempts) {
				Mod.Logger.Debug($"[BetterErProspecting] ProPickWorkSpace pageCodes not ready, retrying in {delayMs}ms (attempt {attemptCount + 1}/{maxAttempts})");
				ScheduleBackfillWhenReady(attemptCount + 1);
			} else {
				Mod.Logger.Warning("[BetterErProspecting] Timed out waiting for ProPickWorkSpace pageCodes to be populated");
			}
		}, delayMs);
	}

	private void BackfillMissingOreCodes() {
		var ppws = ObjectCacheUtil.TryGet<ProPickWorkSpace>(sapi, "propickworkspace");
		if (ppws?.pageCodes == null) {
			Mod.Logger.Error("[BetterErProspecting] ProPickWorkSpace not available, couldn't perform backfill");
			return;
		}

		var missingOreCodes = new HashSet<string>();
		foreach (var oreCode in ppws.pageCodes.Keys.Where(oreCode => !oreData.ContainsKey(oreCode))) {
			missingOreCodes.Add(oreCode);
		}

		if (missingOreCodes.Count <= 0) return;
		Mod.Logger.Notification($"[BetterErProspecting] Backfilling {missingOreCodes.Count} missing ore codes: {string.Join(", ", missingOreCodes)}");


		var allReadings = getAllPlayerReadings(sapi);
		int readingCount = allReadings.Count;

		foreach (var reading in allReadings) {
			foreach (var oreReading in reading.OreReadings) {
				if (missingOreCodes.Contains(oreReading.Key)) {
					UpdatePpt(oreReading.Key, oreReading.Value.PartsPerThousand);
				}
			}
		}

		Mod.Logger.Debug($"[BetterErProspecting] Backfilled from {readingCount} readings");
	}

	// Fills per-player data in oml as well as returns list of all readings
	public static List<PropickReading> getAllPlayerReadings(ICoreServerAPI sapi) {
		var result = new List<PropickReading>();
		var oml = sapi.ModLoader?.GetModSystem<WorldMapManager>()?.MapLayers?.FirstOrDefault(ml => ml is OreMapLayer) as OreMapLayer;
		if (oml == null) {
			BetterErProspect.Logger.Warning("[BetterErProspecting] OreMapLayer not available for backfill");
			return result;
		}

		foreach (var playerUid in sapi.PlayerData.PlayerDataByUid.Keys) {
			result.AddRange(getOrLoadReadings(playerUid, oml, sapi));
		}

		return result;
	}

	public static List<PropickReading> getOrLoadReadings(string playeruid, OreMapLayer oml, ICoreServerAPI sapi) {
		if (oml.PropickReadingsByPlayer.TryGetValue(playeruid, out var orLoadReadings))
			return orLoadReadings;
		byte[] data = sapi.WorldManager.SaveGame.GetData("oreMapMarkers-" + playeruid);
		return data != null ? (oml.PropickReadingsByPlayer[playeruid] = SerializerUtil.Deserialize<List<PropickReading>>(data)) : (oml.PropickReadingsByPlayer[playeruid] = []);
	}
}
