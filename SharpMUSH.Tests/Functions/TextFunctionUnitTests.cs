using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class TextFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> EvalAs(DBRef executor, string expression)
		=> (await WebAppFactoryArg.FunctionParserFor(executor).FunctionParse(MarkupText.Plain(expression)))
			?.Message!.ToPlainText() ?? "<null>";

	private async Task<string> Eval(string expression)
		=> (await Parser.FunctionParse(MarkupText.Plain(expression)))?.Message!.ToPlainText() ?? "<null>";

	/// <summary>
	/// <c>fun_textentries</c> is <c>textentries(&lt;type&gt;, &lt;pattern&gt;[, &lt;osep&gt;])</c>
	/// (<c>src/help.c:1170</c>): the pattern is required and filters the entry names, and the
	/// separator is the third argument. SharpMUSH declared it 1..2 and read argument 2 as the
	/// separator, so it listed every entry and a caller's pattern became the separator. The entry
	/// names come from the shipped help corpus; <c>strip*</c> names exactly two of them.
	/// </summary>
	[Test]
	[Arguments("textentries(help,strip*)", "STRIPACCENTS() STRIPANSI()")]
	[Arguments("textentries(help,strip*,|)", "STRIPACCENTS()|STRIPANSI()")]
	[Arguments("textentries(help,strallof*)", "STRALLOF()")]
	[Arguments("textentries(help,zzznosuchtopic*)", "")]
	public async Task TextentriesFiltersByPattern(string expression, string expected)
		=> await Assert.That(await Eval(expression)).IsEqualTo(expected);

	/// <summary>The pattern is not optional — one argument is an arity error, not a listing.</summary>
	[Test]
	public async Task TextentriesRequiresItsPattern()
		=> await Assert.That(await Eval("textentries(help)"))
			.StartsWith("#-1 FUNCTION (TEXTENTRIES) EXPECTS");

	/// <summary>
	/// An unknown file is refused before anything is read (<c>src/help.c:1177-1181</c>) rather than
	/// falling through to "search every category".
	/// </summary>
	[Test]
	[Arguments("textentries(nosuchfile,*)")]
	[Arguments("textfile(nosuchfile,SOMETHING)")]
	public async Task AnUnknownTextFileIsRefused(string expression)
		=> await Assert.That(await Eval(expression)).IsEqualTo(ErrorMessages.Returns.NoSuchFile);

	/// <summary>
	/// The <c>ahelp</c> corpus is PennMUSH's <c>admin</c> help file, gated on <c>Hasprivs</c>
	/// (<c>src/help.c:1182-1185</c>) — which is why the AHELP command refuses non-wizards. A mortal
	/// handle is the only way to see that: the fixture's handle 1 is God, so a God-driven test here
	/// passes whether or not the gate exists.
	/// </summary>
	[Test]
	public async Task TheAdminCorpusIsRefusedToAMortal()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TextEntriesMortal");

		await Assert.That(await EvalAs(mortal.DbRef, "textentries(ahelp,*)"))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await EvalAs(mortal.DbRef, "textfile(ahelp,AHELP)"))
			.IsEqualTo(ErrorMessages.Returns.PermissionDenied);

		await Assert.That(await Eval("textentries(ahelp,*)")).IsNotEmpty()
			.Because("God must still be able to read it, or the refusal above proves nothing");
	}

	[Test]
	[Arguments("textfile(file)", "")]
	public async Task Textfile(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("textsearch(file,pattern)", "")]
	public async Task Textsearch(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}
}
