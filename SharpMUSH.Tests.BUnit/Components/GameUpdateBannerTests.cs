using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// <see cref="GameUpdateBanner"/> asks the server which build it serves when the game connection comes back,
/// and offers a reload when that is not the build this tab started with. It never reloads on its own.
/// </summary>
public class GameUpdateBannerTests : BunitContext
{
	/// <summary>A connection whose state the test moves by hand.</summary>
	private sealed class SteppedConnection : IConnectionStateService
	{
		public HubConnectionState ConnectionState { get; private set; } = HubConnectionState.Disconnected;
		public bool IsConnected => ConnectionState == HubConnectionState.Connected;
		public event Action? OnConnectionStateChanged;
		public event Action<GameOutputMessage>? OnOutputReceived { add { } remove { } }
		public event Action<RoomEventMessage>? OnRoomEventReceived { add { } remove { } }
		public event Action? OnPluginsChanged { add { } remove { } }

		public Task ConnectAsync() => Task.CompletedTask;
		public Task DisconnectAsync() => Task.CompletedTask;
		public Task ReconnectAsync() => Task.CompletedTask;

		public void Step(params HubConnectionState[] states)
		{
			foreach (var state in states)
			{
				ConnectionState = state;
				OnConnectionStateChanged?.Invoke();
			}
		}
	}

	/// <summary>The server's build, as the test says it is: the first answer, then whatever <see cref="Now"/> holds.</summary>
	private sealed class BuildInfo(string? first) : ServerInfoService(null!)
	{
		public string? Now { get; set; } = first;
		public override Task<string?> BuildIdAsync() => Task.FromResult(first);
		public override Task<string?> CurrentBuildIdAsync() => Task.FromResult(Now);
	}

	/// <summary>Mounts the banner on a tab that is connected to the game, as most are when a deploy happens.</summary>
	private (SteppedConnection Connection, BuildInfo Info, IRenderedComponent<GameUpdateBanner> Banner) Mount(string? first)
	{
		var (connection, info, banner) = MountDisconnected(first);
		connection.Step(HubConnectionState.Connecting, HubConnectionState.Connected);
		return (connection, info, banner);
	}

	private (SteppedConnection Connection, BuildInfo Info, IRenderedComponent<GameUpdateBanner> Banner) MountDisconnected(string? first)
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		var connection = new SteppedConnection();
		var info = new BuildInfo(first);
		Services.AddSingleton<IConnectionStateService>(connection);
		Services.AddSingleton<ServerInfoService>(info);
		return (connection, info, Render<GameUpdateBanner>());
	}

	private BunitNavigationManager Navigation => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

	[TUnit.Core.Test]
	public async Task TheSameBuildAfterAReconnectShowsNothing()
	{
		var (connection, _, banner) = Mount("build-1");

		connection.Step(HubConnectionState.Reconnecting, HubConnectionState.Connected);
		connection.Step(HubConnectionState.Disconnected, HubConnectionState.Connecting, HubConnectionState.Connected);

		await Assert.That(banner.FindAll(".game-update-banner").Count).IsEqualTo(0);
	}

	[TUnit.Core.Test]
	public async Task ANewBuildIsNotLookedForWhileTheConnectionHolds()
	{
		var (connection, info, banner) = Mount("build-1");
		info.Now = "build-2";

		connection.Step(HubConnectionState.Connected);

		await Assert.That(banner.FindAll(".game-update-banner").Count).IsEqualTo(0)
			.Because("only a connection that came back can be talking to a server that was deployed");
	}

	[TUnit.Core.Test]
	public async Task ANewBuildAfterAReconnectOffersAReload()
	{
		var (connection, info, banner) = Mount("build-1");
		info.Now = "build-2";

		connection.Step(HubConnectionState.Reconnecting, HubConnectionState.Connected);

		banner.WaitForElement(".game-update-banner");
		await Assert.That(banner.Find(".game-update-text").TextContent).IsEqualTo("WidGameUpdated");
		await Assert.That(Navigation.History.Count).IsEqualTo(0).Because("the reader decides when to reload");
	}

	[TUnit.Core.Test]
	public async Task ANewBuildAfterTheConnectionClosedOffersAReload()
	{
		var (connection, info, banner) = Mount("build-1");
		info.Now = "build-2";

		connection.Step(HubConnectionState.Disconnected, HubConnectionState.Connecting, HubConnectionState.Connected);

		banner.WaitForElement(".game-update-banner");
		await Assert.That(Navigation.History.Count).IsEqualTo(0);
	}

	[TUnit.Core.Test]
	public async Task ReloadForcesALoad()
	{
		var (connection, info, banner) = Mount("build-1");
		info.Now = "build-2";
		connection.Step(HubConnectionState.Reconnecting, HubConnectionState.Connected);

		await banner.WaitForElement(".game-update-reload").ClickAsync();

		await Assert.That(Navigation.History.Count).IsEqualTo(1);
		await Assert.That(Navigation.History.Single().Options.ForceLoad).IsTrue();
	}

	/// <summary>
	/// A tab that sat on the login page across a deploy has no game connection to lose. Its first connect, after
	/// logging in, is to the new server.
	/// </summary>
	[TUnit.Core.Test]
	public async Task ANewBuildAtTheFirstConnectOffersAReload()
	{
		var (connection, info, banner) = MountDisconnected("build-1");
		info.Now = "build-2";

		connection.Step(HubConnectionState.Connecting, HubConnectionState.Connected);

		banner.WaitForElement(".game-update-banner");
	}

	[TUnit.Core.Test]
	public async Task WithoutAFirstAnswerThereIsNothingToCompare()
	{
		var (connection, info, banner) = Mount(null);
		info.Now = "build-2";

		connection.Step(HubConnectionState.Reconnecting, HubConnectionState.Connected);

		await Assert.That(banner.FindAll(".game-update-banner").Count).IsEqualTo(0);
	}
}
