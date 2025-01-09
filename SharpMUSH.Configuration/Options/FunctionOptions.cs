namespace SharpMUSH.Configuration.Options;

public record FunctionOptions(
	[property: PennConfig(Name = "safer_ufun")] bool SaferUserFunctions,
	[property: PennConfig(Name = "fun_sideeffects")] bool FunctionSideEffects
);