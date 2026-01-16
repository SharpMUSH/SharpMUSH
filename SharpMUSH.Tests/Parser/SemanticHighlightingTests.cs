using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class SemanticHighlightingTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.FunctionParser;

	[Test]
	public async Task GetSemanticTokens_SimpleFunction_ReturnsTokens()
	{
		var tokens = Parser.GetSemanticTokens(MModule.single("add(1,2)"), ParseType.Function);
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have function token
		var functionToken = tokens.FirstOrDefault(t => t.TokenType == SemanticTokenType.Function);
		await Assert.That(functionToken).IsNotNull();
	}

	[Test]
	public async Task GetSemanticTokens_ObjectReference_IdentifiesCorrectly()
	{
		var tokens = Parser.GetSemanticTokens(MModule.single("%#"), ParseType.Function);
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have object reference token
		var objToken = tokens.FirstOrDefault(t => t.TokenType == SemanticTokenType.ObjectReference);
		await Assert.That(objToken).IsNotNull();
	}

	[Test]
	public async Task GetSemanticTokens_Substitution_IdentifiesCorrectly()
	{
		var tokens = Parser.GetSemanticTokens(MModule.single("%0 test %1"), ParseType.Function);
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have substitution tokens
		var subTokens = tokens.Where(t => t.TokenType == SemanticTokenType.Substitution 
		                                   || t.TokenType == SemanticTokenType.Register).ToList();
		await Assert.That(subTokens.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task GetSemanticTokens_BracketSubstitution_IdentifiesCorrectly()
	{
		var tokens = Parser.GetSemanticTokens(MModule.single("test[value]"), ParseType.Function);
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have bracket tokens
		var bracketTokens = tokens.Where(t => t.TokenType == SemanticTokenType.BracketSubstitution).ToList();
		await Assert.That(bracketTokens.Count).IsGreaterThanOrEqualTo(2); // At least '[' and ']'
	}

	[Test]
	public async Task GetSemanticTokens_ComplexExpression_IdentifiesMultipleTypes()
	{
		var input = "add(1,[get(%#)])%0";
		var tokens = Parser.GetSemanticTokens(MModule.single(input), ParseType.Function);
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have multiple token types
		var tokenTypes = tokens.Select(t => t.TokenType).Distinct().ToList();
		await Assert.That(tokenTypes.Count).IsGreaterThan(2);
	}

	[Test]
	public async Task GetSemanticTokens_HasRanges()
	{
		var tokens = Parser.GetSemanticTokens(MModule.single("test[abc]"), ParseType.Function);
		
		await Assert.That(tokens).IsNotEmpty();
		
		// All tokens should have valid ranges
		foreach (var token in tokens)
		{
			await Assert.That(token.Range).IsNotNull();
			await Assert.That(token.Range.Start).IsNotNull();
			await Assert.That(token.Range.End).IsNotNull();
			await Assert.That(token.Range.Start.Line).IsGreaterThanOrEqualTo(0);
			await Assert.That(token.Range.Start.Character).IsGreaterThanOrEqualTo(0);
		}
	}

	[Test]
	public async Task GetSemanticTokensData_ReturnsLspFormat()
	{
		var data = Parser.GetSemanticTokensData(MModule.single("add(1,2)"), ParseType.Function);
		
		await Assert.That(data).IsNotNull();
		await Assert.That(data.TokenTypes).IsNotEmpty();
		await Assert.That(data.TokenModifiers).IsNotEmpty();
		await Assert.That(data.Data).IsNotEmpty();
		
		// Data should be in groups of 5 integers
		await Assert.That(data.Data.Length % 5).IsEqualTo(0);
	}

	[Test]
	public async Task GetSemanticTokens_Numbers_IdentifiesCorrectly()
	{
		var tokens = Parser.GetSemanticTokens(MModule.single("123"), ParseType.Function);
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have number token
		var numberTokens = tokens.Where(t => t.TokenType == SemanticTokenType.Number).ToList();
		await Assert.That(numberTokens.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task GetSemanticTokens_EscapeSequence_IdentifiesCorrectly()
	{
		var tokens = Parser.GetSemanticTokens(MModule.single("\\n test"), ParseType.Function);
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have escape sequence token
		var escapeTokens = tokens.Where(t => t.TokenType == SemanticTokenType.EscapeSequence).ToList();
		await Assert.That(escapeTokens.Count).IsGreaterThan(0);
	}

	[Test]
	public async Task GetSemanticTokens_Operators_IdentifiesCorrectly()
	{
		var tokens = Parser.GetSemanticTokens(MModule.single("a=b,c"), ParseType.Function);
		
		await Assert.That(tokens).IsNotEmpty();
		
		// Should have operator tokens
		var operatorTokens = tokens.Where(t => t.TokenType == SemanticTokenType.Operator).ToList();
		await Assert.That(operatorTokens.Count).IsGreaterThan(0);
	}
}
