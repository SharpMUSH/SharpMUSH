using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
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

	/// <summary>
	/// The output of <paramref name="who"/>'s own <c>think</c>, in the form it was sent in.
	/// </summary>
	/// <remarks>
	/// Chosen by sender and recipient, never by position (#1247). This used to answer
	/// <c>.Last()</c> of everything that reached the mortal while the command ran, and test players
	/// share a room: another test's player leaving broadcasts "<c>X has left.</c>" into the same
	/// bucket, and under load that broadcast arrived after the think's own output and won.
	/// <c>Tagwrap_ForAMortal_KeepsOnlyParametersABrowserCannotRun</c> failed a full run with
	/// <c>SpkNestSp_… has left.</c> where it wanted <c>&lt;a&gt;Click&lt;/a&gt;</c>.
	/// <para>
	/// <c>think</c> notifies its own executor, so the one notification this test is entitled to read
	/// is the one the mortal sent to itself. Only
	/// <see cref="TestHelpers.NotificationRecorder.DeliveriesFor"/> can answer that — the raw index
	/// carries no sender — and the two indexes are not positionally aligned, because a localized
	/// notification is recorded as a delivery and not as a raw message. So the sender decides which
	/// messages are ours and the raw form is matched back to them by its text.
	/// </para>
	/// </remarks>
	private async Task<MString> ThinkAs(TestIsolationHelpers.TestPlayer who, string code)
	{
		var window = OpenWindow(who);
		await CommandParser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain($"think {code}"));
		return OwnOutput(who, window);
	}

	/// <summary>Where <paramref name="who"/>'s two notification indexes stand before a command runs.</summary>
	private (int Raw, int Delivered) OpenWindow(TestIsolationHelpers.TestPlayer who)
		=> (WebAppFactoryArg.Notifications.RawCountFor(who.DbRef),
			WebAppFactoryArg.Notifications.DeliveryCountFor(who.DbRef));

	/// <summary>
	/// The one message <paramref name="who"/> sent to itself since <paramref name="window"/>, read
	/// back in the form it was sent in. Anything another object put in the bucket is skipped.
	/// </summary>
	private MString OwnOutput(TestIsolationHelpers.TestPlayer who, (int Raw, int Delivered) window)
	{
		var fromSelf = WebAppFactoryArg.Notifications.DeliveriesFor(who.DbRef).Skip(window.Delivered)
			.Where(delivery => delivery.Sender == who.DbRef)
			.Select(delivery => delivery.Message)
			.ToHashSet(StringComparer.Ordinal);

		return WebAppFactoryArg.Notifications.RawFor(who.DbRef).Skip(window.Raw)
			.Select(message => message switch
			{
				MString markup => markup,
				string text => MarkupText.Plain(text),
			})
			.Single(message => fromSelf.Contains(message.ToPlainText()));
	}

	/// <summary>
	/// A marker unique to one test, so the content a test matches on can only be its own output
	/// (#1247). "Click" and "Hi" are the kind of text another test can plausibly also produce.
	/// </summary>
	private static string Marker() => $"Mk{Guid.NewGuid():N}"[..10];

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

	/// <summary>
	/// <c>MARK</c> stands in for a per-run marker in both the code and the expected HTML, so the
	/// wrapped text is unique to this test and the helper's content match can only find its own
	/// output (#1247). The attribute values are the subject of these cases and stay literal.
	/// </summary>
	[Test]
	// PennMUSH's ok_tag_attribute: XCH_CMD and SEND belong to Send_OOB.
	[Arguments("tagwrap(a,xch_cmd=\"@destroy me\",MARK)", "<a>MARK</a>")]
	[Arguments("tagwrap(a,send=\"@destroy me\",MARK)", "<a>MARK</a>")]
	// The portal renders this in a browser: no script-bearing scheme, handler or style.
	[Arguments("tagwrap(a,href=\"javascript:alert%(1%)\",MARK)", "<a>MARK</a>")]
	[Arguments("tagwrap(a,href=\"java\tscript:alert%(1%)\",MARK)", "<a>MARK</a>")]
	[Arguments("tagwrap(a,href=\"https://example.test/\" onclick=\"alert%(1%)\",MARK)", "<a>MARK</a>")]
	[Arguments("tagwrap(span,style=\"position:fixed\",MARK)", "<span>MARK</span>")]
	// A value is written back quoted and encoded, so it cannot close the tag.
	[Arguments("tagwrap(font,color=red\"><b,MARK)", "<font color=\"red&quot;&gt;&lt;b\">MARK</font>")]
	[Arguments("tagwrap(font,color=\"red\",MARK)", "<font color=\"red\">MARK</font>")]
	[Arguments("tagwrap(a,href='https://example.test/' title=\"Go\",MARK)", "<a href=\"https://example.test/\" title=\"Go\">MARK</a>")]
	public async Task Tagwrap_ForAMortal_KeepsOnlyParametersABrowserCannotRun(string code, string html)
	{
		var marker = Marker();
		var mortal = await MortalAsync("TagParamMortal");

		var said = await ThinkAs(mortal, code.Replace("MARK", marker, StringComparison.Ordinal));

		await Assert.That(said.Render(MarkupFormat.Html))
			.IsEqualTo(html.Replace("MARK", marker, StringComparison.Ordinal));
	}

	/// <summary>
	/// The race #1247 is about, made deterministic: a foreign broadcast lands in the mortal's bucket
	/// <em>after</em> its own think output, and the helper still answers the think output.
	/// </summary>
	/// <remarks>
	/// A race cannot be written as a test that fails first, so this drives the losing interleaving
	/// directly. The injected notification is what another test's player leaving the shared room
	/// really sends — the leaver's name and "has left.", from the leaver — and against the old
	/// <c>.Last()</c> helper it is exactly the value that came back and failed the assertion.
	/// </remarks>
	[Test]
	public async Task ThinkAs_IgnoresAForeignBroadcastThatLandsAfterTheThink()
	{
		var marker = Marker();
		var mortal = await MortalAsync("TagOwnOutputMortal");
		var intruder = await MortalAsync("TagIntruderMortal");
		var intruderObject = (await Mediator.Send(new GetObjectNodeQuery(intruder.DbRef))).Expect<AnySharpObject>();

		var window = OpenWindow(mortal);
		await CommandParser.CommandParse(mortal.Handle, ConnectionService,
			MarkupText.Plain($"think tagwrap(b,{marker})"));
		await WebAppFactoryArg.Services.GetRequiredService<INotifyService>().Notify(
			mortal.DbRef,
			MarkupText.Plain($"{intruderObject.Object().Name} has left."),
			intruderObject);

		await Assert.That(WebAppFactoryArg.Notifications.RawFor(mortal.DbRef).Skip(window.Raw).Count())
			.IsGreaterThan(1)
			.Because("the foreign broadcast has to really be inside the window, or this proves nothing");

		await Assert.That(OwnOutput(mortal, window).Render(MarkupFormat.Html)).IsEqualTo($"<b>{marker}</b>")
			.Because("the think output is the mortal's own notification; the broadcast is the intruder's, "
							 + "and it is the last thing in the bucket");
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

		var marker = Marker();

		var said = await ThinkAs(mortal, $"cmdlink({marker},look)");

		await Assert.That(said.Render(MarkupFormat.Mxp)).Contains("<SEND HREF=\"look\"");
		await Assert.That(said.ToPlainText()).IsEqualTo(marker)
			.Because("the label is unique to this run, so the helper cannot have matched another test's output");
	}
}
