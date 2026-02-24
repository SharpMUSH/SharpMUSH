using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class SemanticHighlightingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

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
		// %# is the substitution for the current object's dbref.
		// The '#' token (DBREF) is a child of SubstitutionSymbolContext → Substitution.
		// Literal object references like #1234 (OTHER token starting with '#') remain ObjectReference.
		var subTokens = Parser.GetSemanticTokens(MModule.single("%#"), ParseType.Function);
		await Assert.That(subTokens).IsNotEmpty();
		var subToken = subTokens.FirstOrDefault(t => t.TokenType == SemanticTokenType.Substitution);
		await Assert.That(subToken).IsNotNull();

		// A literal dbref in an expression is still ObjectReference
		var litTokens = Parser.GetSemanticTokens(MModule.single("get(#1/ATTR)"), ParseType.Function);
		var litObjToken = litTokens.FirstOrDefault(t => t.TokenType == SemanticTokenType.ObjectReference);
		await Assert.That(litObjToken).IsNotNull();
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
		// Use CommandEqSplit parse type so that the '=' is the actual command split operator,
		// and CommandCommaArgs so that ',' is a real argument separator — not literal text.
		var eqTokens = Parser.GetSemanticTokens(MModule.single("a=b"), ParseType.CommandEqSplit);
		await Assert.That(eqTokens).IsNotEmpty();
		var eqOp = eqTokens.FirstOrDefault(t => t.TokenType == SemanticTokenType.Operator && t.Text.Trim() == "=");
		await Assert.That(eqOp).IsNotNull();

		// With CommandCommaArgs the comma IS an argument separator → Operator
		var commaTokens = Parser.GetSemanticTokens(MModule.single("a,b"), ParseType.CommandCommaArgs);
		var commaOp = commaTokens.FirstOrDefault(t => t.TokenType == SemanticTokenType.Operator && t.Text.Trim() == ",");
		await Assert.That(commaOp).IsNotNull();

		// With CommandList the semicolon is a command separator → Operator
		var semiTokens = Parser.GetSemanticTokens(MModule.single("a;b"), ParseType.CommandList);
		var semiOp = semiTokens.FirstOrDefault(t => t.TokenType == SemanticTokenType.Operator && t.Text.Trim() == ";");
		await Assert.That(semiOp).IsNotNull();
	}

	[Test]
	public async Task GetSemanticTokens_RegisterCaret_ClosingAngleIsRegisterNotOperator()
	{
		// %q<myvar>: the closing '>' should be Register (part of the %q<...> syntax),
		// not Operator (a standalone '>' comparison).
		var tokens = Parser.GetSemanticTokens(MModule.single("%q<myvar>"), ParseType.Function);

		await Assert.That(tokens).IsNotEmpty();

		// The '>' at the end must be Register
		var caretToken = tokens.LastOrDefault(t => t.Text == ">");
		await Assert.That(caretToken).IsNotNull();
		await Assert.That(caretToken!.TokenType).IsEqualTo(SemanticTokenType.Register);

		// No token in this expression should be classified as Operator
		var operatorTokens = tokens.Where(t => t.TokenType == SemanticTokenType.Operator).ToList();
		await Assert.That(operatorTokens).IsEmpty();
	}

	[Test]
	public async Task GetSemanticTokens_StandaloneAngleBracket_IsText()
	{
		// A bare '>' that is NOT part of %q<...> lives in BeginGenericTextContext → Text.
		// It is a literal character in MUSH code, not a language operator.
		var tokens = Parser.GetSemanticTokens(MModule.single("a>b"), ParseType.Function);

		await Assert.That(tokens).IsNotEmpty();

		var caretToken = tokens.FirstOrDefault(t => t.Text == ">");
		await Assert.That(caretToken).IsNotNull();
		await Assert.That(caretToken!.TokenType).IsEqualTo(SemanticTokenType.Text);
	}
}
