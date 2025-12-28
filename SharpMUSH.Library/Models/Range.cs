namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents a range in a text document expressed as (zero-based) start and end positions.
/// This is compatible with the Language Server Protocol (LSP) Range type.
/// </summary>
/// <remarks>
/// A range is comparable to a selection in an editor. Therefore, the end position is exclusive.
/// If you want to specify a range that contains a line including the line ending character(s),
/// then use an end position denoting the start of the next line.
/// </remarks>
public record Range
{
	/// <summary>
	/// The range's start position.
	/// </summary>
	public required Position Start { get; init; }

	/// <summary>
	/// The range's end position (exclusive).
	/// </summary>
	public required Position End { get; init; }

	/// <summary>
	/// Creates a new Range.
	/// </summary>
	public Range() { }

	/// <summary>
	/// Creates a new Range.
	/// </summary>
	/// <param name="start">The start position.</param>
	/// <param name="end">The end position (exclusive).</param>
	public Range(Position start, Position end)
	{
		Start = start;
		End = end;
	}

	/// <summary>
	/// Creates a new Range from line and character coordinates.
	/// </summary>
	/// <param name="startLine">Start line (zero-based).</param>
	/// <param name="startCharacter">Start character (zero-based).</param>
	/// <param name="endLine">End line (zero-based).</param>
	/// <param name="endCharacter">End character (zero-based, exclusive).</param>
	public Range(int startLine, int startCharacter, int endLine, int endCharacter)
	{
		Start = new Position(startLine, startCharacter);
		End = new Position(endLine, endCharacter);
	}

	/// <summary>
	/// Returns true if the range is empty (start equals end).
	/// </summary>
	public bool IsEmpty => Start.Line == End.Line && Start.Character == End.Character;

	/// <summary>
	/// Returns true if the range is a single line.
	/// </summary>
	public bool IsSingleLine => Start.Line == End.Line;

	/// <summary>
	/// Returns true if this range contains the given position.
	/// </summary>
	/// <param name="position">The position to check.</param>
	/// <returns>True if the position is within this range.</returns>
	public bool Contains(Position position)
		=> position.IsAfterOrEqual(Start) && position.IsBefore(End);

	/// <summary>
	/// Returns true if this range contains the other range.
	/// </summary>
	/// <param name="other">The range to check.</param>
	/// <returns>True if the other range is within this range.</returns>
	public bool Contains(Range other)
		=> other.Start.IsAfterOrEqual(Start) && other.End.IsBeforeOrEqual(End);

	/// <summary>
	/// Returns true if this range intersects with the other range.
	/// </summary>
	public bool Intersects(Range other)
		=> Start.IsBefore(other.End) && other.Start.IsBefore(End);

	public override string ToString() => $"[{Start} - {End})";
}
