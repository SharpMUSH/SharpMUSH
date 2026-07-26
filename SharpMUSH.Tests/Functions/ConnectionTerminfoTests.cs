using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Text;

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

	private static long _handleSeq = 950_000;

	private async Task<long> ConnectAsAsync(DBRef player, string connectionType, string presenceClass = PresenceClasses.Play)
	{
		var connectionService = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var handle = Interlocked.Increment(ref _handleSeq);
		var metadata = new ConcurrentDictionary<string, string>(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = connectionType,
			["PresenceClass"] = presenceClass
		});
		await connectionService.Register(handle, "127.0.0.1", "localhost", connectionType,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, metadata);
		await connectionService.Bind(handle, player);
		return handle;
	}

	private async Task<string> TerminfoAsync(DBRef player) =>
		(await Parser.FunctionParse(MModule.single($"terminfo(#{player.Number})")))!.Message!.ToPlainText();

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
}
