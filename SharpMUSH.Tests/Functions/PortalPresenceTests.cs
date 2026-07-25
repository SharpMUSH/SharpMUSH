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
/// A <c>portal</c>-class connection is excluded from the mortal WHO (<c>mwho()</c>, what GET`ONLINE
/// uses) regardless of caller, but still listed by the raw <c>lwho()</c>; a <c>play</c> connection
/// appears in both.
/// </summary>
public class PortalPresenceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// Session-shared connection registry, so handles must not collide with other tests
	// (TestIsolationHelpers hands out handles from 100 upward).
	private static long _handleSeq = 900_000;

	private static ConcurrentDictionary<string, string> ConnectionMetadata(string presenceClass) =>
		new(new Dictionary<string, string>
		{
			["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
			["InternetProtocolAddress"] = "127.0.0.1",
			["HostName"] = "localhost",
			["ConnectionType"] = "websocket",
			["PresenceClass"] = presenceClass
		});

	private async Task<long> ConnectAsAsync(DBRef player, string presenceClass)
	{
		var connectionService = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var handle = Interlocked.Increment(ref _handleSeq);
		await connectionService.Register(handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			ConnectionMetadata(presenceClass));
		await connectionService.Bind(handle, player);
		return handle;
	}

	private async Task<string[]> WhoAsync(string function)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))!.Message!.ToPlainText();
		return result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
	}

	[Test, NotInParallel(nameof(PortalPresenceTests))]
	public async Task PortalConnection_ExcludedFromMwho_ButListedByLwho()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var portalRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "PortalOnly");
		var handle = await ConnectAsAsync(portalRef, PresenceClasses.Portal);

		try
		{
			var mortalWho = await WhoAsync("mwho()");
			await Assert.That(mortalWho).DoesNotContain($"#{portalRef.Number}");

			var rawWho = await WhoAsync("lwho()");
			await Assert.That(rawWho).Contains($"#{portalRef.Number}");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	[Test, NotInParallel(nameof(PortalPresenceTests))]
	public async Task PlayConnection_ListedByMwho()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "PlayTarget");
		var handle = await ConnectAsAsync(playRef, PresenceClasses.Play);

		try
		{
			var mortalWho = await WhoAsync("mwho()");
			await Assert.That(mortalWho).Contains($"#{playRef.Number}");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}
}
