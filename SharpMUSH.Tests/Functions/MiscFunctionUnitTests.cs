using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class MiscFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("list(functions)", "LIST")]
	[Arguments("list(commands)", "@EMIT")]
	[Arguments("list(locks)", "BASIC")] // Penn upper-cases every name list() returns
	public async Task List(string str, string expectedContains)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).Contains(expectedContains);
	}

	/// <summary>
	/// <c>foreach()</c> is <c>map()</c> over characters, not over a list: the ufun runs once per
	/// character with the character as <c>%0</c> and its zero-based position as <c>%1</c>, and the
	/// results are concatenated with nothing between them (help FOREACH, PennMUSH funstr.c:1123).
	/// </summary>
	[Test]
	[Arguments(@"foreach(#lambda/x,abc)", "xxx")]
	[Arguments(@"foreach(#lambda/\%0\%0,abc)", "aabbcc")]
	[Arguments(@"foreach(#lambda/\%1,abcde)", "01234")]
	[Arguments(@"foreach(#lambda/\%0,)", "")]
	[Arguments(@"foreach(#lambda/add\(\%0\,1\),54321)", "65432")]
	// <start> and <end> bracket the transformed span. What falls outside is copied through
	// untouched, the markers themselves are dropped, and %1 keeps counting positions in the
	// original string, so the first transformed character of "This is #0# number" is at 9.
	[Arguments(@"foreach(#lambda/add\(\%0\,1\),This is #0# number,#,#)", "This is 1 number")]
	[Arguments(@"foreach(#lambda/\%1,abcde,b,d)", "a2e")]
	[Arguments(@"foreach(#lambda/x,abcde,b)", "axxx")]
	// No opening marker anywhere means nothing is transformed at all.
	[Arguments(@"foreach(#lambda/x,abcde,z)", "abcde")]
	[Arguments(@"foreach(#lambda/x,abcde,bc)", "#-1 SEPARATOR MUST BE ONE CHARACTER")]
	[Arguments(@"foreach(#lambda/x,abcde,b,cd)", "#-1 SEPARATOR MUST BE ONE CHARACTER")]
	public async Task Foreach(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// The stored-attribute form, which is the one the helpfile documents; <c>#lambda</c> only
	/// spares the test a database write.
	/// </summary>
	[Test]
	public async Task ForeachCallsAStoredAttributeOncePerCharacter()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var attributes = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var actor = (await mediator.Send(new GetObjectNodeQuery(WebAppFactoryArg.ExecutorDBRef))).Expect<AnySharpObject>();
		var name = "FOREACH" + Guid.NewGuid().ToString("N");
		await attributes.SetAttributeAsync(actor, actor, name, MarkupText.Plain("<%1:[ucstr(%0)]>"));

		var result = await Parser.FunctionParse(MarkupText.Plain($"foreach(me/{name},abc)"));

		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("<0:A><1:B><2:C>");
	}

	/// <summary>
	/// Every character keeps the markup it arrived with, both the ones handed to the ufun and the
	/// ones copied through outside the markers.
	/// </summary>
	[Test]
	public async Task ForeachPreservesMarkupOnBothTransformedAndCopiedCharacters()
	{
		var red = (await Parser.FunctionParse(MarkupText.Plain("[ansi(r,ab)]")))!.Message!.Render(MarkupFormat.Ansi);
		var redThenX = (await Parser.FunctionParse(MarkupText.Plain("[ansi(r,ab)]x")))!.Message!.Render(MarkupFormat.Ansi);

		var transformed = await Parser.FunctionParse(MarkupText.Plain(@"foreach(#lambda/\%0,[ansi(r,ab)])"));
		await Assert.That(transformed!.Message!.Render(MarkupFormat.Ansi)).IsEqualTo(red);

		var copied = await Parser.FunctionParse(MarkupText.Plain("foreach(#lambda/x,[ansi(r,ab)]-c,-)"));
		await Assert.That(copied!.Message!.Render(MarkupFormat.Ansi)).IsEqualTo(redThenX);
	}

	/// <summary>
	/// A missing attribute is refused rather than silently producing the input back.
	/// </summary>
	[Test]
	public async Task ForeachRefusesAnAttributeThatDoesNotExist()
	{
		var result = await Parser.FunctionParse(MarkupText.Plain("foreach(me/NOSUCHFOREACHATTR,abc)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("#-1 NO SUCH ATTRIBUTE");
	}

	[Test]
	[Arguments("match(a b c,b)", "2")]
	public async Task Match(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	[Arguments("json_map(obj/attr,{\"a\":1})", "")]
	public async Task JsonMap(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("malias(%#)", "")]
	public async Task Malias(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("message(test)", "")]
	public async Task Message(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("prompt(%#,test)", "")]
	public async Task Prompt(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("mwhoid()", "")]
	public async Task Mwhoid(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("nmwho()", "")]
	public async Task Nmwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("zwho()", "")]
	public async Task Zwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("ceil(3.14)", "4")]
	public async Task Ceil(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("atan2(1,1)", "")]
	public async Task Atan2(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("power(2,3)", "8")]
	public async Task Power(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("remainder(10,3)", "1")]
	public async Task Remainder(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	[Arguments("ctu(3.5,5)", "4")]
	public async Task Ctu(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("dec(100)", "99")]
	public async Task Dec(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("folderstats()", "")]
	public async Task Folderstats(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("open(exit,#0)", "")]
	public async Task Open(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	/// <summary>
	/// <c>fun_tel</c> (<c>src/fundb.c:2309-2327</c>) is <c>do_teleport</c>'s side effect exposed as a
	/// function and writes nothing to the buffer, so the answer is the empty string and the evidence it
	/// did anything is where the victim ended up. Asserting <c>IsNotNull</c> on the answer caught
	/// neither: it passed while the function answered <c>"1"</c> and moved nobody.
	/// </summary>
	[Test]
	public async Task Tel()
	{
		var result = await Parser.FunctionParse(MarkupText.Plain("tel(%#,#0)"));

		await Assert.That(result!.Message!.ToPlainText()).IsEmpty();

		var location = await Parser.FunctionParse(MarkupText.Plain("loc(%#)"));

		// loc() answers with an objid, and #0's creation stamp is not what is under test here.
		await Assert.That(location!.Message!.ToPlainText().Trim().Split(':')[0]).IsEqualTo("#0");
	}

	[Test]
	[Arguments("wipe(%#/testattr)", "")]
	public async Task Wipe(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("fullalias(%#)", "")]
	public async Task Fullalias(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cmsgs(channelname)", "")]
	public async Task Cmsgs(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cbuffer(channelname)", "")]
	public async Task Cbuffer(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cbufferadd(channelname,msg)", "")]
	public async Task Cbufferadd(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cdesc(channelname)", "")]
	public async Task Cdesc(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("clflags(channelname)", "")]
	public async Task Clflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cmogrifier(channelname)", "")]
	public async Task Cmogrifier(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("ctitle(channelname,%#)", "")]
	public async Task Ctitle(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("cusers(channelname)", "")]
	public async Task Cusers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("zfun(#0,func,arg)", "")]
	public async Task Zfun(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("zmwho()", "")]
	public async Task Zmwho(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("zone(#0)", "#-1")]
	public async Task Zone(string str, string expected)
	{
		TestDiagnostics.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}
}
