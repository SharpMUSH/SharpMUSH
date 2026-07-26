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
		Description = "Parser prediction mode: TwoStage (default), SLL, or LL",
		Group = "Parser Configuration",
		Order = 1,
		Tooltip = "TwoStage runs SLL first and only re-runs LL if SLL reports a syntax error — LL's result whenever they could differ, at SLL's speed the rest of the time. SLL and LL force a single mode, for diagnostics.")]
	ParserPredictionMode ParserPredictionMode = ParserPredictionMode.TwoStage
);

/// <summary>
/// ANTLR4 parser prediction strategy.
/// </summary>
public enum ParserPredictionMode
{
	/// <summary>
	/// Strong LL: ignores the parser call stack during prediction. Fastest, and — measured across
	/// the whole test corpus — produces results identical to LL for this grammar. Forcing it is a
	/// diagnostic option; <see cref="TwoStage"/> gets the same speed without betting on that.
	/// </summary>
	SLL,

	/// <summary>
	/// Full LL(*): considers the full call stack. The authoritative result, and slower. A
	/// diagnostic option for confirming a parse independent of SLL.
	/// </summary>
	LL,

	/// <summary>
	/// Parse with <see cref="SLL"/> first; only if it reports a syntax error, discard that attempt
	/// and re-parse with <see cref="LL"/>. ANTLR guarantees SLL either matches LL or reports an
	/// error, so this yields LL's result whenever the two could differ and SLL's speed otherwise.
	/// The default: on error-free input — the common case — it costs one SLL pass.
	/// </summary>
	TwoStage
}
