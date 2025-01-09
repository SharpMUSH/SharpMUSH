namespace SharpMUSH.Configuration.Options;

public record FlagOptions(
	string[]? PlayerFlags,
	string[]? RoomFlags,
	string[]? ExitFlags,
	string[]? ThingFlags,
	string[]? ChannelFlags
);