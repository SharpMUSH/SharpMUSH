using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Markup.Data;

public record PadTestData(
	MString input, MString padStr, int width, MModule.PadType padType, MModule.TruncationType truncType, MString expected);

public static class Pad
{
	public static IEnumerable<Func<PadTestData>> PadData()
	{
		yield return () => new(
			A.single("Test"),
			A.single(" "),
			10,
			MModule.PadType.Right,
			MModule.TruncationType.Overflow,
			A.single("Test      ")
		);

		yield return () => new(
			A.single("Test"),
			A.single(" "),
			10,
			MModule.PadType.Left,
			MModule.TruncationType.Overflow,
			A.single("      Test")
		);

		yield return () => new(
			A.single("Test"),
			A.single(" "),
			10,
			MModule.PadType.Center,
			MModule.TruncationType.Overflow,
			A.single("   Test   ")
		);

		yield return () => new(
			A.single("Example"),
			A.single("-"),
			10,
			MModule.PadType.Right,
			MModule.TruncationType.Truncate,
			A.single("Example---")
		);

		yield return () => new(
			A.single("LongInputString"),
			A.single(" "),
			10,
			MModule.PadType.Right,
			MModule.TruncationType.Truncate,
			A.single("LongInput")
		);

		yield return () => new(
			A.single("Centered"),
			A.single("."),
			15,
			MModule.PadType.Center,
			MModule.TruncationType.Overflow,
			A.single("...Centered....")
		);
		
		yield return () => new(
			A.single("Centered"),
			A.single("."),
			1,
			MModule.PadType.Center,
			MModule.TruncationType.Overflow,
			A.single("Centered")
		);

		// Regression tests for bug where pad with Truncate mode and text length == width
		// would call substring(0, 0) and return empty string instead of the original text

		// Single character at exact width with Truncate - Right padding
		yield return () => new(
			A.single("|"),
			A.single(" "),
			1,
			MModule.PadType.Right,
			MModule.TruncationType.Truncate,
			A.single("|")
		);

		// Single character at exact width with Truncate - Left padding
		yield return () => new(
			A.single("|"),
			A.single(" "),
			1,
			MModule.PadType.Left,
			MModule.TruncationType.Truncate,
			A.single("|")
		);

		// Single character at exact width with Truncate - Center padding
		yield return () => new(
			A.single("X"),
			A.single(" "),
			1,
			MModule.PadType.Center,
			MModule.TruncationType.Truncate,
			A.single("X")
		);

		// Multi-character at exact width with Truncate - Right padding
		yield return () => new(
			A.single("Test"),
			A.single(" "),
			4,
			MModule.PadType.Right,
			MModule.TruncationType.Truncate,
			A.single("Test")
		);

		// Multi-character at exact width with Truncate - Left padding
		yield return () => new(
			A.single("Test"),
			A.single(" "),
			4,
			MModule.PadType.Left,
			MModule.TruncationType.Truncate,
			A.single("Test")
		);

		// Multi-character at exact width with Truncate - Center padding
		yield return () => new(
			A.single("Word"),
			A.single("-"),
			4,
			MModule.PadType.Center,
			MModule.TruncationType.Truncate,
			A.single("Word")
		);
	}
}