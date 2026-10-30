using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// Each thread reuses one lexer and one parser per paren_groups setting (SoftcodeParsePipeline), so
/// a parse must come out the same whatever the reused instances parsed before it, including input
/// that failed, recovered with conjured tokens, or switched lexer modes part way.
/// </summary>
public class ParserReuseTests
{
	private static readonly (string Input, ParseType Type, bool ParenGroups)[] Inputs =
	[
		("add(1,2)", ParseType.Function, false),
		("[unclosed(a,b", ParseType.Function, false),
		("think %q<a>\\[x] — ünïcödé ✓", ParseType.CommandList, false),
		("me=[name(%#)],second,third(x,", ParseType.CommandEqSplitArgs, false),
		("f(a (b, c) d, e)", ParseType.Function, true),
		("(a, b) = c; (d", ParseType.CommandList, true),
		("a,b(c,d),[e(f,g)],{g,h", ParseType.CommandCommaArgs, false),
		("%", ParseType.Function, false),
		("x=y=z(1,=)", ParseType.CommandEqSplit, false),
	];

	private static string Tree(BufferedTokenSpanStream tokens, ParseType type, SharpMUSHParser parser, string input,
		out int errors)
	{
		parser.ErrorHandler = type is ParseType.Function or ParseType.Command or ParseType.CommandList
			? new DefaultErrorStrategy()
			: new LenientErrorStrategy();
		var listener = new ParserErrorListener(input);
		parser.AddErrorListener(listener);
		var tree = SoftcodeParsePipeline.Enter(parser, type);
		errors = listener.Errors.Count;
		return $"{tree.ToStringTree(parser)} | {tree.GetText()}";
	}

	[Test]
	public async Task ReusedParserBuildsTheSameTreesAsFreshOnes()
	{
		var fresh = Inputs.Select(item =>
		{
			var tokens = SoftcodeParsePipeline.Lex(item.Input, nameof(ReusedParserBuildsTheSameTreesAsFreshOnes));
			var parser = SoftcodeParsePipeline.CreateParser(tokens, item.ParenGroups, PredictionMode.LL);
			return (Tree(tokens, item.Type, parser, item.Input, out var errors), errors);
		}).ToArray();

		// Twice round, so every input follows every kind of input on the same instances.
		for (var round = 0; round < 2; round++)
		{
			for (var i = 0; i < Inputs.Length; i++)
			{
				var (input, type, parenGroups) = Inputs[i];
				var tokens = SoftcodeParsePipeline.Lex(input, nameof(ReusedParserBuildsTheSameTreesAsFreshOnes));
				var parser = SoftcodeParsePipeline.RentParser(tokens, parenGroups, PredictionMode.LL);
				try
				{
					var tree = Tree(tokens, type, parser, input, out var errors);
					await Assert.That(tree).IsEqualTo(fresh[i].Item1);
					await Assert.That(errors).IsEqualTo(fresh[i].errors);
				}
				finally
				{
					SoftcodeParsePipeline.ReturnParser(parser);
				}
			}
		}
	}

	[Test]
	public async Task TokensKeepTheirInputAfterTheLexerMovesOn()
	{
		const string first = "think [add(1,2)] ✓ done";
		var tokens = SoftcodeParsePipeline.Lex(first, nameof(TokensKeepTheirInputAfterTheLexerMovesOn));
		SoftcodeParsePipeline.Lex("@pemit %#=something else entirely", nameof(TokensKeepTheirInputAfterTheLexerMovesOn));

		var text = string.Concat(Enumerable.Range(0, tokens.Size).Select(tokens.Get)
			.Where(token => token.Type != TokenConstants.EOF)
			.Select(token => token.InputStream.GetText(Antlr4.Runtime.Misc.Interval.Of(token.StartIndex, token.StopIndex))));

		await Assert.That(text).IsEqualTo(first);
	}
}
