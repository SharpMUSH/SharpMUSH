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

	/// <summary>Window size (chars) either side of the error column used for the snippet.</summary>
	private const int SnippetRadius = 15;

	/// <summary>Maps ANTLR symbolic/literal token names to human-readable characters or phrases.</summary>
	private static readonly Dictionary<string, string> TokenDisplayNames = new(StringComparer.OrdinalIgnoreCase)
	{
		["CPAREN"]  = ")",
		["CBRACK"]  = "]",
		["CBRACE"]  = "}",
		["OPAREN"]  = "(",
		["OBRACK"]  = "[",
		["OBRACE"]  = "{",
		["COMMAWS"] = ",",
		["EQUALS"]  = "=",
		["SEMICOLON"] = ";",
		["PERCENT"] = "%",
		["ESCAPE"]  = "\\",
		["FUNCHAR"] = "function name",
		["EOF"]     = "end of input",
		["<EOF>"]   = "end of input",
	};

	public ParserErrorListener(string inputText)
	{
		_inputText = inputText;
	}

	/// <summary>Gets the list of errors collected during parsing.</summary>
	public IReadOnlyList<ParseError> Errors => _errors;

	/// <summary>Gets whether any errors were encountered.</summary>
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
		List<string>? expectedTokens = null;
		if (recognizer is Parser parser && e is not null)
		{
			expectedTokens = GetExpectedTokens(parser, e);
		}

		// Fallback: when the ATN-based extraction comes back empty, parse ANTLR's generated
		// message string. ANTLR produces:
		//   "missing 'X' at 'Y'"          → expected = [X]
		//   "mismatched input 'X' expecting 'Y'"  → expected = [Y]
		//   "mismatched input 'X' expecting {'A', 'B', ...}" → expected = [A, B, ...]
		if (expectedTokens is null or { Count: 0 })
		{
			expectedTokens = ParseExpectedFromMessage(msg);
		}

		var snippet = BuildSnippet(_inputText, charPositionInLine);
		var enhancedMessage = EnhanceErrorMessage(msg, offendingSymbol, expectedTokens);

		LspRange? errorRange = null;
		if (offendingSymbol is not null)
		{
			errorRange = new LspRange
			{
				Start = new Position(line - 1, charPositionInLine),
				End = new Position(line - 1, charPositionInLine + (offendingSymbol.Text?.Length ?? 1))
			};
		}
		else if (e?.OffendingToken is not null)
		{
			var token = e.OffendingToken;
			errorRange = new LspRange
			{
				Start = new Position(token.Line - 1, token.Column),
				End = new Position(token.Line - 1, token.Column + (token.Text?.Length ?? 1))
			};
		}

		_errors.Add(new ParseError
		{
			Line = line,
			Column = charPositionInLine,
			Range = errorRange,
			Message = enhancedMessage,
			OffendingToken = offendingSymbol?.Text ?? e?.OffendingToken?.Text,
			ExpectedTokens = expectedTokens,
			InputText = _inputText,
			Snippet = snippet,
		});
	}

	private static List<string>? GetExpectedTokens(Parser parser, RecognitionException e)
	{
		try
		{
			var expectedTokenSet = e.GetExpectedTokens();
			if (expectedTokenSet is null || expectedTokenSet.Count == 0)
				return null;

			var expectedTokens = new List<string>();
			var vocabulary = parser.Vocabulary;

			foreach (var tokenType in expectedTokenSet.ToArray())
			{
				var symbolicName = vocabulary.GetSymbolicName(tokenType);
				var literalName = vocabulary.GetLiteralName(tokenType)?.Trim('\'');

				// Prefer mapped display names, then literal names, then symbolic names.
				var raw = symbolicName ?? literalName ?? $"<token {tokenType}>";
				var display = MapTokenName(raw) ?? literalName ?? raw;
				expectedTokens.Add(display);
			}

			return expectedTokens.Count > 0 ? expectedTokens : null;
		}
		catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>
	/// Returns a human-readable display name for an ANTLR token, or <c>null</c> if no mapping exists.
	/// </summary>
	internal static string? MapTokenName(string antlrName)
		=> TokenDisplayNames.TryGetValue(antlrName, out var display) ? display : null;

	/// <summary>
	/// Parses ANTLR's generated error message to extract a list of expected tokens.
	/// Handles two common ANTLR message formats:
	/// <list type="bullet">
	///   <item><c>"missing 'X' at 'Y'"</c> — expects token X</item>
	///   <item><c>"mismatched input 'X' expecting 'Y'"</c> — expects token Y</item>
	///   <item><c>"mismatched input 'X' expecting {'A', 'B', ...}"</c> — expects one of A, B…</item>
	/// </list>
	/// Token names are mapped through <see cref="TokenDisplayNames"/> to human-readable chars.
	/// </summary>
	internal static List<string>? ParseExpectedFromMessage(string msg)
	{
		if (string.IsNullOrEmpty(msg))
			return null;

		// Pattern: "missing 'X' at ..."
		const string missingPrefix = "missing '";
		if (msg.StartsWith(missingPrefix, StringComparison.Ordinal))
		{
			var endQuote = msg.IndexOf('\'', missingPrefix.Length);
			if (endQuote > missingPrefix.Length)
			{
				var raw = msg[missingPrefix.Length..endQuote];
				var display = MapTokenName(raw) ?? raw;
				return [display];
			}
		}

		// Pattern: "mismatched input 'X' expecting 'Y'" or "... expecting {A, B, ...}"
		const string expectingKeyword = " expecting ";
		var expectIdx = msg.IndexOf(expectingKeyword, StringComparison.Ordinal);
		if (expectIdx >= 0)
		{
			var rest = msg[(expectIdx + expectingKeyword.Length)..];

			// Set notation: expecting {'A', 'B'}
			if (rest.StartsWith("{", StringComparison.Ordinal))
			{
				var setEnd = rest.IndexOf('}');
				if (setEnd > 0)
				{
					var setContent = rest[1..setEnd];
					var parts = setContent.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
					var result = new List<string>();
					foreach (var part in parts)
					{
						var raw = part.Trim('\'');
						result.Add(MapTokenName(raw) ?? raw);
					}
					return result.Count > 0 ? result : null;
				}
			}

			// Single token: expecting 'Y'
			if (rest.StartsWith("'", StringComparison.Ordinal))
			{
				var endQuote = rest.IndexOf('\'', 1);
				if (endQuote > 0)
				{
					var raw = rest[1..endQuote];
					var display = MapTokenName(raw) ?? raw;
					return [display];
				}
			}
		}

		return null;
	}

	/// <summary>
	/// Extracts a short window of plain text around <paramref name="column"/>.
	/// The result is trimmed of leading/trailing whitespace and prefixed with "..." / suffixed
	/// with "..." when the window does not reach the start/end of the input.
	/// </summary>
	internal static string? BuildSnippet(string inputText, int column)
	{
		if (string.IsNullOrEmpty(inputText) || column < 0)
			return null;

		var start = Math.Max(0, column - SnippetRadius);
		var end   = Math.Min(inputText.Length, column + SnippetRadius);

		if (start >= end)
			return null;

		var window = inputText[start..end];
		var prefix = start > 0 ? "..." : string.Empty;
		var suffix = end < inputText.Length ? "..." : string.Empty;

		return $"{prefix}{window}{suffix}";
	}

	private static string EnhanceErrorMessage(string originalMessage, IToken? offendingSymbol, List<string>? expectedTokens)
	{
		// When the offending token is EOF, the real problem is what was *expected*.
		// Produce a plain "end of input" message; ToMushFailureString() will prepend "Expected X".
		var isEof = offendingSymbol?.Type == TokenConstants.EOF
			|| (offendingSymbol?.Text is "<EOF>" or null && (originalMessage.Contains("EOF") || originalMessage.Contains("end of file")));

		if (isEof)
		{
			return "end of input";
		}

		if (expectedTokens is { Count: > 0 } && offendingSymbol is not null)
		{
			return $"Unexpected token '{offendingSymbol.Text}' at this position";
		}

		if (originalMessage.Contains("EOF") || originalMessage.Contains("end of file"))
		{
			return "end of input";
		}

		return originalMessage;
	}

	/// <summary>Clears all collected errors.</summary>
	public void Clear() => _errors.Clear();
}
