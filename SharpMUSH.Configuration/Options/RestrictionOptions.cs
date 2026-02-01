namespace SharpMUSH.Configuration.Options;

public record RestrictionOptions(
	[property: SharpConfig(
		Name = "command_restrictions",
		Category = "Restriction",
		Description = "Command restrictions mapping (command name to restriction levels)",
		Group = "Command Restrictions",
		Order = 1)]
	Dictionary<string, string[]> CommandRestrictions,
	
	[property: SharpConfig(
		Name = "function_restrictions",
		Category = "Restriction",
		Description = "Function restrictions mapping (function name to restriction levels)",
		Group = "Function Restrictions",
		Order = 1)]
	Dictionary<string, string[]> FunctionRestrictions
);
