namespace SharpMUSH.Configuration.Options;

public record DebugOptions(
	[property: SharpConfig(Name = "debug_sharpparser", Category = "Debug", Description = "Enable debug output for the SharpMUSH parser")] bool DebugSharpParser,
	[property: SharpConfig(Name = "parser_prediction_mode", Category = "Debug", Description = "Parser prediction mode: SLL (faster) or LL (more powerful). Default: LL")] ParserPredictionMode ParserPredictionMode = ParserPredictionMode.LL
);

/// <summary>
/// ANTLR4 parser prediction mode.
/// </summary>
public enum ParserPredictionMode
{
	/// <summary>
	/// Strong LL parsing - faster but less powerful. Use for simpler grammars.
	/// </summary>
	SLL,
	
	/// <summary>
	/// Full LL(*) parsing - slower but more powerful. Can handle complex grammars.
	/// Default mode.
	/// </summary>
	LL
}