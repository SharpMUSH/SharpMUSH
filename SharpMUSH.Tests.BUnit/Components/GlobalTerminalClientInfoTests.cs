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
/// The terminal tells the game what it is (its terminal types and the browser's time zone) once per connection,
/// whether the connection came up while the view was on the page or before it was, as it does when creating a
/// character connects the play terminal and then opens Play.
/// </summary>
public class GlobalTerminalClientInfoTests : BunitContext
{
	private const string Zone = "Europe/Amsterdam";

	public GlobalTerminalClientInfoTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<CommandHistory>();
		Services.AddSingleton<ScreenReaderMode>();
		Services.AddSingleton<TerminalLog>();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(false));
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("SharpMUSH.timeZone").SetResult(Zone);

		var hostEnv = Substitute.For<IWebAssemblyHostEnvironment>();
		hostEnv.Environment.Returns("Production");
		Services.AddSingleton(hostEnv);

		Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(sp => new AccountAuthService(
			sp.GetRequiredService<IHttpClientFactory>(),
			sp.GetRequiredService<IJSRuntime>(),
			NullLogger<AccountAuthService>.Instance, []));

		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
	}

	private static ITerminalService Terminal(bool connected)
	{
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(connected);
		terminal.Lines.Returns([]);
		return terminal;
	}

	[Test]
	public void A_terminal_connected_before_the_view_opened_is_described_when_it_opens()
	{
		var terminal = Terminal(connected: true);

		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));

		cut.WaitForAssertion(() =>
		{
			terminal.Received(1).SendControlAsync(TerminalInput.TerminalTypesFrame(screenReader: false));
			terminal.Received(1).SendControlAsync(TerminalInput.TimeZoneFrame(Zone));
		});
	}

	[Test]
	public async Task A_terminal_that_connects_while_the_view_is_open_is_described_once()
	{
		var terminal = Terminal(connected: false);
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		await Assert.That(terminal.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(ITerminalService.SendControlAsync)))
			.IsFalse().Because("nothing is connected to tell");

		terminal.IsConnected.Returns(true);
		await cut.InvokeAsync(() => terminal.ConnectionStateChanged += Raise.Event<Action<bool>>(true));

		cut.WaitForAssertion(() =>
		{
			terminal.Received(1).SendControlAsync(TerminalInput.TerminalTypesFrame(screenReader: false));
			terminal.Received(1).SendControlAsync(TerminalInput.TimeZoneFrame(Zone));
		});
	}
}
