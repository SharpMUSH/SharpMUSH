using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>%*&lt;text&gt;</c> marks its text as a list separator. A list function given a marked delimiter splits only
/// where the same text is marked, so an item may hold the separator's characters unmarked.
/// </summary>
public class ListSeparatorTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Eval(string code) => (await Parser.EvaluateAsync(MarkupText.Plain(code))).ToPlainText();

	[Test]
	// The separator prints as its text.
	[Arguments("a%*<|>b", "a|b")]
	[Arguments("a%*<%r>b", "a\nb")]
	[Arguments("a%*<::>b", "a::b")]
	// Only marked occurrences split; an unmarked '|' stays in its item.
	[Arguments("words(a|b%*<|>c,%*<|>)", "2")]
	[Arguments("words(a|b|c,%*<|>)", "1")]
	[Arguments("iter(a|b%*<|>c,strlen(##),%*<|>,-)", "3-1")]
	[Arguments("extract(a|b%*<|>c%*<|>d,2,1,%*<|>)", "c")]
	[Arguments("first(a|b%*<|>c,%*<|>)", "a|b")]
	[Arguments("rest(a|b%*<|>c|d%*<|>e,%*<|>)", "c|d|e")]
	[Arguments("words(rest(a|b%*<|>c|d%*<|>e,%*<|>),%*<|>)", "2")]
	// A marked output separator keeps the result a list of its own.
	[Arguments("words(iter(x y,##|##,%b,%*<|>),%*<|>)", "2")]
	[Arguments("iter(x y,##|##,%b,%*<|>)", "x|x|y|y")]
	// Marked neighbours are one stretch; a marked space keeps empty items.
	[Arguments("words(a%*<|>%*<|>b,%*<|>)", "3")]
	[Arguments("words(a%*<%b>%*<%b>b,%*<%b>)", "3")]
	[Arguments("words(a%*<||>b,%*<|>)", "3")]
	// Nested lists use different text for each level.
	[Arguments("iter(iter(a:1 b:2 c:3,edit(##,:,%*<^>),%b,%*<|>),words(##,%*<^>),%*<|>,-)", "2-2-2")]
	// Stripping colour keeps the mark, for a function with stripped arguments and for stripansi().
	[Arguments("words(ansi(r,a%*<|>b),%*<|>)", "2")]
	[Arguments("words(stripansi(ansi(r,a%*<|>b)),%*<|>)", "2")]
	// The functions that count or rebuild a list in place.
	[Arguments("items(a|b%*<|>c,%*<|>)", "2")]
	[Arguments("wordpos(a|b%*<|>c,3,%*<|>)", "1")]
	[Arguments("wordpos(a|b%*<|>c,5,%*<|>)", "2")]
	[Arguments("words(listset(a|b%*<|>c,2,x|y,%*<|>),%*<|>)", "2")]
	[Arguments("words(lnum(1,3,%*<|>),%*<|>)", "3")]
	// Comparison ignores the mark.
	[Arguments("strmatch(a|b%*<|>c,a|b|c)", "1")]
	[Arguments("eq(comp(a|b%*<|>c,a|b|c),0)", "1")]
	// The help's example of a list inside a list.
	[Arguments("iter(iter(Mannaz:12 Raya:9, edit(##, :, %*<^>), %b, %*<|>), [first(##, %*<^>)] has [rest(##, %*<^>)], %*<|>, %r)", "Mannaz has 12\nRaya has 9")]
	// Penn's plain delimiter still splits on every '|', marked or not.
	[Arguments("words(a|b%*<|>c,|)", "3")]
	public async Task SplitsOnlyOnMarkedSeparators(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	// unmark(), secure() and escape() take the marks off, so the text is one item.
	[Arguments("words(unmark(a%*<|>b),%*<|>)", "1")]
	[Arguments("unmark(a%*<|>b)", "a|b")]
	[Arguments("words(secure(a%*<|>b),%*<|>)", "1")]
	[Arguments("words(escape(a%*<|>b),%*<|>)", "1")]
	[Arguments("escape(a%*<|>b)", "\\a|b")]
	// unmark() keeps the colour; only the mark goes.
	[Arguments("decompose(unmark(ansi(r,a%*<|>b)))", "[ansi(r,a|b)]")]
	[Arguments("decompose(unmark(a|b))", "a|b")]
	// Text unmarked and then marked again by the code that builds the list splits as usual.
	[Arguments("words(iter(unmark(a%*<|>b) c,##,%b,%*<|>),%*<|>)", "2")]
	public async Task UnmarkTakesTheSeparatorsOff(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("decompose(a%*<|>b)", "a%*<|>b")]
	[Arguments("decompose(a%*<%r>b)", "a%*<%r>b")]
	[Arguments("decompose(a%*<\\>>b)", "a%*<\\>>b")]
	[Arguments("decompose(a%*<\\,>b)", "a%*<\\,>b")]
	[Arguments("decompose(a%*<|>%*<|>b)", "a%*<||>b")]
	public async Task DecomposeWritesTheSubstitution(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	public async Task DecomposedSeparatorEvaluatesToTheSameList()
	{
		var decomposed = await Eval("decompose(a|b%*<|>c%*<|>d)");
		await Assert.That(await Eval($"words({decomposed},%*<|>)")).IsEqualTo("3");
	}

	[Test]
	public async Task StoredSeparatorStaysMarked()
	{
		var attribute = TestIsolationHelpers.GenerateUniqueName("SEPLIST").ToUpperInvariant();
		await Eval($"attrib_set(me/{attribute},a|b%*<|>c)");

		await Assert.That(await Eval($"words(get(me/{attribute}),%*<|>)")).IsEqualTo("2");
		await Assert.That(await Eval($"wipe(me/{attribute})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task DolistSplitsOnAMarkedDelimiter()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("dolsep");
		await CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dolist/inline/delimit %*<|> {token}a|b%*<|>{token}c=@pemit #1=item:##"));

		var heard = WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef).Where(line => line.Contains(token, StringComparison.Ordinal)).ToList();
		await Assert.That(heard).IsEquivalentTo([$"item:{token}a|b", $"item:{token}c"]);
	}

	[Test]
	// lfields() splits labels and values on a marked delimiter.
	[Arguments("lfields({{\"border\":\"ascii\"}},Name%*<|>Motto,Raya%*<|>a|b,%*<|>)", "Name:  Raya\nMotto: a|b")]
	// ldatatable() splits rows on a marked delimiter; cells split on '|' as before.
	[Arguments("ldatatable({{\"border\":\"ascii\"}},Name|Motto,Raya|a^b%*<^>Mannaz|c,%*<^>)", "Name    Motto\n-------------\nRaya    a^b\nMannaz  c")]
	// A marked cell delimiter written into the options splits cells only where it is marked.
	[Arguments("ldatatable({{\"border\":\"ascii\",\"delim\":\"%*<^>\"}},Name%*<^>Motto,Raya%*<^>a^b%*<|>Mannaz%*<^>c,%*<|>)", "Name    Motto\n-------------\nRaya    a^b\nMannaz  c")]
	// The option lists split on the same marked delimiter.
	[Arguments("ldatatable({{\"border\":\"ascii\",\"delim\":\"%*<^>\",\"priority\":\"1%*<^>9\"}},Name%*<^>Motto,Raya%*<^>a^b%*<|>Mannaz%*<^>c,%*<|>)", "Name    Motto\n-------------\nRaya    a^b\nMannaz  c")]
	[Arguments("ldatatable({{\"border\":\"ascii\",\"delim\":\"%*<^>\",\"priority\":\"1^9\"}},Name%*<^>Motto,Raya%*<^>a^b%*<|>Mannaz%*<^>c,%*<|>)", "#-1 LAYOUT OPTION PRIORITY: INVALID ARGUMENT")]
	public async Task LayoutsSplitOnMarkedDelimiters(string code, string expected)
		=> await Assert.That(string.Join("\n", (await Eval(code)).Split('\n').Select(line => line.TrimEnd()))).IsEqualTo(expected);

	/// <summary>The Docs example (visual layouts guide): a table of typed text with marked delimiters.</summary>
	[Test]
	public async Task DocsTableOfTypedText()
	{
		var root = TestIsolationHelpers.GenerateUniqueName("MOTTO").ToUpperInvariant();
		await Eval($"attrib_set(me/{root}`MANNAZ,Dark|Light)[attrib_set(me/{root}`RAYA,Bard^Poet)]");

		var table = await Eval($"ldatatable({{{{\"width\":30,\"delim\":\"%*<|>\"}}}},Name%*<|>Motto,iter(lattr(me/{root}`*),[after(##,`)]%*<|>[unmark(v(##))],%b,%*<^>),%*<^>)");

		await Assert.That(string.Join("\n", table.Split('\n').Select(line => line.TrimEnd())))
			.IsEqualTo("Name    Motto\n------------------\nMANNAZ  Dark|Light\nRAYA    Bard^Poet");
		await Eval($"wipe(me/{root})");
	}

	[Test]
	public async Task ShowDrawsSeparatorsInReverseVideo()
	{
		var text = await Parser.EvaluateAsync(MarkupText.Plain("a|b%*<|>c"));
		var shown = ListSeparators.Show(text);

		await Assert.That(shown.ToPlainText()).IsEqualTo("a|b|c");
		await Assert.That(shown.Render(MarkupFormat.Ansi)).IsEqualTo("a|b\e[7m|\e[0mc");
	}
}
