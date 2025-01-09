namespace SharpMUSH.Configuration.Options;

public record FlagOptions(
	[PennConfig(Name = "player_flags")] string[]? PlayerFlags,
	[PennConfig(Name = "room_flags")] string[]? RoomFlags,
	[PennConfig(Name = "exit_flags")] string[]? ExitFlags,
	[PennConfig(Name = "thing_flags")] string[]? ThingFlags,
	[PennConfig(Name = "channel_flags")] string[]? ChannelFlags
);