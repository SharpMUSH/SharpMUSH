namespace SharpMUSH.Configuration.Options;

public record ChatOptions(
	char ChatTokenAlias = '+',
	bool UseMuxComm = true, 
	uint MaxChannels = 200,
	uint MaxPlayerChannels = 0,
	uint ChannelCost = 1000, 
	bool NoisyCEmit = false,
	uint ChannelTitleLength = 80
	);