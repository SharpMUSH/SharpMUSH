namespace SharpMUSH.Configuration.Options;

public record FlagOptions(
	[property: PennConfig(Name = "player_flags")] string[]? PlayerFlags,
	[property: PennConfig(Name = "room_flags")] string[]? RoomFlags,
	[property: PennConfig(Name = "exit_flags")] string[]? ExitFlags,
	[property: PennConfig(Name = "thing_flags")] string[]? ThingFlags,
	[property: PennConfig(Name = "channel_flags")] string[]? ChannelFlags
);