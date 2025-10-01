namespace SharpMUSH.Configuration.Options;

public record ChatOptions(
	[property: PennConfig(Name = "chat_token_alias", Description = "Character used as prefix for chat commands", Category = "Chat System", DefaultValue = "+")] 
	char ChatTokenAlias,
	
	[property: PennConfig(Name = "use_muxcomm", Description = "Use MUX-style communication system", Category = "Chat System", DefaultValue = "no")] 
	bool UseMuxComm,
	
	[property: PennConfig(Name = "max_channels", Description = "Maximum number of channels allowed on the MUSH", Category = "Chat Limits", DefaultValue = "100")] 
	uint MaxChannels,
	
	[property: PennConfig(Name = "max_player_chans", Description = "Maximum channels a single player can join", Category = "Chat Limits", DefaultValue = "20")] 
	uint MaxPlayerChannels,
	
	[property: PennConfig(Name = "chan_cost", Description = "Cost in pennies to create a new channel", Category = "Chat Economy", DefaultValue = "1000")] 
	uint ChannelCost,
	
	[property: PennConfig(Name = "noisy_cemit", Description = "Show channel name in @cemit messages", Category = "Chat Display", DefaultValue = "yes")] 
	bool NoisyCEmit,
	
	[property: PennConfig(Name = "chan_title_len", Description = "Maximum length of channel titles", Category = "Chat Limits", DefaultValue = "256")] 
	uint ChannelTitleLength
);