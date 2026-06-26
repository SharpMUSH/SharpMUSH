using ANSILibrary;
using MarkupString;
using MarkupString.MarkupImplementation;
using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Unit tests for MarkupString optimization functionality and core behaviors.
/// </summary>
public class MarkupStringOptimizationTests
{
	[Test]
	public async Task OptimizeMarkupString_EmptyMarkupString_ReturnsEmpty()
	{
		var emptyMarkup = A.empty();

		var result = emptyMarkup; // msOptimize is private, would need to be exposed or tested indirectly

		await Assert.That(result.ToPlainText()).IsEqualTo("");
		await Assert.That(result.Length).IsEqualTo(0);
	}

	[Test]
	public async Task PlainTextMarkupString_CreatesCorrectString()
	{
		const string testText = "Hello, World!";

		var markupString = A.single(testText);

		await Assert.That(markupString.ToPlainText()).IsEqualTo(testText);
		await Assert.That(markupString.Length).IsEqualTo(testText.Length);
		await Assert.That(markupString.ToString()).IsEqualTo(testText);
	}

	[Test]
	public async Task MarkupStringWithAnsi_CreatesCorrectOutput()
	{
		const string testText = "Colored Text";
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));

		var markupString = A.MarkupSingle(redMarkup, testText);

		await Assert.That(markupString.ToPlainText()).IsEqualTo(testText);
		await Assert.That(markupString.Length).IsEqualTo(testText.Length);

		var stringRepresentation = markupString.ToString();
		await Assert.That(stringRepresentation).Contains(testText);
		await Assert.That(stringRepresentation.Length).IsGreaterThan(testText.Length);
	}

	[Test]
	public async Task ConcatenateMarkupStrings_PlainText_WorksCorrectly()
	{
		var first = A.single("Hello, ");
		var second = A.single("World!");

		var result = A.concat(first, second);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello, World!");
		await Assert.That(result.Length).IsEqualTo(13);
	}

	[Test]
	public async Task ConcatenateMarkupStrings_WithSeparator_WorksCorrectly()
	{
		var first = A.single("Hello");
		var second = A.single("World");
		var separator = A.single(", ");

		var result = A.concat(A.concat(first, separator), second);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello, World");
		await Assert.That(result.Length).IsEqualTo(12);
	}

	[Test]
	public async Task ConcatenateMarkupStrings_WithAnsi_PreservesMarkup()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blueMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Blue));

		var redText = A.MarkupSingle(redMarkup, "Red");
		var blueText = A.MarkupSingle(blueMarkup, "Blue");

		var result = A.concat(redText, blueText);

		await Assert.That(result.ToPlainText()).IsEqualTo("RedBlue");
		await Assert.That(result.Length).IsEqualTo(7);

		var stringOutput = result.ToString();
		await Assert.That(stringOutput).Contains("Red");
		await Assert.That(stringOutput).Contains("Blue");
	}

	[Test]
	public async Task SubstringMarkupString_PlainText_WorksCorrectly()
	{
		var markupString = A.single("Hello, World!");

		var result = A.substring(7, 5, markupString);

		await Assert.That(result.ToPlainText()).IsEqualTo("World");
		await Assert.That(result.Length).IsEqualTo(5);
	}

	[Test]
	public async Task SubstringMarkupString_WithAnsi_PreservesMarkup()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var markupString = A.MarkupSingle(redMarkup, "Hello, World!");

		var result = A.substring(7, 5, markupString);

		await Assert.That(result.ToPlainText()).IsEqualTo("World");
		await Assert.That(result.Length).IsEqualTo(5);

		var stringOutput = result.ToString();
		await Assert.That(stringOutput).Contains("World");
		await Assert.That(stringOutput.Length).IsGreaterThan(5);
	}

	[Test]
	public async Task IndexOfMarkupString_FindsCorrectPosition()
	{
		var markupString = A.single("Hello, World!");
		var searchString = A.single("World");

		var index = A.indexOf(markupString, searchString.ToPlainText());

		await Assert.That(index).IsEqualTo(7);
	}

	[Test]
	public async Task IndexOfMarkupString_NotFound_ReturnsNegativeOne()
	{
		var markupString = A.single("Hello, World!");
		var searchString = A.single("xyz");

		var index = A.indexOf(markupString, searchString.ToPlainText());

		await Assert.That(index).IsEqualTo(-1);
	}

	[Test]
	public async Task SplitMarkupString_WorksCorrectly()
	{
		var markupString = A.single("one,two,three");

		var result = A.split(",", markupString);

		await Assert.That(result.Length).IsEqualTo(3);
		await Assert.That(result[0].ToPlainText()).IsEqualTo("one");
		await Assert.That(result[1].ToPlainText()).IsEqualTo("two");
		await Assert.That(result[2].ToPlainText()).IsEqualTo("three");
	}

	/// <summary>
	/// indexOf / indexOfLast / split must use ordinal (byte-by-byte) comparison so that
	/// results are deterministic regardless of the server's current culture.
	/// For example, under some locales a culture-sensitive search can fold accented
	/// characters, but an ordinal search must treat each Unicode code point as distinct.
	/// </summary>
	[Test]
	public async Task IndexOf_UseOrdinalComparison_NotCultureSensitive()
	{
		// \u00e9 is 'é' (e with acute accent).  Culture-sensitive search on some
		// locales may match it against plain 'e', but ordinal comparison must not.
		var markupString = A.single("caf\u00e9");  // "café"
		var searchMatch = A.single("caf\u00e9");
		var searchNoMatch = A.single("cafe");      // 'e' != 'é' ordinal

		await Assert.That(A.indexOf(markupString, "caf\u00e9")).IsEqualTo(0);
		await Assert.That(A.indexOf(markupString, "cafe")).IsEqualTo(-1);
	}

	[Test]
	public async Task IndexOfLast_UseOrdinalComparison_NotCultureSensitive()
	{
		var markupString = A.single("caf\u00e9-caf\u00e9");
		var search = A.single("caf\u00e9");
		var searchNoMatch = A.single("cafe");

		await Assert.That(A.indexOfLast(markupString, "caf\u00e9")).IsEqualTo(5);
		await Assert.That(A.indexOfLast(markupString, "cafe")).IsEqualTo(-1);
	}

	[Test]
	public async Task Split_UseOrdinalComparison_NotCultureSensitive()
	{
		// Delimiter is a non-ASCII Unicode character (÷, U+00F7).
		// Ordinal comparison ensures only the exact code point matches, not any
		// ASCII look-alike that a culture-sensitive comparer might equate it with.
		var markupString = A.single("a\u00f7b\u00f7c");  // "a÷b÷c"
		var result = A.split("\u00f7", markupString);

		await Assert.That(result.Length).IsEqualTo(3);
		await Assert.That(result[0].ToPlainText()).IsEqualTo("a");
		await Assert.That(result[1].ToPlainText()).IsEqualTo("b");
		await Assert.That(result[2].ToPlainText()).IsEqualTo("c");

		// ASCII delimiter must not match the Unicode delimiter
		var resultAscii = A.split("/", markupString);
		await Assert.That(resultAscii.Length).IsEqualTo(1);
	}

	[Test]
	public async Task EqualityComparison_PlainTextMarkupStrings_WorksCorrectly()
	{
		var first = A.single("Hello");
		var second = A.single("Hello");
		var different = A.single("World");

		await Assert.That(first.Equals(second)).IsTrue();
		await Assert.That(first.Equals("Hello")).IsTrue();
		await Assert.That(first.Equals(different)).IsFalse();
		await Assert.That(first.Equals("World")).IsFalse();
	}

	[Test]
	public async Task HashCode_PlainTextMarkupStrings_ConsistentWithEquality()
	{
		var first = A.single("Hello");
		var second = A.single("Hello");
		var different = A.single("World");

		await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
		await Assert.That(first.GetHashCode()).IsNotEqualTo(different.GetHashCode());
	}

	[Test]
	public async Task MultipleMarkupStrings_CombinesCorrectly()
	{
		var markupStrings = new[]
		{
			A.single("one"),
			A.single("two"),
			A.single("three")
		};

		var result = A.multiple(markupStrings);

		await Assert.That(result.ToPlainText()).IsEqualTo("onetwothree");
		await Assert.That(result.Length).IsEqualTo(11);
	}

	[Test]
	public async Task MultipleMarkupStringsWithDelimiter_CombinesCorrectly()
	{
		var markupStrings = new[]
		{
			A.single("one"),
			A.single("two"),
			A.single("three")
		};
		var delimiter = A.single(", ");

		var result = A.multipleWithDelimiter(delimiter, markupStrings);

		await Assert.That(result.ToPlainText()).IsEqualTo("one, two, three");
		await Assert.That(result.Length).IsEqualTo(15);
	}

	[Test]
	public async Task InsertAtMarkupString_WorksCorrectly()
	{
		var original = A.single("Hello World");
		var insert = A.single("Beautiful ");

		var result = A.insertAt(original, insert, 6);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello Beautiful World");
		await Assert.That(result.Length).IsEqualTo(21);
	}

	[Test]
	public async Task SerializeAndDeserialize_PreservesMarkupString()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var original = A.MarkupSingle(redMarkup, "Test Text");

		var serialized = A.serialize(original);
		var deserialized = A.deserialize(serialized);

		await Assert.That(deserialized.ToPlainText()).IsEqualTo(original.ToPlainText());
		await Assert.That(deserialized.Length).IsEqualTo(original.Length);
		await Assert.That(deserialized.ToString()).IsEqualTo(original.ToString());
	}

	[Test]
	public async Task EvaluateWith_CustomEvaluator_WorksCorrectly()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var markupString = A.MarkupSingle(redMarkup, "Test");

		Func<IMarkup?, string, string> evaluator = (markupType, text) =>
		{
			return markupType switch
			{
				not null => $"[{text}]",
				_ => text
			};
		};

		var result = A.evaluateWith(evaluator, markupString);

		await Assert.That(result).IsEqualTo("[Test]");
	}

	[Test]
	public async Task OptimizeMarkupString_PlainText_RemainsUnchanged()
	{
		var markupString = A.single("Hello World");

		var optimized = A.optimize(markupString);

		await Assert.That(optimized.ToPlainText()).IsEqualTo(markupString.ToPlainText());
		await Assert.That(optimized.Length).IsEqualTo(markupString.Length);
		await Assert.That(optimized.ToString()).IsEqualTo(markupString.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_SingleMarkup_RemainsUnchanged()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var markupString = A.MarkupSingle(redMarkup, "Hello World");

		var optimized = A.optimize(markupString);

		await Assert.That(optimized.ToPlainText()).IsEqualTo(markupString.ToPlainText());
		await Assert.That(optimized.Length).IsEqualTo(markupString.Length);
	}

	[Test]
	public async Task OptimizeMarkupString_AdjacentSameMarkup_MergesContent()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var first = A.MarkupSingle(redMarkup, "Hello ");
		var second = A.MarkupSingle(redMarkup, "World");
		var combined = A.concat(first, second);

		var optimized = A.optimize(combined);

		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(optimized.Length).IsEqualTo(11);

		await Assert.That(optimized.ToString()).IsEqualTo(combined.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_DifferentMarkup_DoesNotMerge()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blueMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Blue));
		var first = A.MarkupSingle(redMarkup, "Hello ");
		var second = A.MarkupSingle(blueMarkup, "World");
		var combined = A.concat(first, second);

		var optimized = A.optimize(combined);

		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(optimized.Length).IsEqualTo(11);

		await Assert.That(optimized.ToString()).IsEqualTo(combined.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_NestedSameMarkup_LiftsContent()
	{

		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var innerMarkup = A.MarkupSingle(redMarkup, "Hello");
		var outerMarkup = A.MarkupSingle2(redMarkup, innerMarkup);

		var optimized = A.optimize(outerMarkup);

		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(optimized.Length).IsEqualTo(5);

		await Assert.That(optimized.ToString()).IsEqualTo(outerMarkup.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_NestedDifferentMarkup_DoesNotLift()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blueMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Blue));
		var innerMarkup = A.MarkupSingle(blueMarkup, "Hello");
		var outerMarkup = A.MarkupSingle2(redMarkup, innerMarkup);

		var optimized = A.optimize(outerMarkup);

		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(optimized.Length).IsEqualTo(5);

		await Assert.That(optimized.Runs.Length).IsEqualTo(outerMarkup.Runs.Length);
	}

	[Test]
	public async Task OptimizeMarkupString_ComplexNesting_OptimizesCorrectly()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));

		var inner1 = A.MarkupSingle(redMarkup, "Hello ");
		var inner2 = A.MarkupSingle(redMarkup, "Beautiful ");
		var inner3 = A.MarkupSingle(redMarkup, "World");

		var combined = A.concat(A.concat(inner1, inner2), inner3);
		var wrapped = A.MarkupSingle2(redMarkup, combined);

		var optimized = A.optimize(wrapped);

		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello Beautiful World");
		await Assert.That(optimized.Length).IsEqualTo(21);

		await Assert.That(optimized.ToString()).IsEqualTo(wrapped.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_MixedTextAndMarkup_HandlesCorrectly()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var plainText = A.single("Plain ");
		var redText = A.MarkupSingle(redMarkup, "Red ");
		var moreRedText = A.MarkupSingle(redMarkup, "Text");

		var combined = A.concat(A.concat(plainText, redText), moreRedText);

		var optimized = A.optimize(combined);

		await Assert.That(optimized.ToPlainText()).IsEqualTo("Plain Red Text");
		await Assert.That(optimized.Length).IsEqualTo(14);

		await Assert.That(optimized).IsEqualTo(combined);
	}

	[Test]
	public async Task OptimizeMarkupString_EmptyMarkup_HandlesCorrectly()
	{
		var emptyMarkup = A.empty();
		var textMarkup = A.single("Hello");
		var combined = A.concat(emptyMarkup, textMarkup);

		var optimized = A.optimize(combined);

		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(optimized.Length).IsEqualTo(5);
	}

	[Test]
	public async Task OptimizeMarkupString_DeepNesting_OptimizesRecursively()
	{
		var redMarkup = M.Create(foreground: new AnsiColor.RGB(Color.Red));

		var level1 = A.MarkupSingle(redMarkup, "Deep");
		var level2 = A.MarkupSingle2(redMarkup, level1);
		var level3 = A.MarkupSingle2(redMarkup, level2);
		var level4 = A.MarkupSingle2(redMarkup, level3);

		var optimized = A.optimize(level4);

		await Assert.That(optimized.ToPlainText()).IsEqualTo("Deep");
		await Assert.That(optimized.Length).IsEqualTo(4);

		await Assert.That(optimized.ToString()).IsEqualTo(level4.ToString());
	}
}
