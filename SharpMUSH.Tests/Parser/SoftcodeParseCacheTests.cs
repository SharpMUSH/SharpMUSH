using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Parser;

public class SoftcodeParseCacheTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private SoftcodeParseCache.Key KeyFor(string text)
	{
		var options = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		return new SoftcodeParseCache.Key(text, "FunctionParse", false,
			options.Compatibility.ParenGroups, options.Debug.ParserPredictionMode);
	}

	[Test]
	public async Task EvaluatedText_IsParsedOnce()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var text = $"{TestIsolationHelpers.GenerateUniqueName("parsed")}[add(1,2)]";

		await Assert.That(cache.TryGet(KeyFor(text), out _)).IsFalse();
		var first = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(text));
		await Assert.That(cache.TryGet(KeyFor(text), out var entry)).IsTrue();

		var second = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(text));
		await Assert.That(cache.TryGet(KeyFor(text), out var again)).IsTrue();
		await Assert.That(again).IsSameReferenceAs(entry);
		await Assert.That(second!.Message!.ToPlainText()).IsEqualTo(first!.Message!.ToPlainText());
		await Assert.That(second.Message.ToPlainText()).EndsWith("3");
	}

	[Test]
	public async Task SharedTree_KeepsEachEvaluationsOwnMarkup()
	{
		var text = $"{TestIsolationHelpers.GenerateUniqueName("styled")} [add(1,2)]";
		var red = MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)), MarkupText.Plain(text));

		var styled = await Factory.FunctionParser.FunctionParse(red);
		var plain = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(text));

		await Assert.That(styled!.Message!.ToPlainText()).IsEqualTo(plain!.Message!.ToPlainText());
		await Assert.That(styled.Message.Render(MarkupFormat.Ansi)).IsNotEqualTo(styled.Message.ToPlainText());
		await Assert.That(plain.Message.Render(MarkupFormat.Ansi)).IsEqualTo(plain.Message.ToPlainText());
	}

	[Test]
	public async Task LongText_IsNotKept()
	{
		var cache = new SoftcodeParseCache();
		var key = KeyFor(new string('x', SoftcodeParseCache.MaxTextLength + 1));
		cache.Add(key, new SoftcodeParseCache.Entry(new object(), new ParserErrorListener(key.Text)));
		await Assert.That(cache.TryGet(key, out _)).IsFalse();
	}

	[Test]
	public async Task FullCache_StartsAgain()
	{
		var cache = new SoftcodeParseCache();
		var keys = Enumerable.Range(0, SoftcodeParseCache.CapacityInCharacters / SoftcodeParseCache.MaxTextLength)
			.Select(i => KeyFor(i.ToString().PadRight(SoftcodeParseCache.MaxTextLength, 'x')))
			.ToList();
		foreach (var key in keys)
			cache.Add(key, new SoftcodeParseCache.Entry(new object(), new ParserErrorListener(key.Text)));
		await Assert.That(cache.TryGet(keys[0], out _)).IsTrue();

		var overflow = KeyFor("overflow");
		cache.Add(overflow, new SoftcodeParseCache.Entry(new object(), new ParserErrorListener(overflow.Text)));

		await Assert.That(cache.TryGet(keys[0], out _)).IsFalse();
		await Assert.That(cache.TryGet(overflow, out _)).IsTrue();
	}
}
