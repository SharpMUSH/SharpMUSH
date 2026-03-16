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
