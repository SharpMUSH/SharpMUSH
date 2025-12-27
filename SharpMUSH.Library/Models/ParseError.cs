namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents a parsing error with detailed information about what went wrong and where.
/// </summary>
public record ParseError
{
	/// <summary>
	/// The line number where the error occurred (1-based).
	/// </summary>
	public int Line { get; init; }

	/// <summary>
	/// The column position where the error occurred (0-based).
	/// </summary>
	public int Column { get; init; }

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
