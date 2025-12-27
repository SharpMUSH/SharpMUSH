using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Manual testing examples for parser error explanations and syntax highlighting.
/// Run these interactively to see the features in action.
/// </summary>
public class ParserExamples
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Example_ValidateInput_WithErrors()
	{
		Console.WriteLine("\n=== Parser Error Explanation Example ===\n");
		
		var testCases = new[]
		{
			"add(1,2)",           // Valid
			"add(1,2",            // Missing closing paren
			"test[unclosed",      // Missing closing bracket  
			"func{missing",       // Missing closing brace
			"nested(call[test)",  // Mismatched delimiters
		};

		foreach (var testCase in testCases)
		{
			Console.WriteLine($"Input: '{testCase}'");
			var errors = Parser.ValidateAndGetErrors(MModule.single(testCase), ParseType.Function);
			
			if (errors.Count == 0)
			{
				Console.WriteLine("  ✓ Valid - No errors\n");
			}
			else
			{
				Console.WriteLine($"  ✗ {errors.Count} error(s) found:");
				foreach (var error in errors)
				{
					Console.WriteLine($"    {error}");
				}
				Console.WriteLine();
			}
		}

		// Just verify we got through the test
		await Assert.That(testCases.Length).IsGreaterThan(0);
	}

	[Test]
	public async Task Example_TokenizeForSyntaxHighlighting()
	{
		Console.WriteLine("\n=== Syntax Highlighting Example ===\n");
		
		var input = "add(1,2)[sub(5,3)]{test}%0";
		Console.WriteLine($"Input: '{input}'\n");
		Console.WriteLine("Tokens:");
		
		var tokens = Parser.Tokenize(MModule.single(input));
		
		foreach (var token in tokens)
		{
			var displayType = token.Type.PadRight(15);
			var displayText = token.Text.Length > 20 
				? token.Text[..17] + "..." 
				: token.Text;
			
			Console.WriteLine($"  [{displayType}] '{displayText}' at position {token.StartIndex}-{token.EndIndex}");
		}

		Console.WriteLine($"\nTotal tokens: {tokens.Count}");
		
		await Assert.That(tokens.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task Example_CompareTokenTypes()
	{
		Console.WriteLine("\n=== Token Type Comparison ===\n");
		
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
			Console.WriteLine($"{description}: '{input}'");
			var tokens = Parser.Tokenize(MModule.single(input));
			var tokenTypes = tokens.Select(t => t.Type).Distinct().ToList();
			Console.WriteLine($"  Token types: {string.Join(", ", tokenTypes)}\n");
		}

		await Assert.That(examples.Count).IsGreaterThan(0);
	}
}
