namespace SharpMUSH.Configuration.Options;

public record ChatOptions(
	[PennConfig(Name = "chat_token_alias")] char ChatTokenAlias = '+',
	[PennConfig(Name = "use_muxcomm")] bool UseMuxComm = true,
	[PennConfig(Name = "max_channels")] uint MaxChannels = 200,
	[PennConfig(Name = "max_player_chans")] uint MaxPlayerChannels = 0,
	[PennConfig(Name = "chan_cost")] uint ChannelCost = 1000,
	[PennConfig(Name = "noisy_cemit")] bool NoisyCEmit = false,
	[PennConfig(Name = "chan_title_len")] uint ChannelTitleLength = 80
);