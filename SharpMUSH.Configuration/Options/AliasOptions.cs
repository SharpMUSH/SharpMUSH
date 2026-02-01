namespace SharpMUSH.Configuration.Options;

public record AliasOptions(
	[property: SharpConfig(
		Name = "function_aliases",
		Category = "Alias",
		Description = "Function name aliases mapping",
		Group = "Function Aliases",
		Order = 1)]
	Dictionary<string, string[]> FunctionAliases,
	
	[property: SharpConfig(
		Name = "command_aliases",
		Category = "Alias",
		Description = "Command name aliases mapping",
		Group = "Command Aliases",
		Order = 1)]
	Dictionary<string, string[]> CommandAliases
);
