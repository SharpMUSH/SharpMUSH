namespace SharpMUSH.Configuration.Options;

public record ChatOptions(
	[property: SharpConfig(Name = "chat_token_alias", Description = "Character used as prefix for chat commands")] 
	char ChatTokenAlias,
	
	[property: SharpConfig(Name = "use_muxcomm", Description = "Use MUX-style communication system")] 
	bool UseMuxComm,
	
	[property: SharpConfig(Name = "max_channels", Description = "Maximum number of channels allowed on the MUSH")] 
	uint MaxChannels,
	
	[property: SharpConfig(Name = "max_player_chans", Description = "Maximum channels a single player can join")] 
	uint MaxPlayerChannels,
	
	[property: SharpConfig(Name = "chan_cost", Description = "Cost in pennies to create a new channel")] 
	uint ChannelCost,
	
	[property: SharpConfig(Name = "noisy_cemit", Description = "Show channel name in @cemit messages")] 
	bool NoisyCEmit,
	
	[property: SharpConfig(Name = "chan_title_len", Description = "Maximum length of channel titles")] 
	uint ChannelTitleLength
);