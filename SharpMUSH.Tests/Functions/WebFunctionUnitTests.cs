using System.Text;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class WebFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// PennMUSH urlencode() uses libcurl's curl_easy_escape: RFC 3986 percent-encoding,
	// space -> %20 (not +), only A-Za-z0-9-._~ left unescaped, hex digits uppercased.
	// (\% in the input keeps a percent literal past the MUSH substitution layer.)
	[Test]
	[Arguments(@"urlencode(test string)", "test%20string")]
	[Arguments(@"urlencode(a/b c)", "a%2Fb%20c")]
	[Arguments(@"urlencode(a+b)", "a%2Bb")]
	[Arguments(@"urlencode(a&b=c)", "a%26b%3Dc")]
	[Arguments(@"urlencode(foo-_.~bar)", "foo-_.~bar")]
	[Arguments(@"urlencode(100\%)", "100%25")]
	public async Task Urlencode(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// PennMUSH urldecode() uses libcurl's curl_easy_unescape: only %XX is decoded; a literal
	// '+' is left untouched (unlike form decoding); non-printable decoded bytes become '?'.
	[Test]
	[Arguments(@"urldecode(test\%20string)", "test string")]
	[Arguments(@"urldecode(a\%2Fb\%20c)", "a/b c")]
	[Arguments(@"urldecode(test+string)", "test+string")]
	[Arguments(@"urldecode(a\%2Fb+c)", "a/b+c")]
	[Arguments(@"urldecode(100\%25)", "100%")]
	[Arguments(@"urldecode(a\%09b)", "a?b")]
	public async Task Urldecode(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// wshtml()/wsjson() embed their payload in the value they return, for a later emit to deliver —
	/// they do not send anything themselves and argument two is the plain-text fallback, not a target
	/// player (help WSHTML, PennMUSH src/websock.c:648). The value a listener without a WebSocket
	/// reads is exactly that fallback.
	/// </summary>
	[Test]
	[Arguments("wshtml(<b>test</b>,plain test)", "plain test")]
	[Arguments("wsjson({\"test\":\"value\"},plain test)", "plain test")]
	// With no payload there is no channel region, so the fallback stands alone; with no fallback
	// there is no text for the layer to cover, and nothing is carried.
	[Arguments("wshtml(,plain test)", "plain test")]
	[Arguments("wshtml(<b>test</b>)", "")]
	[Arguments("wsjson({\"test\":\"value\"})", "")]
	public async Task WebSocketFunctionsReturnTheFallbackAsTheirText(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("wshtml(<b>test</b>,plain test)", "html", "<b>test</b>")]
	// A brace-wrapped argument is a MUSH grouping and the braces come off before the function ever
	// sees it, so a JSON object literal reaches wsjson() through lit().
	[Arguments("wsjson([lit({\"test\":\"value\"})],plain test)", "json", "{\"test\":\"value\"}")]
	public async Task WebSocketFunctionsCarryThePayloadAsAMarkupLayer(string str, string channel, string data)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))!.Message!;

		await Assert.That(result.Runs.Length).IsEqualTo(1);
		await Assert.That(result.Runs[0].Markups.OfType<WebSocketMarkup>().Single())
			.IsEqualTo(new WebSocketMarkup(channel, data));
	}

	/// <summary>
	/// The one emission reaches both readings. A renderer that knows nothing of the kind — the socket
	/// owner's, which does not register the codec — still hands a terminal the fallback and only the
	/// fallback, because the payload rides on the layer and never in the text.
	/// </summary>
	[Test]
	public async Task OneEmissionCarriesBothTheFallbackAndTheWebSocketPayload()
	{
		var emitted = (await Parser.FunctionParse(MarkupText.Plain("wshtml(<b>test</b>,plain test)")))!.Message!;
		var wire = MarkupTextSerializer.Serialize(emitted);
		var socketOwner = MarkupRegistry.Empty.WithAnsi().WithHtml();

		var terminal = new MarkupOutputRenderer().Render(wire,
			new RenderContext("telnet", new ProtocolCapabilities(), null));
		await Assert.That(Encoding.UTF8.GetString(terminal.Data)).IsEqualTo("plain test");

		await Assert.That(MarkupTextSerializer.Deserialize(wire, socketOwner).Render(MarkupFormat.Ansi))
			.IsEqualTo("plain test");

		// The portal is handed the markup itself inside the out-of-band envelope, payload and all.
		var portal = new MarkupOutputRenderer().Render(wire,
			new RenderContext(MarkupOutputRenderer.WebSocketConnectionType, new ProtocolCapabilities(), null));
		var envelope = JsonDocument.Parse(Encoding.UTF8.GetString(portal.Data)).RootElement;
		await Assert.That(envelope.GetProperty("type").GetString()).IsEqualTo("markup");
		var delivered = MarkupTextSerializer.Deserialize(envelope.GetProperty("data").GetString()!);
		await Assert.That(delivered.Runs[0].Markups.OfType<WebSocketMarkup>().Single())
			.IsEqualTo(new WebSocketMarkup(WebSocketMarkup.HtmlChannel, "<b>test</b>"));
	}

	/// <summary>
	/// PennMUSH gates both on <c>Can_Pueblo_Send</c> — a wizard or the Send_OOB power
	/// (<c>hdrs/mushdb.h:61</c>). The test fixtures run as God, so this has to drive a mortal.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task AMortalWithoutSendOobCannotEmbedWebSocketMarkup()
	{
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var connections = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, mediator, connections, "WsGate");

		var refused = await WebAppFactoryArg.FunctionParserFor(mortal.DbRef)
			.FunctionParse(MarkupText.Plain("wshtml(<b>test</b>,plain test)"));
		await Assert.That(refused!.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);

		// The control: God is a wizard, so the same call answers.
		var allowed = await Parser.FunctionParse(MarkupText.Plain("wshtml(<b>test</b>,plain test)"));
		await Assert.That(allowed!.Message!.ToPlainText()).IsEqualTo("plain test");
	}

	/// <summary>Penn's <c>#-1 NESTED TAG</c>: a payload that already carries markup is refused.</summary>
	[Test]
	public async Task APayloadThatAlreadyCarriesMarkupIsRefused()
	{
		var result = await Parser.FunctionParse(MarkupText.Plain("wshtml([tagwrap(b,test)],plain test)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.NestedTag);
	}

	[Test]
	[Arguments("oob(test)", "")]
	public async Task Oob(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("oob(me, room.contents)", "0")]
	public async Task OobNoConnectionReturnsZero(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("pueblo()", "0")]
	public async Task Pueblo(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("ssl(%#)", "0")]
	public async Task Ssl(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("terminfo(%#)", "")]
	public async Task Terminfo(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("width(%#)", "78")]
	public async Task Width(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
