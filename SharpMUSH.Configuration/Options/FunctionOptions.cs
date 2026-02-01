namespace SharpMUSH.Configuration.Options;

public record FunctionOptions(
	[property: SharpConfig(
		Name = "safer_ufun",
		Category = "Function",
		Description = "Enable additional security checks for user-defined functions",
		Group = "Security",
		Order = 1)]
	bool SaferUserFunctions,
	
	[property: SharpConfig(
		Name = "function_side_effects",
		Category = "Function",
		Description = "Allow functions to have side effects beyond return values",
		Group = "Behavior",
		Order = 1)]
	bool FunctionSideEffects
);
