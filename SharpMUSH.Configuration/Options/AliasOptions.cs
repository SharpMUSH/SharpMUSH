namespace SharpMUSH.Configuration.Options;

public record AliasOptions(
	[property: SharpConfig(
		Name = "function_aliases",
		Description = "Function name aliases mapping", 
		Category = "Alias")]
	Dictionary<string, string[]> FunctionAliases,
	
	[property: SharpConfig(
		Name = "command_aliases",
		Description = "Command name aliases mapping", 
		Category = "Alias")]
	Dictionary<string, string[]> CommandAliases
);
