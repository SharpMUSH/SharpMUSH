namespace SharpMUSH.Configuration.Options;

public record FunctionOptions(
	[property: PennConfig(Name = "safer_ufun", Description = "Enable additional security checks for user-defined functions")] bool SaferUserFunctions,
	[property: PennConfig(Name = "function_side_effects", Description = "Allow functions to have side effects beyond return values")] bool FunctionSideEffects
);