namespace SharpMUSH.Configuration.Options;

public record FlagOptions(
	[property: PennConfig(Name = "player_flags", Description = "Default flags assigned to newly created players", DefaultValue = "player")] string[]? PlayerFlags,
	[property: PennConfig(Name = "room_flags", Description = "Default flags assigned to newly created rooms", DefaultValue = "no_command")] string[]? RoomFlags,
	[property: PennConfig(Name = "exit_flags", Description = "Default flags assigned to newly created exits", DefaultValue = "no_command")] string[]? ExitFlags,
	[property: PennConfig(Name = "thing_flags", Description = "Default flags assigned to newly created things", DefaultValue = "")] string[]? ThingFlags,
	[property: PennConfig(Name = "channel_flags", Description = "Default flags assigned to newly created channels", DefaultValue = "player")] string[]? ChannelFlags
);