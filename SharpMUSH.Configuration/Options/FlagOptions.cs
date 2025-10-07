namespace SharpMUSH.Configuration.Options;

public record FlagOptions(
	[property: SharpConfig(Name = "player_flags", Category = "Flag", Description = "Default flags assigned to newly created players")] string[]? PlayerFlags,
	[property: SharpConfig(Name = "room_flags", Category = "Flag", Description = "Default flags assigned to newly created rooms")] string[]? RoomFlags,
	[property: SharpConfig(Name = "exit_flags", Category = "Flag", Description = "Default flags assigned to newly created exits")] string[]? ExitFlags,
	[property: SharpConfig(Name = "thing_flags", Category = "Flag", Description = "Default flags assigned to newly created things")] string[]? ThingFlags,
	[property: SharpConfig(Name = "channel_flags", Category = "Flag", Description = "Default flags assigned to newly created channels")] string[]? ChannelFlags
);