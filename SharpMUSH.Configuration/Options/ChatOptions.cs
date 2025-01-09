namespace SharpMUSH.Configuration.Options;

public record ChatOptions(
	[property: PennConfig(Name = "chat_token_alias")] char ChatTokenAlias,
	[property: PennConfig(Name = "use_muxcomm")] bool UseMuxComm,
	[property: PennConfig(Name = "max_channels")] uint MaxChannels,
	[property: PennConfig(Name = "max_player_chans")] uint MaxPlayerChannels,
	[property: PennConfig(Name = "chan_cost")] uint ChannelCost,
	[property: PennConfig(Name = "noisy_cemit")] bool NoisyCEmit,
	[property: PennConfig(Name = "chan_title_len")] uint ChannelTitleLength
);