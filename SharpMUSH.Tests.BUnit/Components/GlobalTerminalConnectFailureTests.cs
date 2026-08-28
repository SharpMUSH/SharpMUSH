using System.Net.WebSockets;
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
/// The connection gateway being unreachable must not be fatal. <see cref="GlobalTerminal"/> mounts
/// from MainLayout on nearly every page, so an exception escaping its initializer takes the whole
/// SPA to Blazor's "An unhandled error has occurred" bar — on the wiki, on the layout editor, on
/// pages that have nothing to do with the terminal. A refused socket is the ordinary state of a
/// gateway that is down or restarting, and the reconnect machinery behind
/// <see cref="WebSocketClientService"/> exists precisely because it is expected to recover.
/// <para>
/// The guest path is the one exercised here because it is the only auto-connect reachable without
/// faking an account session; all three initializer paths share one guard, so covering it covers
/// them. The manual Connect button was always guarded — this makes start-up agree with it.
/// </para>
/// </summary>
public class GlobalTerminalConnectFailureTests : BunitContext
{
	public GlobalTerminalConnectFailureTests()
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
			NullLogger<AccountAuthService>.Instance,
			Substitute.For<ITerminalService>(),
			Substitute.For<IPlayTerminalService>()));

		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true));
	}

	/// <summary>A terminal whose guest auto-connect fails the way an unreachable gateway fails.</summary>
	private static ITerminalService RefusingTerminal()
	{
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(false);
		terminal.Lines.Returns([]);
		terminal.ConnectAsGuestAsync(Arg.Any<string>())
			.Returns(Task.FromException(new WebSocketException("net_webstatus_ConnectFailure")));
		return terminal;
	}

	private IRenderedComponent<GlobalTerminal> RenderTerminal(ITerminalService terminal) =>
		Render<GlobalTerminal>(p => p
			.Add(g => g.Terminal, terminal)
			.Add(g => g.AllowGuest, true));

	[Test]
	public async Task RefusedConnection_DoesNotFaultTheRenderer()
	{
		// The renderer catches what escapes a component, which is what puts Blazor's fatal error bar
		// on screen in production. Render does NOT rethrow it — the connect fails on a continuation
		// after the first synchronous render — so the renderer's own channel is what has to be read.
		var unhandled = Renderer.UnhandledException;

		RenderTerminal(RefusingTerminal());

		await Task.WhenAny(unhandled, Task.Delay(TimeSpan.FromSeconds(2)));

		await Assert.That(unhandled.IsCompleted)
			.IsFalse()
			.Because("an unreachable gateway must not be fatal to every page the terminal mounts on");
	}

	[Test]
	public async Task RefusedConnection_IsReportedInTheTerminal()
	{
		var cut = RenderTerminal(RefusingTerminal());

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("TermErrorLine", StringComparison.Ordinal))
				throw new InvalidOperationException("the failure has not been reported yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("TermErrorLine");
	}
}
