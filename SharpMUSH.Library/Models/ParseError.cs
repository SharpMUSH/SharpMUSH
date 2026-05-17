using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents a parsing error with detailed information about what went wrong and where.
/// This class is maintained for backward compatibility. For LSP integration, use <see cref="Diagnostic"/> instead.
/// </summary>
public record ParseError
{
	/// <summary>
	/// The line number where the error occurred (1-based for backward compatibility).
	/// </summary>
	public int Line { get; init; }

	/// <summary>
	/// The column position where the error occurred (0-based).
	/// </summary>
	public int Column { get; init; }

	/// <summary>
	/// The range where the error occurred (LSP-compatible, 0-based).
	/// If not set, defaults to a single position at (Line-1, Column).
	/// </summary>
	public Range? Range { get; init; }

	/// <summary>
	/// The error message describing what went wrong.
	/// </summary>
	public required string Message { get; init; }

	/// <summary>
	/// The offending token text, if available.
	/// </summary>
	public string? OffendingToken { get; init; }

	/// <summary>
	/// The expected tokens, if available.
	/// </summary>
	public IReadOnlyList<string>? ExpectedTokens { get; init; }

	/// <summary>
	/// The input text where the error occurred.
	/// </summary>
	public string? InputText { get; init; }

	/// <summary>
	/// A short window of text around the error position (±15 characters), for context.
	/// </summary>
	public string? Snippet { get; init; }

	/// <summary>
	/// Formats this error as a MUSH-facing <c>#-1 PARSER FAILURE: ...</c> string.
	/// Uses <see cref="ExpectedTokens"/> (human-readable), <see cref="Column"/>,
	/// and <see cref="Snippet"/> to produce a compact, useful message.
	/// </summary>
	public string ToMushFailureString()
	{
		var isAtEnd = string.IsNullOrEmpty(InputText) || Column >= InputText.Length;
		var positionLabel = isAtEnd ? "end of expression" : $"position {Column}";

		var expected = ExpectedTokens is { Count: > 0 }
			? $"Expected {string.Join(" or ", ExpectedTokens)}"
			: Message;

		var detail = $"{expected} at {positionLabel}";

		if (!string.IsNullOrEmpty(Snippet) && !isAtEnd)
		{
			detail += $" (near \"{Snippet}\")";
		}

		return string.Format(ErrorMessages.Returns.ParserFailure, detail);
	}

	/// <summary>
	/// Converts this ParseError to an LSP-compatible Diagnostic.
	/// </summary>
	public Diagnostic ToDiagnostic()
	{
		var range = Range ?? new Range
		{
			Start = new Position(Line - 1, Column),
			End = new Position(Line - 1, Column + (OffendingToken?.Length ?? 1))
		};

		return new Diagnostic
		{
			Range = range,
			Severity = DiagnosticSeverity.Error,
			Source = "SharpMUSH Parser",
			Message = Message,
			OffendingToken = OffendingToken,
			ExpectedTokens = ExpectedTokens
		};
	}

	public override string ToString()
	{
		var msg = $"Parse error at line {Line}, column {Column}: {Message}";

		if (OffendingToken is not null)
		{
			msg += $"\n  Unexpected token: '{OffendingToken}'";
		}

		if (ExpectedTokens is not null && ExpectedTokens.Count > 0)
		{
			msg += $"\n  Expected one of: {string.Join(", ", ExpectedTokens)}";
		}

		return msg;
	}
}

