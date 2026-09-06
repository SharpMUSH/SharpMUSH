namespace MarkupString;

/// <summary>
/// A styled span over a <see cref="MarkupText"/>'s text, covering the half-open range
/// <c>[Start, End)</c> under the given <see cref="MarkupSet"/>.
/// </summary>
public readonly record struct Run(int Start, int Length, MarkupSet Markups)
{
	public int End => Start + Length;
}
