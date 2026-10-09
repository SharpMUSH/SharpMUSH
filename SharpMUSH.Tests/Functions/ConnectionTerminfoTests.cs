using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Concurrent;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// terminfo() reports the connection transport as a capability token ("websocket" for a WebSocket
/// session), matching PennMUSH, so a WebSocket connection is distinguishable from a regular one.
/// </summary>
public class ConnectionTerminfoTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<long> ConnectAsAsync(DBRef player, string connectionType,
		string presenceClass = PresenceClasses.Play, string? terminalType = null, bool telnetNegotiated = false)
	{
		var connectionService = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var metadata = new ConcurrentDictionary<string, string>(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = connectionType,
			["PresenceClass"] = presenceClass
		});

		if (terminalType is not null)
		{
			metadata["TerminalType"] = terminalType;
		}

		if (telnetNegotiated)
		{
			metadata["TELNET"] = "1";
		}

		return await TestIsolationHelpers.ConnectTestHandleAsync(connectionService, player, connectionType, metadata);
	}

	private async Task<string> TerminfoAsync(DBRef player) =>
		(await Parser.FunctionParse(MarkupText.Plain($"terminfo(#{player.Number})")))!.Message!.ToPlainText();

	/// <summary>
	/// PennMUSH's <c>lookup_desc()</c> (src/bsd.c) falls back to
	/// <c>match_result(executor, name, TYPE_PLAYER, MAT_ABSOLUTE | MAT_PLAYER | MAT_ME | MAT_TYPE)</c>,
	/// so "me" is a valid argument to every connection function — and it is the one a player types.
	/// The player-name match SharpMUSH used carries no MAT_ME, so <c>terminfo(me)</c> answered
	/// "unknown" and, because it was the notifying variant of the match, said "I can't see that here."
	/// to the caller as well. Functions do not talk.
	/// </summary>
	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ResolvesMe_ToTheCallersOwnConnection()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "MeTermClient");
		var handle = await ConnectAsAsync(playerRef, "telnet", terminalType: "SharpMUTerm", telnetNegotiated: true);
		try
		{
			var asSelf = WebAppFactoryArg.FunctionParserFor(playerRef);
			var byMe = (await asSelf.FunctionParse(MarkupText.Plain("terminfo(me)")))!.Message!.ToPlainText();

			await Assert.That(byMe).StartsWith("SharpMUTerm");
			await Assert.That(byMe).IsEqualTo(await TerminfoAsync(playerRef))
				.Because("\"me\" and the dbref name the same descriptor");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	/// <summary>
	/// <c>fun_terminfo</c> emits the colour style for every caller — it sits after the has_privs block
	/// alongside pueblo and stripaccents — so a client that pinned nothing still learns what depth it
	/// is being rendered at. SharpMUSH returned early with the bare word "unknown" for an unprivileged
	/// caller, which made the documented "one of the color styles will also be included" untrue and
	/// left the includeDetails parameter unreachable.
	/// </summary>
	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_AlwaysReportsAColourStyle()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "StyleTermClient");
		var handle = await ConnectAsAsync(playerRef, "telnet");
		try
		{
			var info = await TerminfoAsync(playerRef);
			await Assert.That(info.Split(' ')).Contains(ColorStyles.SixteenColor)
				.Because("a client that was never asked is assumed to take the basic sixteen");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	/// <summary>
	/// Not PennMUSH tokens: what a connection is sent beyond colour, read the way the renderer reads it.
	/// A kitty terminal is sent hyperlinks; pictures and moving pictures once the player turns them on; a pin
	/// turns a link off.
	/// </summary>
	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ReportsTerminalFeatures()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "KittyTermClient");
		var handle = await ConnectAsAsync(playerRef, "telnet", terminalType: "XTERM-KITTY", telnetNegotiated: true);
		connectionService.Update(handle, TerminalCapabilityReader.TerminalTypesKey, "XTERM-KITTY");
		try
		{
			var tokens = (await TerminfoAsync(playerRef)).Split(' ');
			await Assert.That(tokens).Contains("hyperlinks");
			await Assert.That(tokens).DoesNotContain(TerminalGraphics.Kitty).Because("pictures are opt-in");

			connectionService.Update(handle, TerminalFeatureReader.GraphicsKey, TerminalGraphics.Auto);
			connectionService.Update(handle, TerminalFeatureReader.AnimationKey, "1");
			tokens = (await TerminfoAsync(playerRef)).Split(' ');
			await Assert.That(tokens).Contains(TerminalGraphics.Kitty);
			await Assert.That(tokens).Contains("animation");

			connectionService.Update(handle, TerminalFeatureReader.HyperlinksKey, "0");
			await Assert.That((await TerminfoAsync(playerRef)).Split(' ')).DoesNotContain("hyperlinks");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	/// <summary>
	/// A client that names MTTS SCREEN_READER (the portal's Screen reader mode sends it) is reported as
	/// "screenreader", to any caller, so softcode can leave out what only draws.
	/// </summary>
	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ReportsScreenReader()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "ReaderTermClient");
		var handle = await ConnectAsAsync(playerRef, "websocket", terminalType: "SHARPMUSH-PORTAL");
		connectionService.Update(handle, TerminalCapabilityReader.TerminalTypesKey, "SHARPMUSH-PORTAL\tUTF8");
		try
		{
			await Assert.That((await TerminfoAsync(playerRef)).Split(' ')).DoesNotContain("screenreader");

			connectionService.Update(handle, TerminalCapabilityReader.TerminalTypesKey, "SHARPMUSH-PORTAL\tUTF8\tSCREEN_READER");
			var tokens = (await TerminfoAsync(playerRef)).Split(' ');
			await Assert.That(tokens).Contains("screenreader");
			await Assert.That(tokens).DoesNotContain(ColorStyles.Plain)
				.Because("the browser draws a WebSocket connection's markup itself, colour included");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	/// <summary>
	/// A telnet screen reader is sent no colour and no links, since a reader would speak their escapes, and
	/// terminfo() says so.
	/// </summary>
	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ReportsATelnetScreenReader_AsPlain()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "TelnetReaderClient");
		var handle = await ConnectAsAsync(playerRef, "telnet", terminalType: "MUDLET", telnetNegotiated: true);
		connectionService.Update(handle, TerminalCapabilityReader.TerminalTypesKey, "MUDLET\tANSI\tMSLP\tSCREEN_READER");
		try
		{
			var tokens = (await TerminfoAsync(playerRef)).Split(' ');
			await Assert.That(tokens).Contains("screenreader");
			await Assert.That(tokens).Contains(ColorStyles.Plain);
			await Assert.That(tokens).DoesNotContain("commandlinks");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	/// <summary>
	/// A client with no MTTS option says it with the SCREENREADER command, and terminfo() reports it the same.
	/// </summary>
	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ReportsAPinnedScreenReader()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "PinnedReaderClient");
		var handle = await ConnectAsAsync(playerRef, "telnet", terminalType: "MUDLET", telnetNegotiated: true);
		connectionService.Update(handle, TerminalCapabilityReader.TerminalTypesKey, "MUDLET\tANSI");
		try
		{
			await Assert.That((await TerminfoAsync(playerRef)).Split(' ')).DoesNotContain("screenreader");

			connectionService.Update(handle, TerminalCapabilityReader.ScreenReaderKey, "1");
			var tokens = (await TerminfoAsync(playerRef)).Split(' ');
			await Assert.That(tokens).Contains("screenreader");
			await Assert.That(tokens).Contains(ColorStyles.Plain);
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	/// <summary>
	/// A colour flag can raise the depth above what the terminal negotiated — that is what setting
	/// XTERM256 on a character is for — so the reported style has to account for it. Reading the style
	/// out of terminal metadata alone told a "dumb"-terminal player with XTERM256 that they were on
	/// "hilite" while the wire carried 256-colour output, which is a capability claim softcode acts on.
	/// </summary>
	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ReportsTheStyleAPlayerFlagRaisesItTo()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "FlagStyleClient");
		var handle = await ConnectAsAsync(playerRef, "telnet", terminalType: "dumb", telnetNegotiated: true);
		connectionService.Update(handle, TerminalCapabilityReader.TerminalTypesKey, "dumb");
		try
		{
			await Assert.That((await TerminfoAsync(playerRef)).Split(' ')).Contains(ColorStyles.Hilite)
				.Because("a termcap that names no colour is hilite on its own");

			await Parser.CommandParse(1, connectionService,
				MarkupText.Plain($"@set #{playerRef.Number}=XTERM256"));

			await Assert.That((await TerminfoAsync(playerRef)).Split(' ')).Contains(ColorStyles.Xterm256)
				.Because("the flag raises the depth, so it has to raise the report with it");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ReportsWebsocket_ForWebSocketConnection()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "WsTermClient");
		var handle = await ConnectAsAsync(playerRef, "websocket");
		try
		{
			var info = await TerminfoAsync(playerRef);
			await Assert.That(info).Contains("websocket");
			await Assert.That(info).DoesNotContain("portal");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ReportsPortal_ForPortalClassConnection()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "PortalTermClient");
		var handle = await ConnectAsAsync(playerRef, "websocket", PresenceClasses.Portal);
		try
		{
			await Assert.That(await TerminfoAsync(playerRef)).Contains("portal");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_OmitsWebsocket_ForTelnetConnection()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "TelnetTermClient");
		var handle = await ConnectAsAsync(playerRef, "telnet");
		try
		{
			await Assert.That(await TerminfoAsync(playerRef)).DoesNotContain("websocket");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	/// <summary>
	/// The client name is the terminal type the connection negotiated (RFC 1091) or was given by
	/// <c>@sockset terminaltype</c> — the same "TerminalType" key SOCKSET reports, so the two cannot
	/// disagree about who the client is.
	/// </summary>
	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ReportsNegotiatedTerminalType_AsTheClient()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "TTypeTermClient");
		var handle = await ConnectAsAsync(playerRef, "telnet", terminalType: "SharpMUTerm", telnetNegotiated: true);
		try
		{
			var info = await TerminfoAsync(playerRef);
			await Assert.That(info).StartsWith("SharpMUTerm");
			await Assert.That(info).Contains("telnet")
				.Because("a client that answered TTYPE has demonstrably negotiated telnet");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	/// <summary>A client that never answers TTYPE stays "unknown", as PennMUSH documents.</summary>
	[Test, NotInParallel(nameof(ConnectionTerminfoTests))]
	public async Task Terminfo_ReportsUnknown_WhenNoTerminalTypeWasNegotiated()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "NoTTypeTermClient");
		var handle = await ConnectAsAsync(playerRef, "telnet");
		try
		{
			var info = await TerminfoAsync(playerRef);
			await Assert.That(info).StartsWith("unknown");
			await Assert.That(info).DoesNotContain("telnet")
				.Because("a raw socket on the telnet port has negotiated nothing, so it cannot claim telnet");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}
}
