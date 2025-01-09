namespace SharpMUSH.Configuration.Options;

public record FunctionOptions(
	[PennConfig(Name = "safer_ufun")] bool SaferUserFunctions,
	[PennConfig(Name = "fun_sideeffects")] bool FunctionSideEffects
);