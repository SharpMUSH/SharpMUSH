namespace SharpMUSH.Configuration.Options;

public record ChatOptions(
	[property: SharpConfig(Name = "chat_token_alias", Category = "Chat", Description = "Character used as prefix for chat commands", ValidationPattern = @"^.$")]
	char ChatTokenAlias,
	[property: SharpConfig(Name = "use_muxcomm", Category = "Chat", Description = "Use MUX-style communication system")]
	bool UseMuxComm,
	[property: SharpConfig(Name = "max_channels", Category = "Chat", Description = "Maximum number of channels allowed on the MUSH", ValidationPattern = @"^\d+$")]
	uint MaxChannels,
	[property: SharpConfig(Name = "max_player_chans", Category = "Chat", Description = "Maximum channels a single player can join", ValidationPattern = @"^\d+$")]
	uint MaxPlayerChannels,
	[property: SharpConfig(Name = "chan_cost", Category = "Chat", Description = "Cost in pennies to create a new channel", ValidationPattern = @"^\d+$")]
	uint ChannelCost,
	[property: SharpConfig(Name = "noisy_cemit", Category = "Chat", Description = "Show channel name in @cemit messages")]
	bool NoisyCEmit,
	[property: SharpConfig(Name = "chan_title_len", Category = "Chat", Description = "Maximum length of channel titles", ValidationPattern = @"^\d+$")]
	uint ChannelTitleLength
);