namespace SharpMUSH.Configuration.Options;

public record FunctionOptions(
	bool SaferUserFunctions,
	bool FunctionSideEffects
);