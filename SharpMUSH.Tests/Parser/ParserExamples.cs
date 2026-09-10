using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Manual testing examples for parser error explanations and syntax highlighting.
/// Run these interactively to see the features in action.
/// </summary>
public class ParserExamples
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Example_ValidateInput_WithErrors()
	{
		TestDiagnostics.WriteLine("\n=== Parser Error Explanation Example ===\n");

		var testCases = new[]
		{
			"add(1,2)",
			"add(1,2",
			"test[unclosed",
			"func{missing",
			"nested(call[test)",
		};

		foreach (var testCase in testCases)
		{
			TestDiagnostics.WriteLine($"Input: '{testCase}'");
			var errors = Parser.ValidateAndGetErrors(MarkupText.Plain(testCase), ParseType.Function);

			if (errors.Count == 0)
			{
				TestDiagnostics.WriteLine("  ✓ Valid - No errors\n");
			}
			else
			{
				TestDiagnostics.WriteLine($"  ✗ {errors.Count} error(s) found:");
				foreach (var error in errors)
				{
					TestDiagnostics.WriteLine($"    {error}");
				}
				TestDiagnostics.WriteLine();
			}
		}

		await Assert.That(testCases.Length).IsGreaterThan(0);
	}

	[Test]
	public async Task Example_TokenizeForSyntaxHighlighting()
	{
		TestDiagnostics.WriteLine("\n=== Syntax Highlighting Example ===\n");

		var input = "add(1,2)[sub(5,3)]{test}%0";
		TestDiagnostics.WriteLine($"Input: '{input}'\n");
		TestDiagnostics.WriteLine("Tokens:");

		var tokens = Parser.Tokenize(MarkupText.Plain(input));

		foreach (var token in tokens)
		{
			var displayType = token.Type.PadRight(15);
			var displayText = token.Text.Length > 20
				? token.Text[..17] + "..."
				: token.Text;

			TestDiagnostics.WriteLine($"  [{displayType}] '{displayText}' at position {token.StartIndex}-{token.EndIndex}");
		}

		TestDiagnostics.WriteLine($"\nTotal tokens: {tokens.Count}");

		await Assert.That(tokens.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task Example_CompareTokenTypes()
	{
		TestDiagnostics.WriteLine("\n=== Token Type Comparison ===\n");

		var examples = new Dictionary<string, string>
		{
			["Function"] = "add(1,2)",
			["Bracket substitution"] = "test[value]",
			["Brace grouping"] = "some{text}",
			["Percent substitution"] = "%0 and %1",
			["Escape sequence"] = "\\n and \\t",
			["Complex"] = "add(x,[get(#1)])%0"
		};

		foreach (var (description, input) in examples)
		{
			TestDiagnostics.WriteLine($"{description}: '{input}'");
			var tokens = Parser.Tokenize(MarkupText.Plain(input));
			var tokenTypes = tokens.Select(t => t.Type).Distinct().ToList();
			TestDiagnostics.WriteLine($"  Token types: {string.Join(", ", tokenTypes)}\n");
		}

		await Assert.That(examples.Count).IsGreaterThan(0);
	}
}
