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
		var ruleName = recognizer.RuleNames[recognizer.RuleContext.RuleIndex];
		throw new InvalidOperationException($"Parser error in rule '{ruleName}': {e.Message}", e);
	}

	/// <summary>
	/// Instead of recovering from a token mismatch, throw an exception immediately.
	/// </summary>
	public override IToken RecoverInline(Parser recognizer)
	{
		var exception = new InputMismatchException(recognizer);
		var ruleName = recognizer.RuleNames[recognizer.RuleContext.RuleIndex];
		throw new InvalidOperationException($"Unexpected token in rule '{ruleName}': {exception.Message}", exception);
	}

	/// <summary>
	/// Don't attempt to recover from mismatched tokens in subrules.
	/// </summary>
	public override void Sync(Parser recognizer)
	{
		// Don't sync - let errors bubble up immediately
	}
}
