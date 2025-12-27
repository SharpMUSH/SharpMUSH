	using Antlr4.Runtime;
using SharpMUSH.Library.Models;
using LspRange = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.Implementation;

/// <summary>
/// Custom error listener that collects detailed parse error information.
/// </summary>
public class ParserErrorListener : BaseErrorListener
{
	private readonly List<ParseError> _errors = [];
	private readonly string _inputText;

	public ParserErrorListener(string inputText)
	{
		_inputText = inputText;
	}

	/// <summary>
	/// Gets the list of errors collected during parsing.
	/// </summary>
	public IReadOnlyList<ParseError> Errors => _errors;

	/// <summary>
	/// Gets whether any errors were encountered.
	/// </summary>
	public bool HasErrors => _errors.Count > 0;

	public override void SyntaxError(
		TextWriter output,
		IRecognizer recognizer,
		IToken offendingSymbol,
		int line,
		int charPositionInLine,
		string msg,
		RecognitionException e)
	{
		// Extract expected tokens if available
		List<string>? expectedTokens = null;
		
		if (recognizer is Parser parser && e is not null)
		{
			expectedTokens = GetExpectedTokens(parser, e);
		}

		// Create a more informative error message
		var enhancedMessage = EnhanceErrorMessage(msg, offendingSymbol, expectedTokens);

		// Determine the error range
		LspRange? errorRange = null;
		if (offendingSymbol is not null)
		{
			// Range spans the offending token
			errorRange = new LspRange
			{
				Start = new Position(line - 1, charPositionInLine),
				End = new Position(line - 1, charPositionInLine + offendingSymbol.Text.Length)
			};
		}
		else if (e?.OffendingToken is not null)
		{
			// Use exception's offending token if available
			var token = e.OffendingToken;
			errorRange = new LspRange
			{
				Start = new Position(token.Line - 1, token.Column),
				End = new Position(token.Line - 1, token.Column + token.Text.Length)
			};
		}

		var error = new ParseError
		{
			Line = line,
			Column = charPositionInLine,
			Range = errorRange,
			Message = enhancedMessage,
			OffendingToken = offendingSymbol?.Text ?? e?.OffendingToken?.Text,
			ExpectedTokens = expectedTokens,
			InputText = _inputText
		};

		_errors.Add(error);
	}

	private static List<string>? GetExpectedTokens(Parser parser, RecognitionException e)
	{
		try
		{
			var expectedTokenSet = e.GetExpectedTokens();
			if (expectedTokenSet is null || expectedTokenSet.Count == 0)
			{
				return null;
			}

			var expectedTokens = new List<string>();
			var vocabulary = parser.Vocabulary;

			foreach (var tokenType in expectedTokenSet.ToArray())
			{
				var symbolicName = vocabulary.GetSymbolicName(tokenType);
				var literalName = vocabulary.GetLiteralName(tokenType);
				
				// Prefer literal names (e.g., '[') over symbolic names (e.g., OBRACK)
				var tokenName = literalName ?? symbolicName ?? $"<token {tokenType}>";
				expectedTokens.Add(tokenName);
			}

			return expectedTokens.Count > 0 ? expectedTokens : null;
		}
		catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException)
		{
			// Expected token extraction can fail if vocabulary is incomplete
			// This is not critical - we just won't have expected token suggestions
			return null;
		}
	}

	private static string EnhanceErrorMessage(string originalMessage, IToken? offendingSymbol, List<string>? expectedTokens)
	{
		// If we have a clear expectation, make the message more helpful
		if (expectedTokens is not null && expectedTokens.Count > 0)
		{
			if (offendingSymbol is not null)
			{
				return $"Unexpected token '{offendingSymbol.Text}' at this position";
			}
			return originalMessage;
		}

		// For EOF errors, provide clearer messaging
		if (originalMessage.Contains("EOF") || originalMessage.Contains("end of file"))
		{
			return "Unexpected end of input - missing closing delimiter?";
		}

		return originalMessage;
	}

	/// <summary>
	/// Clears all collected errors.
	/// </summary>
	public void Clear()
	{
		_errors.Clear();
	}
}
