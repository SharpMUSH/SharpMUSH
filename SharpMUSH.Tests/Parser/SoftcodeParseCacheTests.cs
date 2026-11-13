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

	/// <summary>Attribute text a typed command runs is not part of the line, and is still kept.</summary>
	[Test]
	public async Task AttributeTextATypedLineRuns_IsKept()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var attribute = TestIsolationHelpers.GenerateUniqueName("PARSED").ToUpperInvariant();
		var body = $"{attribute}[add(%0,2)]";
		var connections = Factory.Services.GetRequiredService<IConnectionService>();

		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"&{attribute} me={body}"));
		await Factory.CommandParser.CommandParse(1, connections, MarkupText.Plain($"think u(me/{attribute},1)"));

		// Short enough to key on its text, so the same body on other objects shares the tree.
		await Assert.That(cache.TryGet(KeyFor(body), out _)).IsTrue();
		await Assert.That(cache.TryGet(await SourceKeyFor(attribute), body, out _)).IsFalse();
	}

	/// <summary>A u() body too long to key on its text is kept under its attribute and modification time.</summary>
	[Test]
	public async Task LongAttributeBody_IsKeptUnderItsAttribute()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var attribute = TestIsolationHelpers.GenerateUniqueName("LONGBODY").ToUpperInvariant();
		var body = LongBody("[add(%0,1)]");
		await SetOnOneAsync(attribute, body);

		var result = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"u(#1/{attribute},1)"));

		await Assert.That(cache.TryGet(await SourceKeyFor(attribute), body, out _)).IsTrue();
		await Assert.That(result!.Message.ToPlainText()).IsEqualTo(new string('2', body.Length / "[add(%0,1)]".Length));
	}

	/// <summary>A $-command's action list too long to key on its text is kept the same way.</summary>
	[Test]
	public async Task LongCommandActionList_IsKeptUnderItsAttribute()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var attribute = TestIsolationHelpers.GenerateUniqueName("LONGCMD").ToUpperInvariant();
		var command = TestIsolationHelpers.GenerateUniqueName("+longcmd").ToLowerInvariant();
		var actionList = LongBody("@@ [add(1,1)];") + "@@ done";
		await SetOnOneAsync(attribute, $"${command}:{actionList}");

		await Factory.CommandParser.CommandParse(1, Factory.Services.GetRequiredService<IConnectionService>(),
			MarkupText.Plain(command));

		await Assert.That(cache.TryGet(await SourceKeyFor(attribute, "CommandListParse"), actionList, out _)).IsTrue();
	}

	/// <summary>A new value is a new modification time, so the old tree is never run for it.</summary>
	[Test]
	public async Task RewrittenAttribute_RunsItsNewValue()
	{
		var cache = Factory.Services.GetRequiredService<SoftcodeParseCache>();
		var attribute = TestIsolationHelpers.GenerateUniqueName("REWRITTEN").ToUpperInvariant();
		var call = MarkupText.Plain($"u(#1/{attribute},5)");
		var added = LongBody("[add(%0,1)]");
		var multiplied = LongBody("[mul(%0,3)]");

		await SetOnOneAsync(attribute, added);
		var before = await Factory.FunctionParser.FunctionParse(call);
		var firstKey = await SourceKeyFor(attribute);
		await SetOnOneAsync(attribute, multiplied);
		var after = await Factory.FunctionParser.FunctionParse(call);
		var secondKey = await SourceKeyFor(attribute);

		await Assert.That(secondKey.Modified).IsGreaterThan(firstKey.Modified);
		await Assert.That(cache.TryGet(secondKey, multiplied, out _)).IsTrue();
		await Assert.That(before!.Message.ToPlainText()).StartsWith("666");
		await Assert.That(after!.Message.ToPlainText()).StartsWith("151515");
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

	/// <summary><paramref name="unit"/> repeated past the longest text keyed on its text.</summary>
	private static string LongBody(string unit)
		=> string.Concat(Enumerable.Repeat(unit, SoftcodeParseCache.MaxTextLength / unit.Length + 1));

	private SoftcodeParseCache.SourceKey SourceKey(DBRef obj, string attribute, long modified,
		string entryPoint = "FunctionParse")
	{
		var options = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		return new SoftcodeParseCache.SourceKey(obj, attribute, modified, entryPoint, false,
			options.Compatibility.ParenGroups, options.Debug.ParserPredictionMode);
	}

	/// <summary>The key a parse of <paramref name="attribute"/>'s text on #1 keeps its tree under.</summary>
	private async Task<SoftcodeParseCache.SourceKey> SourceKeyFor(string attribute, string entryPoint = "FunctionParse")
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var one = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Object().DBRef;
		var stored = await mediator.CreateStream(new GetAttributeQuery(one, [attribute])).LastAsync();
		return SourceKey(one, attribute, stored.ModifiedTime!.Value, entryPoint);
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
