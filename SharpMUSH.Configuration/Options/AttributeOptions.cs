namespace SharpMUSH.Configuration.Options;

public record AttributeOptions(
	bool ADestroy = false,
	bool AMail = false,
	bool PlayerListen = true,
	bool PlayerAHear = true,
	bool Startups = true,
	bool ReadRemoteDesc = false,
	bool RoomConnects = true,
	bool ReverseShs = true,
	bool EmptyAttributes = true);