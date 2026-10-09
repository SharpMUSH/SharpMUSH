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
/// Covers the <see cref="GlobalTerminal.Interactive"/> gate: the /play terminal keeps its command
/// input (the default), while the always-mounted global/command terminal in the drawer is a
/// read-only diagnostics view (<c>Interactive=false</c>) that drops the input row but keeps its
/// output/scrollback.
/// </summary>
public class GlobalTerminalInteractiveTests : BunitContext
{
	public GlobalTerminalInteractiveTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<CommandHistory>();
		Services.AddSingleton<ScreenReaderMode>();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true));
		JSInterop.Mode = JSRuntimeMode.Loose;

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

	private static ITerminalService AnonymousTerminal()
	{
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(false);
		terminal.Lines.Returns([]);
		return terminal;
	}

	[Test]
	public async Task Non_interactive_terminal_has_no_command_input_but_keeps_its_output()
	{
		var cut = Render<GlobalTerminal>(p => p
			.Add(g => g.Terminal, AnonymousTerminal())
			.Add(g => g.Interactive, false));

		await Assert.That(cut.FindAll(".term-input")).IsEmpty();
		await Assert.That(cut.FindAll(".term-send")).IsEmpty();
		await Assert.That(cut.FindAll(".sharp-terminal-output")).IsNotEmpty();
	}

	[Test]
	public async Task Up_and_Down_walk_the_commands_sent()
	{
		var terminal = AnonymousTerminal();
		terminal.IsConnected.Returns(true);
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		// The terminal reads the connection after its first await; under a loaded run the line can render first.
		cut.WaitForAssertion(() =>
		{
			if (cut.Find(".term-input").HasAttribute("disabled")) throw new InvalidOperationException("not connected yet");
		}, TimeSpan.FromSeconds(5));
		// The key handlers are async, and a loaded run can finish them after KeyDown returns: wait on each effect.
		foreach (var command in new[] { "look", "say hi" })
		{
			await cut.Find(".term-input").InputAsync(command);
			await cut.Find(".term-input").KeyDownAsync("Enter");
			cut.WaitForAssertion(() => terminal.Received(1).SendAsync(command), TimeSpan.FromSeconds(5));
		}

		await cut.Find(".term-input").KeyDownAsync("ArrowUp");
		cut.WaitForAssertion(() => InputIs(cut, "say hi"), TimeSpan.FromSeconds(5));
		await cut.Find(".term-input").KeyDownAsync("ArrowUp");
		cut.WaitForAssertion(() => InputIs(cut, "look"), TimeSpan.FromSeconds(5));
		await cut.Find(".term-input").KeyDownAsync("ArrowDown");
		await cut.Find(".term-input").KeyDownAsync("ArrowDown");
		cut.WaitForAssertion(() => InputIs(cut, ""), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".term-input").GetAttribute("value")).IsEqualTo("").Because("past the newest is the empty line");
	}

	private static void InputIs(IRenderedComponent<GlobalTerminal> cut, string expected)
	{
		var value = cut.Find(".term-input").GetAttribute("value");
		if (value != expected) throw new InvalidOperationException($"input is \"{value}\", expected \"{expected}\"");
	}

	[Test]
	public async Task Interactive_terminal_by_default_renders_the_command_input()
	{
		var cut = Render<GlobalTerminal>(p => p
			.Add(g => g.Terminal, AnonymousTerminal()));

		await Assert.That(cut.FindAll(".term-input")).IsNotEmpty();
		await Assert.That(cut.FindAll(".term-send")).IsNotEmpty();
		await Assert.That(cut.FindAll(".sharp-terminal-output")).IsNotEmpty();
	}
}
