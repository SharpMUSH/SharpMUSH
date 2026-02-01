namespace SharpMUSH.Configuration.Options;

public record FlagOptions(
	[property: SharpConfig(
		Name = "player_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created players",
		Group = "Default Flags",
		Order = 1)]
	string[]? PlayerFlags,
	
	[property: SharpConfig(
		Name = "room_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created rooms",
		Group = "Default Flags",
		Order = 2)]
	string[]? RoomFlags,
	
	[property: SharpConfig(
		Name = "exit_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created exits",
		Group = "Default Flags",
		Order = 3)]
	string[]? ExitFlags,
	
	[property: SharpConfig(
		Name = "thing_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created things",
		Group = "Default Flags",
		Order = 4)]
	string[]? ThingFlags,
	
	[property: SharpConfig(
		Name = "channel_flags",
		Category = "Flag",
		Description = "Default flags assigned to newly created channels",
		Group = "Default Flags",
		Order = 5)]
	string[]? ChannelFlags
);
