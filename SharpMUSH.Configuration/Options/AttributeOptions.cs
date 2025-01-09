namespace SharpMUSH.Configuration.Options;

public record AttributeOptions(
	[PennConfig(Name = "adestroy")] bool ADestroy,
	[PennConfig(Name = "amail")] bool AMail,
	[PennConfig(Name = "player_listen")] bool PlayerListen,
	[PennConfig(Name = "player_ahear")] bool PlayerAHear,
	[PennConfig(Name = "startups")] bool Startups,
	[PennConfig(Name = "read_remote_desc")] bool ReadRemoteDesc,
	[PennConfig(Name = "room_connects")] bool RoomConnects,
	[PennConfig(Name = "reverse_shs")] bool ReverseShs,
	[PennConfig(Name = "empty_attrs")] bool EmptyAttributes
);