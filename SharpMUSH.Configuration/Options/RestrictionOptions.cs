namespace SharpMUSH.Configuration.Options;

public record RestrictionOptions(
	[property: SharpConfig(
		Name = "command_restrictions",
		Description = "Command restrictions mapping (command name to restriction levels)", 
		Category = "Restriction")]
	Dictionary<string, string[]> CommandRestrictions,
	
	[property: SharpConfig(
		Name = "function_restrictions",
		Description = "Function restrictions mapping (function name to restriction levels)", 
		Category = "Restriction")]
	Dictionary<string, string[]> FunctionRestrictions
);
