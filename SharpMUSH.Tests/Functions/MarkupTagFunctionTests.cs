using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;

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

	// ── The shared vocabulary from softcode ──────────────────────────────────────

	/// <summary>
	/// One object, written for each client the way that client has it. A terminal is sent nothing at
	/// all, and nothing is left in the plain text for a listen pattern to match either.
	/// </summary>
	[Test]
	public async Task Sound_IsWrittenForEachClientInItsOwnWay()
	{
		var result = await Eval("sound(door.wav,80)");

		await Assert.That(result.Render(MarkupFormat.Mxp)).IsEqualTo("<SOUND door.wav V=80>");
		await Assert.That(result.Render(MarkupFormat.Pueblo))
			.IsEqualTo("<img xch_sound=\"play\" href=\"door.wav\" xch_volume=\"80\">");
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEmpty();
		await Assert.That(result.ToPlainText()).IsEmpty();
	}

	[Test]
	public async Task Music_PlaysOnItsOwnChannel()
	{
		var result = await Eval("music(theme.mid,,-1)");

		await Assert.That(result.Render(MarkupFormat.Mxp)).IsEqualTo("<MUSIC theme.mid L=-1>");
		await Assert.That(result.Render(MarkupFormat.Pueblo)).Contains("xch_sound=\"loop\"");
	}

	[Test]
	public async Task StopSound_SilencesTheChannelAskedFor()
	{
		await Assert.That((await Eval("stopsound(music)")).Render(MarkupFormat.Mxp)).IsEqualTo("<MUSIC Off>");
		await Assert.That((await Eval("stopsound()")).Render(MarkupFormat.Mxp)).IsEqualTo("<SOUND Off><MUSIC Off>");
		await Assert.That((await Eval("stopsound(sideways)")).ToPlainText()).IsEqualTo(ErrorMessages.Returns.InvalidArgument);
	}

	/// <summary>A picture stands in its description for a client that shows none, which is the point of writing one.</summary>
	[Test]
	public async Task Image_LeavesItsDescriptionForAClientWithNoPictures()
	{
		var result = await Eval("image(map.png,A map of the city,200)");

		await Assert.That(result.Render(MarkupFormat.Mxp)).IsEqualTo("<IMAGE map.png W=200>");
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo("A map of the city");
		await Assert.That(result.ToPlainText()).IsEqualTo("A map of the city");
	}

	[Test]
	public async Task Image_WithNoDescriptionLeavesItsAddress()
		=> await Assert.That((await Eval("image(map.png)")).Render(MarkupFormat.Ansi)).IsEqualTo("map.png");

	/// <summary>A picture inside a command link is a picture that runs a command, in every dialect.</summary>
	[Test]
	public async Task Image_InsideCmdlink_IsALink()
	{
		var result = await Eval("cmdlink(image(map.png,A map),look map)");

		await Assert.That(result.Render(MarkupFormat.Pueblo))
			.IsEqualTo("<A XCH_CMD=\"look map\" XCH_HINT=\"look map\"><img src=\"map.png\" alt=\"A map\"></A>");
	}

	[Test]
	public async Task Pane_KeepsItsTextForAClientWithNoPanes()
	{
		var result = await Eval("pane(North: the gate,map,The Map)");

		await Assert.That(result.Render(MarkupFormat.Mxp))
			.IsEqualTo("<FRAME map TITLE=\"The Map\"><DEST map>North: the gate</DEST>");
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo("North: the gate");
	}

	/// <summary>
	/// Softcode that draws its own columns can say so, the way align() and table() already do for
	/// themselves.
	/// </summary>
	[Test]
	public async Task Preformat_SaysTheSpacingIsTheLayout()
	{
		var result = await Eval("preformat(a%b%b1%rb%b%b2)");

		await Assert.That(result.Render(MarkupFormat.Pueblo)).IsEqualTo("<xch_mudtext>a  1\nb  2</xch_mudtext>");
		await Assert.That(result.Render(MarkupFormat.Html)).StartsWith("<pre");
		await Assert.That(result.Render(MarkupFormat.Ansi)).IsEqualTo("a  1\nb  2");
	}

	[Test]
	public async Task ClearScreenAndPrefetchAndExpire_AreWrittenWhereTheyAreUnderstood()
	{
		await Assert.That((await Eval("clearscreen()")).Render(MarkupFormat.Pueblo)).IsEqualTo("<xch_page clear=\"text\">");
		await Assert.That((await Eval("clearscreen()")).Render(MarkupFormat.Mxp)).IsEmpty();
		await Assert.That((await Eval("prefetch(https://example.test/map.png)")).Render(MarkupFormat.Pueblo))
			.Contains("<xch_prefetch href=\"https://example.test/map.png\"");
		await Assert.That((await Eval("expirelinks(exits)")).Render(MarkupFormat.Mxp)).IsEqualTo("<EXPIRE exits>");
	}

	[Test]
	[Arguments("sound(a.wav,101)")]
	[Arguments("sound(a.wav,,0)")]
	[Arguments("image(a.png,alt,0)")]
	public async Task AnArgumentOutOfRangeIsRefused(string code)
		=> await Assert.That((await Eval(code)).ToPlainText()).IsEqualTo(ErrorMessages.Returns.OutOfRange);

	/// <summary>
	/// Gated where <c>tagwrap()</c> is gated: these make a client fetch a file, play it, or wipe the
	/// screen. Laying text out does not, so <c>preformat()</c> is not gated.
	/// </summary>
	[Test]
	[Arguments("sound(door.wav)")]
	[Arguments("image(map.png,A map)")]
	[Arguments("clearscreen()")]
	[Arguments("prefetch(https://example.test/x.png)")]
	public async Task AMortalIsRefusedWhatMakesAClientFetchOrPlay(string code)
	{
		var mortal = await MortalAsync("VocabularyMortal");

		var said = await ThinkAs(mortal, code);

		await Assert.That(said.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	[Test]
	public async Task AMortalMayStillSayHowTextIsLaidOut()
	{
		var mortal = await MortalAsync("PreformatMortal");

		var said = await ThinkAs(mortal, "preformat(a%b%b1)");

		await Assert.That(said.ToPlainText()).IsEqualTo("a  1");
		await Assert.That(said.Render(MarkupFormat.Pueblo)).Contains("<xch_mudtext>");
	}

	/// <summary>
	/// <c>wshtml()</c> returns markup and sends nothing itself; whatever emits it decides what each client
	/// reads (#1120). The message is composed with the text around it and reaches the recipient as one
	/// value that still carries the element, so the HTML, Pueblo and MXP renderers write it as a tag and a
	/// plain client gets the text — the mixed-client behaviour PennMUSH's second argument was for.
	/// </summary>
	[Test]
	[Arguments("think ")]
	[Arguments("@pemit %#=")]
	public async Task Wshtml_IsComposedIntoTheEmissionAndRenderedPerClient(string emitter)
	{
		var marker = Marker();
		var mortal = await MortalAsync("WshtmlEmitMortal");

		var window = OpenWindow(mortal);
		await CommandParser.CommandParse(mortal.Handle, ConnectionService,
			MarkupText.Plain($"{emitter}[wshtml(<b>{marker}</b>)] and more"));
		var said = OwnOutput(mortal, window);

		await Assert.That(said.Render(MarkupFormat.Html)).IsEqualTo($"<b>{marker}</b> and more");
		await Assert.That(said.Render(MarkupFormat.Pueblo)).IsEqualTo($"<b>{marker}</b> and more");
		await Assert.That(said.Render(MarkupFormat.Ansi)).IsEqualTo($"\u001b[1m{marker}\u001b[0m and more");
		await Assert.That(said.Render(MarkupFormat.Plain)).IsEqualTo($"{marker} and more")
			.Because("a client that negotiated neither HTML nor Pueblo reads the text and never a literal tag");
	}

	/// <summary>
	/// Nothing reaches the player's notifications or their websocket (portal) connection, the channel
	/// out-of-band functions such as <c>oob()</c> write to. An <c>oob()</c> probe sent afterwards on the
	/// same connection shows the socket was being watched: everything published before it has arrived.
	/// </summary>
	[Test]
	public async Task Wshtml_SendsNothingByItself()
	{
		var marker = Marker();
		var probe = Marker();
		var mortal = await MortalAsync("WshtmlQuietMortal");
		var socket = TestIsolationHelpers.GenerateUniqueHandle();
		await ConnectionService.Register(socket, "localhost", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await ConnectionService.Bind(socket, mortal.DbRef);

		await using var nats = new NatsConnection(new NatsOpts
		{
			Url = $"nats://localhost:{WebAppFactoryArg.NatsTestServer.Instance.GetMappedPublicPort(4222)}"
		});
		var subject = NatsSubjects.For(typeof(WebSocketOutputMessage),
			WebAppFactoryArg.Services.GetRequiredService<NatsOptions>().SubjectPrefix);
		await using var published = await nats.SubscribeCoreAsync(subject,
			serializer: CompressingNatsSerializer<WebSocketOutputMessage>.Default);
		// The subscription is in place on the server before anything is published.
		await nats.PingAsync();

		var window = OpenWindow(mortal);
		await CommandParser.CommandParse(socket, ConnectionService,
			MarkupText.Plain($"think [null(wshtml(<b>{marker}</b>))]"));
		await CommandParser.CommandParse(socket, ConnectionService, MarkupText.Plain($"think [oob(%#,{probe})]"));

		var toSocket = new List<string>();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		await foreach (var message in published.Msgs.ReadAllAsync(timeout.Token))
		{
			if (message.Data is not { } output || output.Handle != socket) continue;
			if (output.Data.Contains(probe, StringComparison.Ordinal)) break;
			toSocket.Add(output.Data);
		}

		await Assert.That(toSocket).IsEmpty()
			.Because("the value is for whatever emits it; the function itself publishes nothing out of band");
		await Assert.That(WebAppFactoryArg.Notifications.RawFor(mortal.DbRef).Skip(window.Raw)
				.Any(message => TestHelpers.MessagePlainTextContains(message, marker)))
			.IsFalse().Because("the value is for whatever emits it; the function itself notifies nobody");
	}
}
