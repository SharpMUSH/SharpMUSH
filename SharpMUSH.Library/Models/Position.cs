namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents a position in a text document expressed as zero-based line and zero-based character offset.
/// This is compatible with the Language Server Protocol (LSP) Position type.
/// </summary>
/// <remarks>
/// A position is between two characters like an 'insert' cursor in an editor.
/// Line and Character are 0-based.
/// </remarks>
public record Position
{
	/// <summary>
	/// Line position in a document (zero-based).
	/// </summary>
	public int Line { get; init; }

	/// <summary>
	/// Character offset on a line in a document (zero-based).
	/// The meaning of this offset is determined by the negotiated UTF encoding.
	/// For LSP, this is typically UTF-16 code units.
	/// </summary>
	public int Character { get; init; }

	/// <summary>
	/// Creates a new Position.
	/// </summary>
	/// <param name="line">Zero-based line number.</param>
	/// <param name="character">Zero-based character offset.</param>
	public Position(int line, int character)
	{
		Line = line;
		Character = character;
	}

	/// <summary>
	/// Returns true if this position is before the other position.
	/// </summary>
	public bool IsBefore(Position other)
		=> Line < other.Line || (Line == other.Line && Character < other.Character);

	/// <summary>
	/// Returns true if this position is after the other position.
	/// </summary>
	public bool IsAfter(Position other)
		=> Line > other.Line || (Line == other.Line && Character > other.Character);

	/// <summary>
	/// Returns true if this position is at or before the other position.
	/// </summary>
	public bool IsBeforeOrEqual(Position other)
		=> Line < other.Line || (Line == other.Line && Character <= other.Character);

	/// <summary>
	/// Returns true if this position is at or after the other position.
	/// </summary>
	public bool IsAfterOrEqual(Position other)
		=> Line > other.Line || (Line == other.Line && Character >= other.Character);

	public override string ToString() => $"({Line}, {Character})";
}
