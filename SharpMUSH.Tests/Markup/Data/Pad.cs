using ANSILibrary;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Markup.Data;

public static class Pad
{
	public static IEnumerable<(MString input, MString padStr, int width, A.PadType padType, A.TruncationType truncType,
		MString expected)> PadData()
	{
		yield return (
			A.single("Test"),
			A.single(" "),
			10,
			A.PadType.Right,
			A.TruncationType.Overflow,
			A.single("Test      ")
		);

		yield return (
			A.single("Test"),
			A.single(" "),
			10,
			A.PadType.Left,
			A.TruncationType.Overflow,
			A.single("      Test")
		);

		yield return (
			A.single("Test"),
			A.single(" "),
			10,
			A.PadType.Center,
			A.TruncationType.Overflow,
			A.single("   Test   ")
		);

		yield return (
			A.single("Example"),
			A.single("-"),
			10,
			A.PadType.Right,
			A.TruncationType.Truncate,
			A.single("Example---")
		);

		yield return (
			A.single("LongInputString"),
			A.single(" "),
			10,
			A.PadType.Right,
			A.TruncationType.Truncate,
			A.single("LongInput")
		);

		yield return (
			A.single("Centered"),
			A.single("."),
			15,
			A.PadType.Center,
			A.TruncationType.Overflow,
			A.single("...Centered....")
		);
	}
}