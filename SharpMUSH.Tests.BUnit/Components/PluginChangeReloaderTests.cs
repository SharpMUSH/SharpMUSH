using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>A test double for the connection that lets the test raise OnPluginsChanged on demand.</summary>
file sealed class FakeConnectionStateService : IConnectionStateService
{
	public bool IsConnected => false;
	public HubConnectionState ConnectionState => HubConnectionState.Disconnected;
	public event Action? OnConnectionStateChanged;
	public event Action<GameOutputMessage>? OnOutputReceived;
	public event Action<RoomEventMessage>? OnRoomEventReceived;
	public event Action? OnPluginsChanged;

	public Task ConnectAsync(string accessToken) => Task.CompletedTask;
	public Task DisconnectAsync() => Task.CompletedTask;
	public Task SendCommandAsync(string command) => Task.CompletedTask;

	public void RaisePluginsChanged() => OnPluginsChanged?.Invoke();

	// Keep the compiler from warning about unused events on the double.
	public void Touch()
	{
		OnConnectionStateChanged?.Invoke();
		OnOutputReceived?.Invoke(default!);
		OnRoomEventReceived?.Invoke(default!);
	}
}

/// <summary>
/// Proves the client half of the forced-refresh chain: when the GameHub raises the generic
/// <c>OnPluginsChanged</c> signal (server broadcast of <c>ReceivePluginsChanged</c>), the
/// <see cref="PluginChangeReloader"/> forces a hard reload via <c>NavigationManager.NavigateTo(uri,
/// forceLoad: true)</c>. bUnit's FakeNavigationManager records the navigation (including the forceLoad flag).
/// </summary>
public class PluginChangeReloaderTests : BunitContext
{
	[TUnit.Core.Test]
	public async Task PluginsChanged_ForcesHardReload()
	{
		Services.AddMudServices();
		var connection = new FakeConnectionStateService();
		Services.AddSingleton<IConnectionStateService>(connection);

		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

		Render<PluginChangeReloader>();

		// No navigation yet — only an actual plugin-change signal must trigger the reload.
		await Assert.That(nav.History.Count).IsEqualTo(0);

		connection.RaisePluginsChanged();

		await Assert.That(nav.History.Count).IsEqualTo(1).Because("a plugins-changed signal forces exactly one reload");
		var entry = nav.History.Single();
		await Assert.That(entry.Options.ForceLoad).IsTrue().Because("the reload must be a hard, forceLoad reload");
	}
}
