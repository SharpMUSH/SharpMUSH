using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>tagwrap()</c> and <c>cmdlink()</c> put markup on a string for the renderer to write per client.
/// Written into the text instead, a tag is escaped like any other &lt; and every client shows it
/// literally — which is how <c>+help</c> came to print <c>&lt;a xch_cmd=…&gt;</c> at an MXP client.
/// </summary>
public class MarkupTagFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private Mediator.IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();

	private async Task<MString> Eval(string code) =>
		(await FunctionParser.FunctionParse(MarkupText.Plain(code)))!.Message!;

	private async Task<MString> ThinkAs(TestIsolationHelpers.TestPlayer who, string code)
	{
		var before = WebAppFactoryArg.Notifications.RawCountFor(who.DbRef);
		await CommandParser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain($"think {code}"));
		return WebAppFactoryArg.Notifications.RawFor(who.DbRef).Skip(before)
			.Select(m => m switch
			{
				MString markup => markup,
				string text => MarkupText.Plain(text),
			})
			.Last();
	}

	private Task<TestIsolationHelpers.TestPlayer> MortalAsync(string name) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, name);

	[Test]
	public async Task Tagwrap_IsMarkupOnTheString_NotTextInIt()
	{
		var wrapped = await Eval("tagwrap(b,text)");

		await Assert.That(wrapped.ToPlainText()).IsEqualTo("text")
			.Because("a client that renders no tags reads the string alone");
		await Assert.That(wrapped.Render(MarkupFormat.Pueblo)).IsEqualTo("<b>text</b>")
			.Because("a Pueblo client receives the tag as a tag, not as escaped &lt;b&gt;");
		await Assert.That((await Eval("strlen(tagwrap(b,text))")).ToPlainText()).IsEqualTo("4")
			.Because("PennMUSH's ansi_strlen skips the tag, so strlen() counts only the string");
	}

	[Test]
	[Arguments("tagwrap(img src=x onerror=alert%(1%),x)")]
	[Arguments("tagwrap(b>,x)")]
	[Arguments("tagwrap(,x)")]
	public async Task Tagwrap_RefusesANameThatIsNotATagName(string code)
	{
		await Assert.That((await Eval(code)).ToPlainText()).IsEqualTo(ErrorMessages.Returns.InvalidTagName)
			.Because("a name carrying a space or > would smuggle attributes into the tag, even for a wizard");
	}

	[Test]
	public async Task Tagwrap_ForAMortal_RefusesATagOffPennMUSHsList()
	{
		var mortal = await MortalAsync("TagScriptMortal");

		var said = await ThinkAs(mortal, "tagwrap(script,alert(1))");

		await Assert.That(said.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	// PennMUSH's ok_tag_attribute: XCH_CMD and SEND belong to Send_OOB.
	[Arguments("tagwrap(a,xch_cmd=\"@destroy me\",Click)", "<a>Click</a>")]
	[Arguments("tagwrap(a,send=\"@destroy me\",Click)", "<a>Click</a>")]
	// The portal renders this in a browser: no script-bearing scheme, handler or style.
	[Arguments("tagwrap(a,href=\"javascript:alert%(1%)\",Click)", "<a>Click</a>")]
	[Arguments("tagwrap(a,href=\"java\tscript:alert%(1%)\",Click)", "<a>Click</a>")]
	[Arguments("tagwrap(a,href=\"https://example.test/\" onclick=\"alert%(1%)\",Click)", "<a>Click</a>")]
	[Arguments("tagwrap(span,style=\"position:fixed\",Click)", "<span>Click</span>")]
	// A value is written back quoted and encoded, so it cannot close the tag.
	[Arguments("tagwrap(font,color=red\"><b,Hi)", "<font color=\"red&quot;&gt;&lt;b\">Hi</font>")]
	[Arguments("tagwrap(font,color=\"red\",Hi)", "<font color=\"red\">Hi</font>")]
	[Arguments("tagwrap(a,href='https://example.test/' title=\"Go\",Click)", "<a href=\"https://example.test/\" title=\"Go\">Click</a>")]
	public async Task Tagwrap_ForAMortal_KeepsOnlyParametersABrowserCannotRun(string code, string html)
	{
		var mortal = await MortalAsync("TagParamMortal");

		var said = await ThinkAs(mortal, code);

		await Assert.That(said.Render(MarkupFormat.Html)).IsEqualTo(html);
	}

	[Test]
	public async Task Tagwrap_ForAWizard_KeepsTheParametersAsWritten()
	{
		var wrapped = await Eval("tagwrap(a,xch_cmd=\"+help scene\",Read it)");

		await Assert.That(wrapped.Render(MarkupFormat.Pueblo)).IsEqualTo("<a xch_cmd=\"+help scene\">Read it</a>");
	}

	[Test]
	public async Task Cmdlink_IsWrittenInEachClientsOwnDialect()
	{
		var link = await Eval("cmdlink(Read it,+help scene)");

		await Assert.That(link.Render(MarkupFormat.Pueblo)).IsEqualTo("<A XCH_CMD=\"+help scene\" XCH_HINT=\"+help scene\">Read it</A>");
		await Assert.That(link.Render(MarkupFormat.Mxp)).IsEqualTo("<SEND HREF=\"+help scene\" HINT=\"+help scene\">Read it</SEND>");
		await Assert.That(link.Render(MarkupFormat.Html)).Contains("xch_cmd=\"+help scene\"")
			.Because("the portal's terminal runs an anchor carrying xch_cmd");
		await Assert.That(link.ToPlainText()).IsEqualTo("Read it");
		await Assert.That((await Eval("strlen(cmdlink(Read it,+help scene))")).ToPlainText()).IsEqualTo("7");
	}

	[Test]
	public async Task Cmdlink_EncodesTheCommandAndTheHint()
	{
		var link = await Eval("cmdlink(Say it,say \"hi\" & bye,A \"quoted\" hint)");

		await Assert.That(link.Render(MarkupFormat.Pueblo))
			.IsEqualTo("<A XCH_CMD=\"say &quot;hi&quot; &amp; bye\" XCH_HINT=\"A &quot;quoted&quot; hint\">Say it</A>");
	}

	[Test]
	public async Task Cmdlink_RefusesALineBreakInTheCommand()
	{
		await Assert.That((await Eval("cmdlink(Click,look%r@destroy me)")).ToPlainText())
			.IsEqualTo(ErrorMessages.Returns.InvalidArgument)
			.Because("the client would send the text after the break as a second command");
	}

	[Test]
	public async Task Cmdlink_ForAMortal_IsDenied()
	{
		var mortal = await MortalAsync("CmdlinkMortal");

		var said = await ThinkAs(mortal, "cmdlink(Click,@destroy me)");

		await Assert.That(said.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("PennMUSH gives XCH_CMD only to Can_Send_OOB, and a command link is the same thing");
	}

	[Test]
	public async Task Cmdlink_WithTheSendOobPower_IsAllowed()
	{
		var mortal = await MortalAsync("CmdlinkOobMortal");
		await CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {mortal.DbRef}=Send_OOB"));

		var said = await ThinkAs(mortal, "cmdlink(Click,look)");

		await Assert.That(said.Render(MarkupFormat.Mxp)).Contains("<SEND HREF=\"look\"");
	}
}
