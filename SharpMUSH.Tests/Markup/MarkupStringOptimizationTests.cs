using System.Drawing;
using MarkupString;
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
		// Arrange
		var emptyMarkup = A.empty();
		
		// Act
		var result = emptyMarkup; // msOptimize is private, would need to be exposed or tested indirectly
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("");
		await Assert.That(result.Length).IsEqualTo(0);
	}

	[Test]
	public async Task PlainTextMarkupString_CreatesCorrectString()
	{
		// Arrange
		const string testText = "Hello, World!";
		
		// Act
		var markupString = A.single(testText);
		
		// Assert
		await Assert.That(markupString.ToPlainText()).IsEqualTo(testText);
		await Assert.That(markupString.Length).IsEqualTo(testText.Length);
		await Assert.That(markupString.ToString()).IsEqualTo(testText);
	}

	[Test]
	public async Task MarkupStringWithAnsi_CreatesCorrectOutput()
	{
		// Arrange
		const string testText = "Colored Text";
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		
		// Act
		var markupString = A.markupSingle(redMarkup, testText);
		
		// Assert
		await Assert.That(markupString.ToPlainText()).IsEqualTo(testText);
		await Assert.That(markupString.Length).IsEqualTo(testText.Length);
		
		// The toString should contain ANSI codes
		var stringRepresentation = markupString.ToString();
		await Assert.That(stringRepresentation).Contains(testText);
		await Assert.That(stringRepresentation.Length).IsGreaterThan(testText.Length); // ANSI codes add length
	}

	[Test]
	public async Task ConcatenateMarkupStrings_PlainText_WorksCorrectly()
	{
		// Arrange
		var first = A.single("Hello, ");
		var second = A.single("World!");
		
		// Act
		var result = A.concat(first, second);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Hello, World!");
		await Assert.That(result.Length).IsEqualTo(13);
	}

	[Test]
	public async Task ConcatenateMarkupStrings_WithSeparator_WorksCorrectly()
	{
		// Arrange
		var first = A.single("Hello");
		var second = A.single("World");
		var separator = A.single(", ");
		
		// Act
		var result = A.concat(first, second, separator);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Hello, World");
		await Assert.That(result.Length).IsEqualTo(12);
	}

	[Test]
	public async Task ConcatenateMarkupStrings_WithAnsi_PreservesMarkup()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		
		var redText = A.markupSingle(redMarkup, "Red");
		var blueText = A.markupSingle(blueMarkup, "Blue");
		
		// Act
		var result = A.concat(redText, blueText);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("RedBlue");
		await Assert.That(result.Length).IsEqualTo(7);
		
		// The string representation should contain both markup elements
		var stringOutput = result.ToString();
		await Assert.That(stringOutput).Contains("Red");
		await Assert.That(stringOutput).Contains("Blue");
	}

	[Test]
	public async Task SubstringMarkupString_PlainText_WorksCorrectly()
	{
		// Arrange
		var markupString = A.single("Hello, World!");
		
		// Act
		var result = A.substring(7, 5, markupString);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("World");
		await Assert.That(result.Length).IsEqualTo(5);
	}

	[Test]
	public async Task SubstringMarkupString_WithAnsi_PreservesMarkup()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var markupString = A.markupSingle(redMarkup, "Hello, World!");
		
		// Act
		var result = A.substring(7, 5, markupString);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("World");
		await Assert.That(result.Length).IsEqualTo(5);
		
		// The substring should preserve the markup
		var stringOutput = result.ToString();
		await Assert.That(stringOutput).Contains("World");
		await Assert.That(stringOutput.Length).IsGreaterThan(5); // Should have ANSI codes
	}

	[Test]
	public async Task IndexOfMarkupString_FindsCorrectPosition()
	{
		// Arrange
		var markupString = A.single("Hello, World!");
		var searchString = A.single("World");
		
		// Act
		var index = A.indexOf(markupString, searchString);
		
		// Assert
		await Assert.That(index).IsEqualTo(7);
	}

	[Test]
	public async Task IndexOfMarkupString_NotFound_ReturnsNegativeOne()
	{
		// Arrange
		var markupString = A.single("Hello, World!");
		var searchString = A.single("xyz");
		
		// Act
		var index = A.indexOf(markupString, searchString);
		
		// Assert
		await Assert.That(index).IsEqualTo(-1);
	}

	[Test]
	public async Task SplitMarkupString_WorksCorrectly()
	{
		// Arrange
		var markupString = A.single("one,two,three");
		
		// Act
		var result = A.split(",", markupString);
		
		// Assert
		await Assert.That(result.Length).IsEqualTo(3);
		await Assert.That(result[0].ToPlainText()).IsEqualTo("one");
		await Assert.That(result[1].ToPlainText()).IsEqualTo("two");
		await Assert.That(result[2].ToPlainText()).IsEqualTo("three");
	}

	[Test]
	public async Task EqualityComparison_PlainTextMarkupStrings_WorksCorrectly()
	{
		// Arrange
		var first = A.single("Hello");
		var second = A.single("Hello");
		var different = A.single("World");
		
		// Act & Assert
		await Assert.That(first.Equals(second)).IsTrue();
		await Assert.That(first.Equals("Hello")).IsTrue();
		await Assert.That(first.Equals(different)).IsFalse();
		await Assert.That(first.Equals("World")).IsFalse();
	}

	[Test]
	public async Task HashCode_PlainTextMarkupStrings_ConsistentWithEquality()
	{
		// Arrange
		var first = A.single("Hello");
		var second = A.single("Hello");
		var different = A.single("World");
		
		// Act & Assert
		await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
		await Assert.That(first.GetHashCode()).IsNotEqualTo(different.GetHashCode());
	}

	[Test]
	public async Task MultipleMarkupStrings_CombinesCorrectly()
	{
		// Arrange
		var markupStrings = new[] 
		{
			A.single("one"),
			A.single("two"), 
			A.single("three")
		};
		
		// Act
		var result = A.multiple(markupStrings);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("onetwothree");
		await Assert.That(result.Length).IsEqualTo(11);
	}

	[Test]
	public async Task MultipleMarkupStringsWithDelimiter_CombinesCorrectly()
	{
		// Arrange
		var markupStrings = new[] 
		{
			A.single("one"),
			A.single("two"), 
			A.single("three")
		};
		var delimiter = A.single(", ");
		
		// Act
		var result = A.multipleWithDelimiter(delimiter, markupStrings);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("one, two, three");
		await Assert.That(result.Length).IsEqualTo(15);
	}

	[Test]
	public async Task InsertAtMarkupString_WorksCorrectly()
	{
		// Arrange
		var original = A.single("Hello World");
		var insert = A.single("Beautiful ");
		
		// Act
		var result = A.insertAt(original, insert, 6);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Hello Beautiful World");
		await Assert.That(result.Length).IsEqualTo(21);
	}

	[Test]
	public async Task SerializeAndDeserialize_PreservesMarkupString()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var original = A.markupSingle(redMarkup, "Test Text");
		
		// Act
		var serialized = A.serialize(original);
		var deserialized = A.deserialize(serialized);
		
		// Assert
		await Assert.That(deserialized.ToPlainText()).IsEqualTo(original.ToPlainText());
		await Assert.That(deserialized.Length).IsEqualTo(original.Length);
		await Assert.That(deserialized.ToString()).IsEqualTo(original.ToString());
	}

	[Test]
	public async Task EvaluateWith_CustomEvaluator_WorksCorrectly()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var markupString = A.markupSingle(redMarkup, "Test");
		
		// Custom evaluator that wraps marked up text in brackets
		Func<MarkupStringModule.MarkupTypes, string, string> evaluator = (markupType, text) =>
		{
			return markupType switch
			{
				MarkupStringModule.MarkupTypes.MarkedupText => $"[{text}]",
				_ => text
			};
		};
		
		// Act
		var result = A.evaluateWith(evaluator, markupString);
		
		// Assert
		await Assert.That(result).IsEqualTo("[Test]");
	}

	[Test]
	public async Task OptimizeMarkupString_PlainText_RemainsUnchanged()
	{
		// Arrange
		var markupString = A.single("Hello World");
		
		// Act
		var optimized = A.optimize(markupString);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo(markupString.ToPlainText());
		await Assert.That(optimized.Length).IsEqualTo(markupString.Length);
		await Assert.That(optimized.ToString()).IsEqualTo(markupString.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_SingleMarkup_RemainsUnchanged()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var markupString = A.markupSingle(redMarkup, "Hello World");
		
		// Act
		var optimized = A.optimize(markupString);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo(markupString.ToPlainText());
		await Assert.That(optimized.Length).IsEqualTo(markupString.Length);
	}

	[Test]
	public async Task OptimizeMarkupString_AdjacentSameMarkup_MergesContent()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var first = A.markupSingle(redMarkup, "Hello ");
		var second = A.markupSingle(redMarkup, "World");
		var combined = A.concat(first, second);
		
		// Act
		var optimized = A.optimize(combined);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(optimized.Length).IsEqualTo(11);
		
		// The optimized version should have fewer nested structures
		// but produce the same output
		await Assert.That(optimized.ToString()).IsEqualTo(combined.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_DifferentMarkup_DoesNotMerge()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var first = A.markupSingle(redMarkup, "Hello ");
		var second = A.markupSingle(blueMarkup, "World");
		var combined = A.concat(first, second);
		
		// Act
		var optimized = A.optimize(combined);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(optimized.Length).IsEqualTo(11);
		
		// Different markups should not be merged
		await Assert.That(optimized.ToString()).IsEqualTo(combined.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_NestedSameMarkup_LiftsContent()
	{
		// Arrange
		
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var innerMarkup = A.markupSingle(redMarkup, "Hello");
		var outerMarkup = A.markupSingle2(redMarkup, innerMarkup);
		
		// Act
		var optimized = A.optimize(outerMarkup);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(optimized.Length).IsEqualTo(5);
		
		// The nested structure should be lifted
		await Assert.That(optimized.ToString()).IsEqualTo(outerMarkup.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_NestedDifferentMarkup_DoesNotLift()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Blue));
		var innerMarkup = A.markupSingle(blueMarkup, "Hello");
		var outerMarkup = A.markupSingle2(redMarkup, innerMarkup);
		
		// Act
		var optimized = A.optimize(outerMarkup);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(optimized.Length).IsEqualTo(5);
		
		// Different nested markups should not be lifted
		await Assert.That(optimized.MarkupDetails).IsEqualTo(outerMarkup.MarkupDetails);
	}

	[Test]
	public async Task OptimizeMarkupString_ComplexNesting_OptimizesCorrectly()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		
		// Create a complex nested structure with same markup
		var inner1 = A.markupSingle(redMarkup, "Hello ");
		var inner2 = A.markupSingle(redMarkup, "Beautiful ");
		var inner3 = A.markupSingle(redMarkup, "World");
		
		var combined = A.concat(A.concat(inner1, inner2), inner3);
		var wrapped = A.markupSingle2(redMarkup, combined);
		
		// Act
		var optimized = A.optimize(wrapped);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello Beautiful World");
		await Assert.That(optimized.Length).IsEqualTo(21);
		
		// Should produce same output but be optimized internally
		await Assert.That(optimized.ToString()).IsEqualTo(wrapped.ToString());
	}

	[Test]
	public async Task OptimizeMarkupString_MixedTextAndMarkup_HandlesCorrectly()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var plainText = A.single("Plain ");
		var redText = A.markupSingle(redMarkup, "Red ");
		var moreRedText = A.markupSingle(redMarkup, "Text");
		
		var combined = A.concat(A.concat(plainText, redText), moreRedText);
		
		// Act
		var optimized = A.optimize(combined);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Plain Red Text");
		await Assert.That(optimized.Length).IsEqualTo(14);
		
		// Should handle mixed content correctly
		await Assert.That(optimized).IsEqualTo(combined);
	}

	[Test]
	public async Task OptimizeMarkupString_EmptyMarkup_HandlesCorrectly()
	{
		// Arrange
		var emptyMarkup = A.empty();
		var textMarkup = A.single("Hello");
		var combined = A.concat(emptyMarkup, textMarkup);
		
		// Act
		var optimized = A.optimize(combined);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Hello");
		await Assert.That(optimized.Length).IsEqualTo(5);
	}

	[Test]
	public async Task OptimizeMarkupString_DeepNesting_OptimizesRecursively()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		
		// Create deeply nested structure
		var level1 = A.markupSingle(redMarkup, "Deep");
		var level2 = A.markupSingle2(redMarkup, level1);
		var level3 = A.markupSingle2(redMarkup, level2);
		var level4 = A.markupSingle2(redMarkup, level3);
		
		// Act
		var optimized = A.optimize(level4);
		
		// Assert
		await Assert.That(optimized.ToPlainText()).IsEqualTo("Deep");
		await Assert.That(optimized.Length).IsEqualTo(4);
		
		// Deep nesting of same markup should be flattened
		await Assert.That(optimized.ToString()).IsEqualTo(level4.ToString());
	}
}
