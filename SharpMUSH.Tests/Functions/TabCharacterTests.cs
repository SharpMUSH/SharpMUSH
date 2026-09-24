using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// A tab is a character, not whitespace to trim (#1256). PennMUSH trims only spaces from a function
/// argument (<c>process_expression</c>, <c>src/parse.c</c>), so a tab that <c>%t</c> or <c>chr(9)</c>
/// produced reaches the function: <c>strlen(%t)</c> is 1.
/// <para>
/// Not covered: a literal tab typed straight after <c>(</c> or <c>,</c> is absorbed by the lexer's
/// <c>WS</c> fragment along with newlines, which is what lets a formatter lay softcode out over several
/// lines without changing it. PennMUSH keeps such a tab; that difference is deliberate.
/// </para>
/// </summary>
public class TabCharacterTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Evaluate(string code)
		=> (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();

	[Test]
	[Arguments("strlen(%t)", "1")]
	[Arguments("strlen(chr(9))", "1")]
	[Arguments("strlen(a%tb)", "3")]
	[Arguments("strlen(%t%t)", "2")]
	[Arguments("strlen(x%t)", "2")]
	[Arguments("strlen(%tx)", "2")]
	[Arguments("strlen(%b%t%b)", "3")]
	[Arguments("strlen(cat(a,%t,b))", "5")]
	[Arguments("strlen(strcat(a,%t))", "2")]
	[Arguments("ord(%t)", "9")]
	[Arguments("strlen(a%rb)", "3")]
	[Arguments("words(a%tb)", "1")]
	[Arguments("mid(a%tb,1,1)", "\t")]
	[Arguments("trim(%tx%t)", "\tx\t")]
	[Arguments("strlen(first(%t))", "1")]
	[Arguments("t(%t)", "1")]
	[Arguments("strlen(a\tb)", "3")]
	[Arguments("strlen(a\t)", "2")]
	[Arguments("strlen(s(a%tb))", "3")]
	public async Task ATabReachesTheFunction(string code, string expected)
		=> await Assert.That(await Evaluate(code)).IsEqualTo(expected);

	[Test]
	public async Task AStoredTabSurvivesAReadBack()
	{
		await Evaluate("attrib_set(me/TAB1256,a%tb)");

		await Assert.That(await Evaluate("strlen(get(me/TAB1256))")).IsEqualTo("3");
		await Assert.That(await Evaluate("strlen(v(TAB1256))")).IsEqualTo("3");
	}
}
