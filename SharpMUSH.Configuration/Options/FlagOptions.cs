namespace SharpMUSH.Configuration.Options;

public record FlagOptions(
	string? PlayerFlags = "enter_ok ansi no_command",
	string? RoomFlags = "no_command",
	string? ExitFlags = "",
	string? ThingFlags = "no_command",
	string? ChannelFlags = "player"
);