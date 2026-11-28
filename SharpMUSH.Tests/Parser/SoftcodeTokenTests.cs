using Antlr4.Runtime;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Parsing;

namespace SharpMUSH.Tests.Parser;

public class SoftcodeTokenTests
{
	private static List<IToken> LexWithCommonTokens(string input)
	{
		var lexer = new SharpMUSHLexer(new StringSpanInputStream(input, "test"));
		lexer.RemoveErrorListeners();
		var stream = new BufferedTokenSpanStream(lexer);
		stream.Fill();
		return Tokens(stream);
	}

	private static List<IToken> Tokens(BufferedTokenSpanStream stream)
		=> [.. Enumerable.Range(0, stream.Size).Select(stream.Get)];

	[Test]
	[Arguments("think [add(1,2)]")]
	[Arguments("&CMD me=$+go *:@pemit %#={%0}\n@switch %0=a,{b\n  c},d\n\n[iter(x y,%i0)]")]
	[Arguments("line one\r\nline two\r\n\r\n[strcat(é,ü)]{😀,\n}")]
	[Arguments("\n\nstarts and ends with newlines\n")]
	[Arguments("")]
	public async Task EveryTokenMatchesTheLexersOwn(string input)
	{
		var expected = LexWithCommonTokens(input);
		var actual = Tokens(SoftcodeParsePipeline.Lex(input, "test", rewriteOrphanedClosers: false));

		await Assert.That(actual.Count).IsEqualTo(expected.Count);
		for (var i = 0; i < expected.Count; i++)
		{
			var (e, a) = (expected[i], actual[i]);
			await Assert.That(a).IsTypeOf<SoftcodeToken>();
			await Assert.That((a.Type, a.StartIndex, a.StopIndex, a.TokenIndex, a.Line, a.Column, a.Channel, a.Text))
				.IsEqualTo((e.Type, e.StartIndex, e.StopIndex, e.TokenIndex, e.Line, e.Column, e.Channel, e.Text));
		}
	}

	[Test]
	public async Task TokensWithTextOfTheirOwnAreCommonTokens()
	{
		var source = Tuple.Create<ITokenSource, ICharStream>(null!, new StringSpanInputStream("abc", "test"));

		var token = SoftcodeTokenFactory.Instance.Create(source, SharpMUSHLexer.OTHER, "", TokenConstants.DefaultChannel, 2, 2, 1, 3);

		await Assert.That(token).IsTypeOf<CommonToken>();
		await Assert.That((token.Text, token.Line, token.Column)).IsEqualTo(("", 1, 3));
	}
}
