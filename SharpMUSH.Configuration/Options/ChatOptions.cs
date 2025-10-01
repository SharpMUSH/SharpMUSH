namespace SharpMUSH.Configuration.Options;

public record ChatOptions(
	[property: PennConfig(Name = "chat_token_alias", Description = "Character used as prefix for chat commands", DefaultValue = "+")] 
	char ChatTokenAlias,
	
	[property: PennConfig(Name = "use_muxcomm", Description = "Use MUX-style communication system", DefaultValue = "no")] 
	bool UseMuxComm,
	
	[property: PennConfig(Name = "max_channels", Description = "Maximum number of channels allowed on the MUSH", DefaultValue = "100")] 
	uint MaxChannels,
	
	[property: PennConfig(Name = "max_player_chans", Description = "Maximum channels a single player can join", DefaultValue = "20")] 
	uint MaxPlayerChannels,
	
	[property: PennConfig(Name = "chan_cost", Description = "Cost in pennies to create a new channel", DefaultValue = "1000")] 
	uint ChannelCost,
	
	[property: PennConfig(Name = "noisy_cemit", Description = "Show channel name in @cemit messages", DefaultValue = "yes")] 
	bool NoisyCEmit,
	
	[property: PennConfig(Name = "chan_title_len", Description = "Maximum length of channel titles", DefaultValue = "256")] 
	uint ChannelTitleLength
);