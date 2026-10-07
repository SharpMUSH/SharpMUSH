using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>decompose()</c> of a layout is the call that builds it: evaluated, it draws the same layout,
/// and decomposed again it reads the same.
/// </summary>
public class LayoutDecomposeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;

	private async Task<MString> Eval(string code) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(code)))!.Message!;

	[Test]
	[Arguments("box(Hello there.)", "[box(Hello there.)]")]
	[Arguments("box(Hi,T,16,{{\"border\":\"double\",\"pad\":2}})", "[box(Hi,T,16,{{\"border\":\"double\",\"pad\":2}})]")]
	[Arguments("box(x,,20,{{\"border\":\"ascii\",\"top\":\"-=\"}})", "[box(x,,20,{{\"border\":\"ascii\",\"top\":\"-=\"}})]")]
	[Arguments("rule(Factions,40)", "[rule(Factions,40)]")]
	[Arguments("rule(Factions,,{{\"title\":\"left\"}})", "[rule(Factions,,{{\"title\":\"left\"}})]")]
	[Arguments("flex({{\"sep\":\" | \",\"width\":40}},item(Strength%rAgility,18),item(High%rLow,19))",
		"[flex({{\"width\":40,\"sep\":\" | \"}},[item(Strength%rAgility,18)],[item(High%rLow,19)])]")]
	[Arguments("flex(,a,b)", "[flex(,a,b)]")]
	[Arguments("item(Left,>40\\%,10,2)", "[item(Left,>40\\%,10,2)]")]
	[Arguments("fields({{\"width\":40,\"leader\":\".\"}},Sex,Male,Species,Human)", "[fields({{\"width\":40,\"leader\":\".\"}},Sex,Male,Species,Human)]")]
	[Arguments("tree({{\"guide\":\"ascii\"}},node(Mail,Inbox,Sent))", "[tree({{\"guide\":\"ascii\"}},[node(Mail,Inbox,Sent)])]")]
	[Arguments("gauge(6,12,HP,{{\"bar\":4,\"gradient\":\"#ff0000|#00ff00\"}})", "[gauge(6,12,HP,{{\"bar\":4,\"gradient\":\"#ff0000|#00ff00\"}})]")]
	[Arguments("bullets(alpha beta gamma,,{{\"style\":\"number\"}})", "[bullets(alpha beta gamma,,{{\"style\":\"number\"}})]")]
	[Arguments("bullets(one two|three,|)", "[bullets(one two|three,|)]")]
	[Arguments("grid(a b c d,,{{\"across\":true}})", "[grid(a b c d,,{{\"across\":true}})]")]
	[Arguments("datatable({{\"nowrap\":\"1\",\"priority\":\"|2\"}},Name|>Score,Ann|10,Bob|7)",
		"[datatable({{\"priority\":\"|2\",\"nowrap\":\"1\"}},Name|>Score,Ann|10,Bob|7)]")]
	[Arguments("datacolumns(,Name|Ann|Bob,Score|10|7)", "[datatable(,Name|Score,Ann|10,Bob|7)]")]
	[Arguments("gradient(box(Hi,T,12),#ff0000|#0000ff,{{\"flow\":\"diagonal\"}})", "[gradient([box(Hi,T,12)],#ff0000|#0000ff,{{\"flow\":\"diagonal\"}})]")]
	[Arguments("box(a%r[rule()]%rb,,9)", "[box(a%r[rule()]%rb,,9)]")]
	[Arguments("box(fields(,Sex,Male)%r[rule(Quote)]%rHooo,Name)", "[box([fields(,Sex,Male)]%r[rule(Quote)]%rHooo,Name)]")]
	[Arguments("figure(,A cat,=^.^=,left,The cat sits.,30)", "[figure(,A cat,=\\^.\\^=,left,The cat sits.,30)]")]
	[Arguments("box(x,,,{{\"theme\":\"nord\"}})", "[box(x,,,{{\"theme\":\"nord\"}})]")]
	[Arguments("datatable({{\"stripe\":true}},A,1,2)", "[datatable({{\"stripe\":true}},A,1,2)]")]
	public async Task ALayoutDecomposesToTheCallThatBuildsIt(string call, string expected)
	{
		var softcode = (await Eval($"decompose({call})")).ToPlainText();
		await Assert.That(softcode).IsEqualTo(expected);
		await AssertRebuilds(call, softcode);
	}

	[Test]
	[Arguments("box(Hi there,Title,30,{{\"border\":\"rounded\",\"tl\":\"[ansi(r,*)]\"}})")]
	[Arguments("box(flex({{\"sep\":\" | \"}},item(Sex: Male%rSpecies: Human,35),item(Job: Dark Warrior%rOnline: 1h,36))%r[rule(Quote)]%rHooooo?,Mannaz Byron,78,{{\"open\":\"<< \",\"close\":\" >>\"}})")]
	[Arguments("box([ansi(hr,Red)] and a\\, comma,[ansi(g,Green)])")]
	[Arguments("tree({{\"width\":30}},node(Channels,node(Public,+chat,+ooc),node(Staff,+admin)))")]
	[Arguments("bullets(a|b c|[box(x)],|)")]
	[Arguments("datatable({{\"delim\":\"~\"}},A~B,x|y~[box(z)])")]
	[Arguments("fields({{\"sep\":\" = \",\"cols\":2,\"align\":\"right\"}},a,1,b,2)")]
	[Arguments("gauge(3.5,10,,{{\"show\":\"value\",\"filled\":\"#\",\"empty\":\".\",\"open\":\"\",\"close\":\"\"}})")]
	[Arguments("before%r[box(inner)]%rafter [ansi(r,red)]")]
	[Arguments("gradient(rule(Hi,20),hr|hb,{{\"mirror\":true,\"space\":\"hsl\",\"repeat\":2}})")]
	[Arguments("box(x,,,{{\"theme\":\"fantasy\",\"border\":\"heavy\"}})")]
	[Arguments("fields({{\"stripe\":\"/#202020\",\"border\":\"ascii\"}},a,box(b))")]
	[Arguments("box(x,,,{{\"theme\":{\"seed\":\"#7aa2f7\",\"harmony\":\"triadic\"}}})")]
	public async Task ALayoutRebuildsFromItsDecomposition(string call)
		=> await AssertRebuilds(call, (await Eval($"decompose({call})")).ToPlainText());

	/// <summary>The softcode draws what the call drew, for a terminal and a browser, and reads back the same.</summary>
	private async Task AssertRebuilds(string call, string softcode)
	{
		var original = await Eval(call);
		var rebuilt = await Eval(softcode);
		await Assert.That(rebuilt.Render(MarkupFormat.Ansi)).IsEqualTo(original.Render(MarkupFormat.Ansi)).Because(softcode);
		await Assert.That(StripIds(rebuilt.Render(MarkupFormat.Html))).IsEqualTo(StripIds(original.Render(MarkupFormat.Html))).Because(softcode);
		await Assert.That((await Eval($"decompose({softcode})")).ToPlainText()).IsEqualTo(softcode);
	}

	private static string StripIds(string html) => System.Text.RegularExpressions.Regex.Replace(html, " id=\"[^\"]*\"", string.Empty);
}
