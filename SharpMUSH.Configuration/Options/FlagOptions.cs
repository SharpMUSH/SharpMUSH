namespace SharpMUSH.Configuration.Options;

public record FlagOptions(
	[property: PennConfig(Name = "player_flags", Description = "Default flags assigned to newly created players")] string[]? PlayerFlags,
	[property: PennConfig(Name = "room_flags", Description = "Default flags assigned to newly created rooms")] string[]? RoomFlags,
	[property: PennConfig(Name = "exit_flags", Description = "Default flags assigned to newly created exits")] string[]? ExitFlags,
	[property: PennConfig(Name = "thing_flags", Description = "Default flags assigned to newly created things")] string[]? ThingFlags,
	[property: PennConfig(Name = "channel_flags", Description = "Default flags assigned to newly created channels")] string[]? ChannelFlags
);