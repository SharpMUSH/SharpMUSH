using Bunit;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
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
		Services.AddSingleton<TerminalLog>();
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
	public async Task Guide_inserts_only_look_into_an_empty_connected_terminal_without_sending()
	{
		var terminal = AnonymousTerminal();
		terminal.IsConnected.Returns(true);
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		cut.WaitForAssertion(() => { if (cut.Find(".term-input").HasAttribute("disabled")) throw new InvalidOperationException("not ready"); });
		await Assert.That(await cut.InvokeAsync(() => cut.Instance.TryInsertGuideCommandAsync("say hi"))).IsFalse();
		await Assert.That(await cut.InvokeAsync(() => cut.Instance.TryInsertGuideCommandAsync("look"))).IsTrue();
		await Assert.That(cut.Find(".term-input").GetAttribute("value")).IsEqualTo("look");
		await terminal.DidNotReceive().SendAsync(Arg.Any<string>());
		await cut.Find(".term-input").InputAsync("my unfinished draft");
		await Assert.That(await cut.InvokeAsync(() => cut.Instance.TryInsertGuideCommandAsync("look"))).IsFalse();
		await Assert.That(cut.Find(".term-input").GetAttribute("value")).IsEqualTo("my unfinished draft");
	}

	[Test]
	public async Task Earlier_lines_from_the_kept_log_go_above_the_rest()
	{
		var terminal = AnonymousTerminal();
		var now = DateTime.Now;
		terminal.Lines.Returns([new SharpMUSH.Client.Models.TerminalLine(now, "now", "now", SharpMUSH.Client.Models.TerminalLineSource.Server)]);
		terminal.Identity.Returns(new TerminalIdentity("ada@example.com", "#7:1"));
		var at = new DateTimeOffset(now.AddMinutes(-5)).ToUnixTimeMilliseconds();
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.earlier", _ => true).SetResult($$"""[{"t":"earlier","h":"earlier","at":{{at}}}]""");
		await Services.GetRequiredService<TerminalLog>().SetAsync(true);

		var withoutLog = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		await Assert.That(withoutLog.FindAll(".sharp-terminal-earlier-btn")).IsEmpty().Because("only the play terminal offers the log");

		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal).Add(g => g.KeepsLog, true));
		await cut.Find(".sharp-terminal-earlier-btn").ClickAsync(new());

		cut.WaitForAssertion(() =>
		{
			if (cut.Find(".sharp-terminal-earlier").TextContent.Trim() != "earlier") throw new InvalidOperationException("not loaded yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "SharpMUSH.Terminal.restorePlace")).IsTrue();
	}

	[Test]
	public async Task Echo_off_hides_the_sent_commands_and_keeps_them()
	{
		var terminal = AnonymousTerminal();
		terminal.Lines.Returns([
			new TerminalLine(DateTime.Now, "look", TerminalLineSource.Client),
			new TerminalLine(DateTime.Now, "A quiet room.", TerminalLineSource.Server)]);

		var echoing = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		await Assert.That(echoing.FindAll(".sharp-terminal-no-echo")).IsEmpty();

		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal).Add(g => g.EchoCommands, false));
		await Assert.That(cut.FindAll(".sharp-terminal-output.sharp-terminal-no-echo")).Count().IsEqualTo(1);
		await Assert.That(cut.Find(".sharp-terminal-no-echo .terminal-sent").TextContent).IsEqualTo("look");

		cut.Render(p => p.Add(g => g.EchoCommands, true));
		await Assert.That(cut.FindAll(".sharp-terminal-no-echo")).IsEmpty();
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

	[Test]
	public async Task The_unsent_line_waits_on_the_terminal_while_the_player_is_on_another_page()
	{
		var inner = AnonymousTerminal();
		inner.IsConnected.Returns(true);
		inner.Identity.Returns(new TerminalIdentity("ada@example.com", "#7:1"));
		var host = new TerminalServiceHost(() => inner);
		var first = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, host));
		await first.Find(".term-input").InputAsync("pose waves");
		await first.InvokeAsync(() => first.Instance.DisposeAsync().AsTask());

		var back = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, host));
		await Assert.That(back.Find(".term-input").GetAttribute("value")).IsEqualTo("pose waves");

		// Signed out meanwhile (the connection gone) or signed in as someone else: nothing is offered.
		await back.InvokeAsync(() => back.Instance.DisposeAsync().AsTask());
		inner.IsConnected.Returns(false);
		await Assert.That(host.TakeDraft()).IsEqualTo(string.Empty);
		host.KeepDraft("connect Bob secret");
		inner.IsConnected.Returns(true);
		inner.Identity.Returns(new TerminalIdentity("bob@example.com", "#9:1"));
		await Assert.That(host.TakeDraft()).IsEqualTo(string.Empty);

		inner.Identity.Returns(new TerminalIdentity("ada@example.com", "#7:1"));
		back = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, host));
		await back.Find(".term-input").InputAsync("pose waves");

		// A new session (a character switch): the line written as the old character goes with it.
		await back.InvokeAsync(host.RecreateAsync);
		back.WaitForAssertion(() =>
		{
			if (back.Find(".term-input").GetAttribute("value") is { Length: > 0 }) throw new InvalidOperationException("draft still shown");
		}, TimeSpan.FromSeconds(5));
		await back.InvokeAsync(() => back.Instance.DisposeAsync().AsTask());
		await Assert.That(host.TakeDraft()).IsEqualTo(string.Empty).Because("a recreated session's view was cleared before it went");
	}

	[Test]
	public async Task A_command_link_sends_its_command_shows_the_newest_lines_and_leaves_the_draft()
	{
		var terminal = AnonymousTerminal();
		terminal.IsConnected.Returns(true);
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		cut.WaitForAssertion(() => { if (cut.Find(".term-input").HasAttribute("disabled")) throw new InvalidOperationException("not ready"); });
		await cut.Find(".term-input").InputAsync("say half");

		await cut.InvokeAsync(() => cut.Instance.RunCommandLinkAsync("WHO"));

		await terminal.Received(1).SendAsync("WHO");
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "SharpMUSH.Terminal.jumpToLatest")).IsTrue();
		await Assert.That(cut.Find(".term-input").GetAttribute("value")).IsEqualTo("say half");
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "SharpMUSH.Terminal.attachInputKeys")).IsTrue();
		// Pressed with the mouse, these act without taking focus from the input line (input-focus.js).
		foreach (var control in new[] { ".term-send", ".sharp-terminal-jump" })
			await Assert.That(cut.Find(control).HasAttribute("data-keeps-input")).IsTrue();
	}
}
