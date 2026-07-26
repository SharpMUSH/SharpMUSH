using Bunit;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Covers the guest entry into <see cref="GlobalTerminal"/>: an anonymous visitor on a terminal that
/// permits guests (<c>AllowGuest</c>) auto-connects as a game guest — but only when the server
/// actually accepts guest logins (<c>Net.Guests</c>, surfaced anonymously via
/// <see cref="ServerInfoService"/>). No login, no seeded session: Loose-mode <c>getItem</c> reads
/// return null so <see cref="AccountAuthService"/> stays anonymous throughout.
/// </summary>
public class GlobalTerminalGuestTests : BunitContext
{
	public GlobalTerminalGuestTests()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;

		var hostEnv = Substitute.For<IWebAssemblyHostEnvironment>();
		hostEnv.Environment.Returns("Production");
		Services.AddSingleton(hostEnv);

		Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(sp => new AccountAuthService(
			sp.GetRequiredService<IHttpClientFactory>(),
			sp.GetRequiredService<IJSRuntime>(),
			NullLogger<AccountAuthService>.Instance, Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>()));

		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
	}

	private static ITerminalService AnonymousTerminal()
	{
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(false);
		terminal.Lines.Returns([]);
		return terminal;
	}

	[Test]
	public void Anonymous_visitor_auto_connects_as_guest_when_guests_are_enabled()
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true));
		var terminal = AnonymousTerminal();

		var cut = Render<GlobalTerminal>(p => p
			.Add(g => g.Terminal, terminal)
			.Add(g => g.AllowGuest, true));

		cut.WaitForAssertion(() => terminal.Received(1).ConnectAsGuestAsync(Arg.Any<string>()));
	}

	[Test]
	public async Task Anonymous_visitor_does_not_guest_connect_when_guests_are_disabled()
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(false));
		var terminal = AnonymousTerminal();

		Render<GlobalTerminal>(p => p
			.Add(g => g.Terminal, terminal)
			.Add(g => g.AllowGuest, true));

		await terminal.DidNotReceive().ConnectAsGuestAsync(Arg.Any<string>());
	}

	[Test]
	public async Task Guest_connect_is_not_attempted_without_AllowGuest()
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true));
		var terminal = AnonymousTerminal();

		Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));

		await terminal.DidNotReceive().ConnectAsGuestAsync(Arg.Any<string>());
	}
}
