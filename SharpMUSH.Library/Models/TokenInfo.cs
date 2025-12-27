namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents information about a token for syntax highlighting purposes.
/// </summary>
public record TokenInfo
{
	/// <summary>
	/// The type of token (e.g., "FUNCHAR", "OBRACK", "OTHER", etc.)
	/// </summary>
	public required string Type { get; init; }

	/// <summary>
	/// The start position of the token in the input (0-based).
	/// </summary>
	public int StartIndex { get; init; }

	/// <summary>
	/// The end position of the token in the input (0-based, inclusive).
	/// </summary>
	public int EndIndex { get; init; }

	/// <summary>
	/// The text content of the token.
	/// </summary>
	public required string Text { get; init; }

	/// <summary>
	/// The line number where the token starts (1-based).
	/// </summary>
	public int Line { get; init; }

	/// <summary>
	/// The column position where the token starts (0-based).
	/// </summary>
	public int Column { get; init; }

	/// <summary>
	/// The channel the token is on (usually 0 for DEFAULT_CHANNEL).
	/// </summary>
	public int Channel { get; init; }

	/// <summary>
	/// Gets the length of the token.
	/// </summary>
	public int Length => EndIndex - StartIndex + 1;
}
