using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// A <c>portal</c>-class connection is a background query connection the web portal opens; it must
/// not make its character look connected to a mortal viewer (mirroring how DARK sessions are hidden),
/// while a wizard viewer still sees it. A default <c>play</c> connection is unaffected. These drive
/// <c>lwho()</c> — the primitive behind WHO and the portal's online widget (GET`ONLINE) — against the
/// real connection registry, asserting the mortal/wizard split for both presence classes.
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

	private async Task<string[]> ListWhoAsAsync(string? lookerRef)
	{
		var code = lookerRef is null ? "lwho()" : $"lwho({lookerRef})";
		var result = (await Parser.FunctionParse(MModule.single(code)))!.Message!.ToPlainText();
		return result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
	}

	[Test, NotInParallel(nameof(PortalPresenceTests))]
	public async Task PortalOnlyConnection_HiddenFromMortal_VisibleToWizard()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var portalRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "PortalOnly");
		var mortalRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "PortalMortalLooker");
		var handle = await ConnectAsAsync(portalRef, "portal");

		try
		{
			// Wizard viewer (#1 / God, the FunctionParser executor) sees the portal-only character.
			var wizardWho = await ListWhoAsAsync(null);
			await Assert.That(wizardWho).Contains($"#{portalRef.Number}");

			// Mortal viewer does not — the character's only session is portal-class.
			var mortalWho = await ListWhoAsAsync($"#{mortalRef.Number}");
			await Assert.That(mortalWho).DoesNotContain($"#{portalRef.Number}");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}

	[Test, NotInParallel(nameof(PortalPresenceTests))]
	public async Task PlayConnection_VisibleToMortalAndWizard()
	{
		var services = WebAppFactoryArg.Services;
		var mediator = services.GetRequiredService<IMediator>();
		var connectionService = services.GetRequiredService<IConnectionService>();

		var playRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "PlayTarget");
		var mortalRef = await TestIsolationHelpers.CreateTestPlayerAsync(services, mediator, "PlayMortalLooker");
		var handle = await ConnectAsAsync(playRef, "play");

		try
		{
			var wizardWho = await ListWhoAsAsync(null);
			await Assert.That(wizardWho).Contains($"#{playRef.Number}");

			var mortalWho = await ListWhoAsAsync($"#{mortalRef.Number}");
			await Assert.That(mortalWho).Contains($"#{playRef.Number}");
		}
		finally
		{
			await connectionService.Disconnect(handle);
		}
	}
}
