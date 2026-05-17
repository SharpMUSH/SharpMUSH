using System.Reflection;
using Antlr4.Runtime;
using SharpMUSH.Implementation;

namespace SharpMUSH.Tests.Parser;

public class OptimizedTokenFactoryTests
{
	private static readonly Type StringSpanInputStreamType =
		typeof(MUSHCodeParser).Assembly.GetType("SharpMUSH.Implementation.StringSpanInputStream", throwOnError: true)!;

	private static readonly Type OptimizedTokenFactoryType =
		typeof(MUSHCodeParser).Assembly.GetType("SharpMUSH.Implementation.OptimizedTokenFactory", throwOnError: true)!;

	private static ICharStream CreateStringSpanInputStream(string input, string sourceName)
		=> (ICharStream)Activator.CreateInstance(
			StringSpanInputStreamType,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			binder: null,
			args: [input, sourceName],
			culture: null)!;

	private static ITokenFactory GetOptimizedTokenFactory()
		=> (ITokenFactory)OptimizedTokenFactoryType
			.GetField("Default", BindingFlags.Public | BindingFlags.Static)!
			.GetValue(null)!;

	private static List<IToken> ReadAllTokens(SharpMUSHLexer lexer)
	{
		var tokens = new List<IToken>();
		while (true)
		{
			var token = lexer.NextToken();
			tokens.Add(token);
			if (token.Type == TokenConstants.EOF)
			{
				break;
			}
		}

		return tokens;
	}

	[Test]
	[Arguments("add(1,2)")]
	[Arguments("test[inner]{brace}%0\\n")]
	[Arguments("strcat(foo,[bar(baz)],{qux})")]
	[Arguments("")]
	public async Task OptimizedTokenFactory_ShouldMatchCommonTokenFactory_ForLexerOutput(string input)
	{
		var baselineLexer = new SharpMUSHLexer(new AntlrInputStream(input))
		{
			TokenFactory = CommonTokenFactory.Default
		};
		var baselineTokens = ReadAllTokens(baselineLexer);

		var optimizedStream = CreateStringSpanInputStream(input, nameof(OptimizedTokenFactory_ShouldMatchCommonTokenFactory_ForLexerOutput));
		var optimizedLexer = new SharpMUSHLexer(optimizedStream)
		{
			TokenFactory = GetOptimizedTokenFactory()
		};
		var optimizedTokens = ReadAllTokens(optimizedLexer);

		await Assert.That(optimizedTokens.Count).IsEqualTo(baselineTokens.Count);

		for (var i = 0; i < baselineTokens.Count; i++)
		{
			var baseline = baselineTokens[i];
			var optimized = optimizedTokens[i];

			await Assert.That(optimized.Type).IsEqualTo(baseline.Type);
			await Assert.That(optimized.Channel).IsEqualTo(baseline.Channel);
			await Assert.That(optimized.StartIndex).IsEqualTo(baseline.StartIndex);
			await Assert.That(optimized.StopIndex).IsEqualTo(baseline.StopIndex);
			await Assert.That(optimized.Line).IsEqualTo(baseline.Line);
			await Assert.That(optimized.Column).IsEqualTo(baseline.Column);
			await Assert.That(optimized.Text).IsEqualTo(baseline.Text);
		}
	}

	[Test]
	public async Task OptimizedTokenFactory_ShouldMatchCommonTokenFactory_ForOutOfRangeTokenText()
	{
		const string input = "abc";
		var stream = CreateStringSpanInputStream(input, nameof(OptimizedTokenFactory_ShouldMatchCommonTokenFactory_ForOutOfRangeTokenText));
		var lexer = new SharpMUSHLexer(stream);
		var source = Tuple.Create<ITokenSource, ICharStream>(lexer, stream);
		var optimizedFactory = GetOptimizedTokenFactory();

		var optimized = optimizedFactory.Create(source, 1, null!, TokenConstants.DefaultChannel, input.Length + 1, input.Length + 2, 1, 0);
		var baseline = CommonTokenFactory.Default.Create(source, 1, null!, TokenConstants.DefaultChannel, input.Length + 1, input.Length + 2, 1, 0);

		await Assert.That(optimized.StartIndex).IsEqualTo(baseline.StartIndex);
		await Assert.That(optimized.StopIndex).IsEqualTo(baseline.StopIndex);
		await Assert.That(optimized.Text).IsEqualTo(baseline.Text);
	}
}
