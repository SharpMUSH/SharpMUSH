namespace SharpMUSH.Configuration.Options;

public record ChatOptions(
	[property: SharpConfig(
		Name = "chat_token_alias",
		Category = "Chat",
		Description = "Character used as prefix for chat commands",
		ValidationPattern = @"^.$",
		Group = "General",
		Order = 1)]
	char ChatTokenAlias,

	[property: SharpConfig(
		Name = "use_muxcomm",
		Category = "Chat",
		Description = "Use MUX-style communication system",
		Group = "General",
		Order = 2)]
	bool UseMuxComm,

	[property: SharpConfig(
		Name = "max_channels",
		Category = "Chat",
		Description = "Maximum number of channels allowed on the MUSH",
		ValidationPattern = @"^\d+$",
		Group = "Limits",
		Order = 1,
		Min = 1,
		Max = 10000)]
	uint MaxChannels,

	[property: SharpConfig(
		Name = "max_player_chans",
		Category = "Chat",
		Description = "Maximum channels a single player can join",
		ValidationPattern = @"^\d+$",
		Group = "Limits",
		Order = 2,
		Min = 1,
		Max = 100)]
	uint MaxPlayerChannels,

	[property: SharpConfig(
		Name = "chan_cost",
		Category = "Chat",
		Description = "Cost in pennies to create a new channel",
		ValidationPattern = @"^\d+$",
		Group = "Economy",
		Order = 1,
		Min = 0,
		Max = 100000)]
	uint ChannelCost,

	[property: SharpConfig(
		Name = "noisy_cemit",
		Category = "Chat",
		Description = "Show channel name in @cemit messages",
		Group = "Behavior",
		Order = 1)]
	bool NoisyCEmit,

	[property: SharpConfig(
		Name = "chan_title_len",
		Category = "Chat",
		Description = "Maximum length of channel titles",
		ValidationPattern = @"^\d+$",
		Group = "Limits",
		Order = 3,
		Min = 10,
		Max = 500)]
	uint ChannelTitleLength
);
