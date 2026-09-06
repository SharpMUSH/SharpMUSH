namespace MarkupString;

/// <summary>Which end(s) of a <see cref="MarkupText"/> a trim removes characters from.</summary>
public enum TrimType
{
	TrimStart,
	TrimEnd,
	TrimBoth,
}

/// <summary>Where <see cref="MarkupText.Pad"/> places the fill it adds.</summary>
public enum PadType
{
	/// <summary>Fill goes in front of the text (the text is right-aligned).</summary>
	Left,

	/// <summary>Fill goes after the text (the text is left-aligned).</summary>
	Right,

	/// <summary>Fill is split evenly either side, the extra cell going to the right.</summary>
	Center,

	/// <summary>No fill is appended; the gaps between space-separated words absorb the width.</summary>
	Full,
}

/// <summary>What a width-bounded operation does with text that is already wider than the target.</summary>
public enum TruncationType
{
	/// <summary>Cut the text down to the target display width.</summary>
	Truncate,

	/// <summary>Leave the text as it is, exceeding the target display width.</summary>
	Overflow,
}
