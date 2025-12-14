using System.Collections.Generic;
using ProtoBuf;

namespace BetterErProspecting.Tracking;

[ProtoContract]
public class PptDataPacket(Dictionary<string, PptData> data) {
	[ProtoMember(1)]
	public Dictionary<string, PptData> AllData { get; set; } = data;

	public PptDataPacket() : this(new Dictionary<string, PptData>()) {
	}
}

[ProtoContract]
public class PptDataUpdatePacket {
	[ProtoMember(1)]
	public string OreCode { get; set; }

	[ProtoMember(2)]
	public PptData Data { get; set; }

	public PptDataUpdatePacket() { }

	public PptDataUpdatePacket(string oreCode, PptData data) {
		OreCode = oreCode;
		Data = data;
	}
}
