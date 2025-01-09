namespace SharpMUSH.Configuration.Options;

public record ChatOptions(
	[PennConfig(Name = "chat_token_alias")] char ChatTokenAlias,
	[PennConfig(Name = "use_muxcomm")] bool UseMuxComm,
	[PennConfig(Name = "max_channels")] uint MaxChannels,
	[PennConfig(Name = "max_player_chans")] uint MaxPlayerChannels,
	[PennConfig(Name = "chan_cost")] uint ChannelCost,
	[PennConfig(Name = "noisy_cemit")] bool NoisyCEmit,
	[PennConfig(Name = "chan_title_len")] uint ChannelTitleLength
);