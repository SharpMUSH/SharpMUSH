namespace SharpMUSH.Configuration.Options;

public record AttributeOptions(
	[PennConfig(Name = "adestroy")] bool ADestroy = false,
	[PennConfig(Name = "amail")] bool AMail = false,
	[PennConfig(Name = "player_listen")] bool PlayerListen = true,
	[PennConfig(Name = "player_ahear")] bool PlayerAHear = true,
	[PennConfig(Name = "startups")] bool Startups = true,
	[PennConfig(Name = "read_remote_desc")] bool ReadRemoteDesc = false,
	[PennConfig(Name = "room_connects")] bool RoomConnects = true,
	[PennConfig(Name = "reverse_shs")] bool ReverseShs = true,
	[PennConfig(Name = "empty_attrs")] bool EmptyAttributes = true);