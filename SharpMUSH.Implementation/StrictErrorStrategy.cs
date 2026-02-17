using Antlr4.Runtime;

namespace SharpMUSH.Implementation;

/// <summary>
/// Error strategy that throws exceptions immediately on syntax errors.
/// This is useful for testing to identify all parsing issues.
/// </summary>
public class StrictErrorStrategy : DefaultErrorStrategy
{
	/// <summary>
	/// Instead of recovering from an error, throw an exception immediately.
	/// </summary>
	public override void Recover(Parser recognizer, RecognitionException e)
	{
		throw new ParseCanceledException($"Parser error: {e.Message}", e);
	}

	/// <summary>
	/// Instead of recovering from a token mismatch, throw an exception immediately.
	/// </summary>
	public override IToken RecoverInline(Parser recognizer)
	{
		var exception = new InputMismatchException(recognizer);
		throw new ParseCanceledException($"Unexpected token: {exception.Message}", exception);
	}

	/// <summary>
	/// Don't attempt to recover from mismatched tokens in subrules.
	/// </summary>
	public override void Sync(Parser recognizer)
	{
		// Don't sync - let errors bubble up immediately
	}
}
