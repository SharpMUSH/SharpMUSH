using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class SyntaxHighlightingTests : TestClassFactory
{
	private IMUSHCodeParser Parser => FunctionParser;

	[Test]
	public async Task SimpleText_ShouldTokenize()
	{
		var tokens = Parser.Tokenize(MModule.single("hello world"));
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have at least one token
		var firstToken = tokens[0];
		await Assert.That(firstToken.Text).IsNotEmpty();
		await Assert.That(firstToken.Type).IsNotEmpty();
	}

	[Test]
	public async Task FunctionCall_ShouldIdentifyTokens()
	{
		var tokens = Parser.Tokenize(MModule.single("add(1,2)"));
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have function name, parentheses, arguments, comma
		var functionToken = tokens.FirstOrDefault(t => t.Type == "FUNCHAR");
		await Assert.That(functionToken).IsNotNull();
		await Assert.That(functionToken!.Text).Contains("add(");
	}

	[Test]
	public async Task Brackets_ShouldBeIdentified()
	{
		var tokens = Parser.Tokenize(MModule.single("test[inner]"));
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have opening and closing brackets
		var openBracket = tokens.FirstOrDefault(t => t.Type == "OBRACK");
		var closeBracket = tokens.FirstOrDefault(t => t.Type == "CBRACK");
		
		await Assert.That(openBracket).IsNotNull();
		await Assert.That(closeBracket).IsNotNull();
	}

	[Test]
	public async Task Braces_ShouldBeIdentified()
	{
		var tokens = Parser.Tokenize(MModule.single("test{inner}"));
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have opening and closing braces
		var openBrace = tokens.FirstOrDefault(t => t.Type == "OBRACE");
		var closeBrace = tokens.FirstOrDefault(t => t.Type == "CBRACE");
		
		await Assert.That(openBrace).IsNotNull();
		await Assert.That(closeBrace).IsNotNull();
	}

	[Test]
	public async Task TokenPositions_ShouldBeCorrect()
	{
		var input = "abc[def]";
		var tokens = Parser.Tokenize(MModule.single(input));
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Each token should have valid positions
		foreach (var token in tokens)
		{
			await Assert.That(token.StartIndex).IsGreaterThanOrEqualTo(0);
			await Assert.That(token.EndIndex).IsGreaterThanOrEqualTo(token.StartIndex);
			await Assert.That(token.Length).IsGreaterThan(0);
		}
	}

	[Test]
	public async Task Substitution_ShouldBeIdentified()
	{
		var tokens = Parser.Tokenize(MModule.single("%0 test"));
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have percent sign token
		var percentToken = tokens.FirstOrDefault(t => t.Type == "PERCENT");
		await Assert.That(percentToken).IsNotNull();
	}

	[Test]
	public async Task Escape_ShouldBeIdentified()
	{
		var tokens = Parser.Tokenize(MModule.single("\\n test"));
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have escape token
		var escapeToken = tokens.FirstOrDefault(t => t.Type == "ESCAPE");
		await Assert.That(escapeToken).IsNotNull();
	}

	[Test]
	public async Task ComplexInput_ShouldTokenizeCompletely()
	{
		var input = "add(1,2)[sub(3,4)]{test}%0";
		var tokens = Parser.Tokenize(MModule.single(input));
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have multiple different token types
		var tokenTypes = tokens.Select(t => t.Type).Distinct().ToList();
		await Assert.That(tokenTypes.Count).IsGreaterThan(3);
	}

	[Test]
	public async Task EmptyInput_ShouldReturnEmptyList()
	{
		var tokens = Parser.Tokenize(MModule.single(""));
		
		await Assert.That(tokens).IsEmpty();
	}

	[Test]
	public async Task TokenText_ShouldMatchInput()
	{
		var input = "hello";
		var tokens = Parser.Tokenize(MModule.single(input));
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Reconstruct input from tokens
		var reconstructed = string.Concat(tokens.Select(t => t.Text));
		await Assert.That(reconstructed).IsEqualTo(input);
	}
}
