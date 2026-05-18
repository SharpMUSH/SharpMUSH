namespace SharpMUSH.Configuration.Options;

public record DebugOptions(
	[property: SharpConfig(
		Name = "debug_sharpparser",
		Category = "Debug",
		Description = "Enable debug output for the SharpMUSH parser",
		Group = "Parser Debugging",
		Order = 1)]
	bool DebugSharpParser,

	[property: SharpConfig(
		Name = "parser_prediction_mode",
		Category = "Debug",
		Description = "Parser prediction mode: SLL (faster) or LL (more powerful). Default: LL",
		Group = "Parser Configuration",
		Order = 1,
		Tooltip = "LL evaluates semantic predicates correctly; SLL is faster but may misparse predicate-gated alternatives")]
	ParserPredictionMode ParserPredictionMode = ParserPredictionMode.LL
);

/// <summary>
/// ANTLR4 parser prediction mode.
/// </summary>
public enum ParserPredictionMode
{
	/// <summary>
	/// Strong LL parsing - faster but less powerful. Ignores semantic predicates during
	/// prediction, which can cause incorrect parses for predicate-gated grammar alternatives.
	/// </summary>
	SLL,

	/// <summary>
	/// Full LL(*) parsing - evaluates semantic predicates correctly. Default mode.
	/// </summary>
	LL
}
