using SharpMUSH.Server.Helpers;

namespace SharpMUSH.Tests.Server.Helpers;

public class LogSanitizerTests
{
	[Test]
	public async Task Sanitize_NullInput_ReturnsNullPlaceholder()
	{
		string? input = null;
		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("[null]");
	}

	[Test]
	public async Task Sanitize_EmptyString_ReturnsEmptyPlaceholder()
	{
		var result = LogSanitizer.Sanitize(string.Empty);

		await Assert.That(result).IsEqualTo("[empty]");
	}

	[Test]
	public async Task Sanitize_WhitespaceOnly_ReturnsEmptyPlaceholder()
	{
		var result = LogSanitizer.Sanitize("   \t  ");

		await Assert.That(result).IsEqualTo("[empty]");
	}

	[Test]
	public async Task Sanitize_NormalText_ReturnsUnchanged()
	{
		var input = "Hello World";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("Hello World");
	}

	[Test]
	public async Task Sanitize_NewlineInInput_EscapesNewline()
	{
		var input = "Line1\nLine2";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("Line1\\nLine2");
	}

	[Test]
	public async Task Sanitize_CarriageReturnInInput_EscapesCarriageReturn()
	{
		var input = "Line1\rLine2";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("Line1\\nLine2");
	}

	[Test]
	public async Task Sanitize_WindowsNewlineInInput_EscapesBoth()
	{
		var input = "Line1\r\nLine2";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("Line1\\n\\nLine2");
	}

	[Test]
	public async Task Sanitize_TabInInput_ReplacedWithSpace()
	{
		var input = "Column1\tColumn2";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("Column1 Column2");
	}

	[Test]
	public async Task Sanitize_ControlCharactersInInput_Removed()
	{
		var input = $"Text{(char)0}with{(char)1}control{(char)2}chars";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("Textwithcontrolchars");
	}

	[Test]
	public async Task Sanitize_LongInput_TruncatesWithEllipsis()
	{
		var input = new string('A', 250);

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).StartsWith(new string('A', 200));
		await Assert.That(result).EndsWith("... [truncated]");
		await Assert.That(result.Length).IsEqualTo(215);
	}

	[Test]
	public async Task Sanitize_ExactlyMaxLength_NoTruncation()
	{
		var input = new string('A', 200);

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo(input);
		await Assert.That(result).DoesNotContain("truncated");
	}

	[Test]
	public async Task Sanitize_LogInjectionAttempt_PreventsInjection()
	{
		var input = "normal\n2024-01-30 12:00:00 [ERROR] Fake admin login from attacker";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("normal\\n2024-01-30 12:00:00 [ERROR] Fake admin login from attacker");
		await Assert.That(result).DoesNotContain("\n");
	}

	[Test]
	public async Task Sanitize_UnicodeCharacters_Preserved()
	{
		var input = "Hello 世界 🌍";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("Hello 世界 🌍");
	}

	[Test]
	public async Task Sanitize_SpecialCharacters_Preserved()
	{
		var input = "Price: $100.50 (50% off!)";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("Price: $100.50 (50% off!)");
	}

	[Test]
	public async Task Sanitize_MultipleInputs_SanitizesAll()
	{
		var input1 = "Value1\nInjection";
		var input2 = "Value2\tSpaced";
		string? input3 = null;

		var results = LogSanitizer.Sanitize(input1, input2, input3);

		await Assert.That(results).Count().IsEqualTo(3);
		await Assert.That(results[0]).IsEqualTo("Value1\\nInjection");
		await Assert.That(results[1]).IsEqualTo("Value2 Spaced");
		await Assert.That(results[2]).IsEqualTo("[null]");
	}

	[Test]
	public async Task Sanitize_EmptyArray_ReturnsEmptyArray()
	{
		var results = LogSanitizer.Sanitize();

		await Assert.That(results).IsEmpty();
	}

	[Test]
	public async Task Sanitize_MixedControlAndPrintable_FiltersCorrectly()
	{
		var input = "Start\x1B[31mRed Text\x1B[0mEnd";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("Start[31mRed Text[0mEnd");
	}

	[Test]
	public async Task Sanitize_PathTraversal_Sanitized()
	{
		var input = "../../etc/passwd\n/root/.ssh/id_rsa";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).DoesNotContain("\n");
		await Assert.That(result).Contains("\\n");
	}

	[Test]
	public async Task Sanitize_SqlInjection_NoSpecialHandling()
	{
		var input = "'; DROP TABLE users; --";

		var result = LogSanitizer.Sanitize(input);

		await Assert.That(result).IsEqualTo("'; DROP TABLE users; --");
	}
}
