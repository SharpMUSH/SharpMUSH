using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Parser;

public class SoftcodeParseCacheTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private SoftcodeParseCache.Key KeyFor(string text, bool lenient = false)
	{
		var options = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		return new SoftcodeParseCache.Key(text, "FunctionParse", lenient,
			options.Compatibility.ParenGroups, options.Debug.ParserPredictionMode);
	}

	[Test]
	public async Task EvaluatedText_IsParsedOnce()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var text = $"{TestIsolationHelpers.GenerateUniqueName("parsed")}[add(1,2)]";

		await Assert.That(cache.TryGet(KeyFor(text), out _)).IsFalse();
		var first = await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(text));
		await Assert.That(cache.TryGet(KeyFor(text), out var entry)).IsTrue();

		var second = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(text));
		await Assert.That(cache.TryGet(KeyFor(text), out var again)).IsTrue();
		await Assert.That(again).IsSameReferenceAs(entry);
		await Assert.That(second!.Message.ToPlainText()).IsEqualTo(first.ToPlainText());
		await Assert.That(second.Message.ToPlainText()).EndsWith("3");
	}

	[Test]
	public async Task SharedTree_KeepsEachEvaluationsOwnMarkup()
	{
		var text = $"{TestIsolationHelpers.GenerateUniqueName("styled")} [add(1,2)]";
		var red = MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)), MarkupText.Plain(text));

		var styled = await Factory.FunctionParser.FunctionParse(red);
		var plain = await Factory.FunctionParser.FunctionParse(MarkupText.Plain(text));

		await Assert.That(styled!.Message.ToPlainText()).IsEqualTo(plain!.Message.ToPlainText());
		await Assert.That(styled.Message.Render(MarkupFormat.Ansi)).IsNotEqualTo(styled.Message.ToPlainText());
		await Assert.That(plain.Message.Render(MarkupFormat.Ansi)).IsEqualTo(plain.Message.ToPlainText());
	}

	/// <summary>A typed line can carry a password, so no part of it outlives the command.</summary>
	[Test]
	public async Task TypedText_IsNotKept()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var attribute = TestIsolationHelpers.GenerateUniqueName("TYPED").ToUpperInvariant();

		// & evaluates the attribute name it was typed with on its own.
		await Factory.CommandParser.CommandParse(1, Factory.Services.GetRequiredService<IConnectionService>(),
			MarkupText.Plain($"&{attribute} me=1"));

		await Assert.That(cache.TryGet(KeyFor(attribute), out _)).IsFalse();
	}

	/// <summary>A function re-parsing part of the typed line in its own frame, as localize() does, still keeps none of it.</summary>
	[Test]
	public async Task TypedTextAFunctionEvaluates_IsNotKept()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var body = $"{TestIsolationHelpers.GenerateUniqueName("typedbody")} [add(1,2)]";

		await Factory.CommandParser.CommandParse(1, Factory.Services.GetRequiredService<IConnectionService>(),
			MarkupText.Plain($"think [localize({body})]"));

		await Assert.That(cache.TryGet(KeyFor(body), out _)).IsFalse();
	}

	/// <summary>
	/// Attribute text a typed command runs is not part of the line, and is still kept: under the attribute and its
	/// modification time, not under its text.
	/// </summary>
	[Test]
	public async Task AttributeTextATypedLineRuns_IsKeptUnderItsAttribute()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var attribute = TestIsolationHelpers.GenerateUniqueName("PARSED").ToUpperInvariant();
		var body = $"{attribute}[add(%0,2)]";
		var connections = Factory.Services.GetRequiredService<IConnectionService>();

		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"&{attribute} me={body}"));
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"think u(me/{attribute},1)"));

		await Assert.That(cache.TryGet(await SourceKeyFor(attribute), body, out _)).IsTrue();
		await Assert.That(cache.TryGet(KeyFor(body), out _)).IsFalse();
	}

	/// <summary>A body too long to key on its text is parsed once all the same.</summary>
	[Test]
	public async Task LongAttributeBody_IsParsedOnce()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var attribute = TestIsolationHelpers.GenerateUniqueName("LONGBODY").ToUpperInvariant();
		var body = string.Concat(Enumerable.Repeat("[add(%0,1)]", SoftcodeParseCache.MaxTextLength / 10));
		await Assert.That(body.Length).IsGreaterThan(SoftcodeParseCache.MaxTextLength);
		await SetOnOneAsync(attribute, body);

		var first = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"u(#1/{attribute},1)"));
		await Assert.That(cache.TryGet(await SourceKeyFor(attribute), body, out var entry)).IsTrue();

		var second = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"u(#1/{attribute},1)"));
		await Assert.That(cache.TryGet(await SourceKeyFor(attribute), body, out var again)).IsTrue();
		await Assert.That(again).IsSameReferenceAs(entry);
		await Assert.That(second!.Message.ToPlainText()).IsEqualTo(first!.Message.ToPlainText());
		await Assert.That(second.Message.ToPlainText()).StartsWith("222");
	}

	/// <summary>A new value is a new modification time, so the old tree is never run for it.</summary>
	[Test]
	public async Task RewrittenAttribute_RunsItsNewValue()
	{
		var attribute = TestIsolationHelpers.GenerateUniqueName("REWRITTEN").ToUpperInvariant();
		var call = MarkupText.Plain($"u(#1/{attribute},5)");

		await SetOnOneAsync(attribute, "[add(%0,1)]");
		var before = await Factory.FunctionParser.FunctionParse(call);
		await SetOnOneAsync(attribute, "[mul(%0,3)]");
		var after = await Factory.FunctionParser.FunctionParse(call);

		await Assert.That(before!.Message.ToPlainText()).IsEqualTo("6");
		await Assert.That(after!.Message.ToPlainText()).IsEqualTo("15");
	}

	/// <summary>
	/// An attribute deleted and set again, or an object's reused dbref, can bring back a time another value had: the key
	/// then holds a tree of other text, which is not used.
	/// </summary>
	[Test]
	public async Task SourceKeyOfOtherText_IsNotUsed()
	{
		var cache = new SoftcodeParseCache();
		var key = SourceKey(new DBRef(5, 100), "FN", 42);
		cache.Add(key, "[add(%0,1)]", new SoftcodeParseCache.Entry(new object(), new ParserErrorListener("[add(%0,1)]")));

		await Assert.That(cache.TryGet(key, "[mul(%0,3)]", out _)).IsFalse();
		await Assert.That(cache.TryGet(key, "[add(%0,1)]", out _)).IsTrue();

		cache.Add(key, "[mul(%0,3)]", new SoftcodeParseCache.Entry(new object(), new ParserErrorListener("[mul(%0,3)]")));
		await Assert.That(cache.TryGet(key, "[mul(%0,3)]", out _)).IsTrue();
		await Assert.That(cache.TryGet(key, "[add(%0,1)]", out _)).IsFalse();
	}

	[Test]
	public async Task SourcedTextLongerThanAnAttribute_IsNotKept()
	{
		var cache = new SoftcodeParseCache();
		var key = SourceKey(new DBRef(5, 100), "FN", 42);
		var text = new string('x', SoftcodeParseCache.MaxSourcedTextLength + 1);
		cache.Add(key, text, new SoftcodeParseCache.Entry(new object(), new ParserErrorListener(text)));
		await Assert.That(cache.TryGet(key, text, out _)).IsFalse();
	}

	[Test]
	public async Task FullCache_DropsSourcedTrees()
	{
		var cache = new SoftcodeParseCache();
		var text = new string('x', SoftcodeParseCache.MaxSourcedTextLength);
		var keys = Enumerable.Range(0, SoftcodeParseCache.CapacityInCharacters / SoftcodeParseCache.MaxSourcedTextLength)
			.Select(i => SourceKey(new DBRef(i, 100), "FN", 42))
			.ToList();
		foreach (var key in keys)
			cache.Add(key, text, new SoftcodeParseCache.Entry(new object(), new ParserErrorListener(text)));
		await Assert.That(cache.TryGet(keys[0], text, out _)).IsTrue();

		var overflow = KeyFor("overflow");
		cache.Add(overflow, new SoftcodeParseCache.Entry(new object(), new ParserErrorListener(overflow.Text)));

		await Assert.That(cache.TryGet(keys[0], text, out _)).IsFalse();
		await Assert.That(cache.TryGet(overflow, out _)).IsTrue();
	}

	private SoftcodeParseCache.SourceKey SourceKey(DBRef obj, string attribute, long modified)
	{
		var options = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		return new SoftcodeParseCache.SourceKey(obj, attribute, modified, "FunctionParse", false,
			options.Compatibility.ParenGroups, options.Debug.ParserPredictionMode);
	}

	/// <summary>The key a u() of <paramref name="attribute"/> on #1 keeps its tree under.</summary>
	private async Task<SoftcodeParseCache.SourceKey> SourceKeyFor(string attribute)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var one = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Object().DBRef;
		var stored = await mediator.CreateStream(new GetAttributeQuery(one, [attribute])).LastAsync();
		return SourceKey(one, attribute, stored.ModifiedTime!.Value);
	}

	private async Task SetOnOneAsync(string attribute, string value)
		=> await Factory.CommandParser.CommandParse(1, Factory.Services.GetRequiredService<IConnectionService>(),
			MarkupText.Plain($"&{attribute} #1={value}"));

	/// <summary>A command in an action list splits the same arguments each time the list runs.</summary>
	[Test]
	public async Task ActionListArguments_AreParsedOnce()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var arguments = $"me={TestIsolationHelpers.GenerateUniqueName("split")} [add(1,2)]";
		var options = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var key = new SoftcodeParseCache.Key(arguments, "CommandEqSplitParse", true,
			options.Compatibility.ParenGroups, options.Debug.ParserPredictionMode);

		await Factory.CommandParser.CommandListParse(MarkupText.Plain($"@pemit {arguments}"));
		await Assert.That(cache.TryGet(key, out var entry)).IsTrue();

		await Factory.CommandParser.CommandListParse(MarkupText.Plain($"@pemit {arguments}"));
		await Assert.That(cache.TryGet(key, out var again)).IsTrue();
		await Assert.That(again).IsSameReferenceAs(entry);
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
