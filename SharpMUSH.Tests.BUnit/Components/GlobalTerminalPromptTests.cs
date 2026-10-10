using System.Text.Json.Nodes;
using Bunit;
using MarkupString;
using MarkupString.Ansi;
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
/// The prompt row: the prompt the game is waiting on sits between the output and the input line, replaced by
/// the next prompt and emptied by its clear, and its command links send their command as typed input.
/// </summary>
public class GlobalTerminalPromptTests : BunitContext
{
	public GlobalTerminalPromptTests()
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

	/// <summary>A real terminal over a socket that is open, so frames go through the terminal's own handling.</summary>
	private static async Task<(TerminalService Terminal, IWebSocketClientService Ws)> ConnectedTerminalAsync()
	{
		var ws = Substitute.For<IWebSocketClientService>();
		ws.IsConnected.Returns(true);
		var terminal = new TerminalService(ws, NullLogger<TerminalService>.Instance);
		await terminal.ConnectAsync("ws://localhost:4202/ws");
		return (terminal, ws);
	}

	private static string PromptFrame(MarkupText text, string? session = null)
	{
		var envelope = new JsonObject
		{
			["type"] = "markup",
			["data"] = MarkupTextSerializer.Serialize(text),
			["prompt"] = true
		};
		if (session is not null) envelope["session"] = session;
		return envelope.ToJsonString();
	}

	private static void Receive(IRenderedComponent<GlobalTerminal> cut, IWebSocketClientService ws, string frame) =>
		cut.InvokeAsync(() => ws.MessageReceived += Raise.Event<EventHandler<string>>(ws, frame)).GetAwaiter().GetResult();

	private static void PromptRowReads(IRenderedComponent<GlobalTerminal> cut, string? expected)
	{
		var row = cut.Find(".sharp-terminal-prompt");
		var shown = row.HasAttribute("hidden") ? null : row.TextContent.Trim();
		if (shown != expected) throw new InvalidOperationException($"prompt row reads \"{shown}\", expected \"{expected}\"");
	}

	[Test]
	public async Task A_prompt_fills_the_row_and_stays_out_of_the_output()
	{
		var (terminal, ws) = await ConnectedTerminalAsync();
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		PromptRowReads(cut, null);

		Receive(cut, ws, PromptFrame(MarkupText.Plain("Read which post?"), "s1"));

		cut.WaitForAssertion(() => PromptRowReads(cut, "Read which post?"), TimeSpan.FromSeconds(5));
		var row = cut.Find(".sharp-terminal-prompt");
		await Assert.That(row.GetAttribute("aria-live")).IsEqualTo("polite");
		await Assert.That(cut.Find(".sharp-terminal-output").TextContent).DoesNotContain("Read which post?");
	}

	[Test]
	public async Task A_second_prompt_replaces_the_first_and_a_clear_empties_the_row()
	{
		var (terminal, ws) = await ConnectedTerminalAsync();
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));

		Receive(cut, ws, PromptFrame(MarkupText.Plain("First?"), "s1"));
		cut.WaitForAssertion(() => PromptRowReads(cut, "First?"), TimeSpan.FromSeconds(5));

		Receive(cut, ws, PromptFrame(MarkupText.Plain("Second?"), "s1"));
		cut.WaitForAssertion(() => PromptRowReads(cut, "Second?"), TimeSpan.FromSeconds(5));

		Receive(cut, ws, "{\"type\":\"prompt\",\"clear\":true,\"session\":\"other\"}");
		Receive(cut, ws, "{\"type\":\"prompt\",\"clear\":true,\"session\":\"s1\"}");
		cut.WaitForAssertion(() => PromptRowReads(cut, null), TimeSpan.FromSeconds(5));
		await Assert.That(terminal.Prompt).IsNull();
	}

	[Test]
	public async Task Sending_a_line_moves_the_prompt_into_the_output()
	{
		var (terminal, ws) = await ConnectedTerminalAsync();
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		cut.WaitForAssertion(() =>
		{
			if (cut.Find(".term-input").HasAttribute("disabled")) throw new InvalidOperationException("not connected yet");
		}, TimeSpan.FromSeconds(5));
		Receive(cut, ws, PromptFrame(MarkupText.Plain("Continue?")));
		cut.WaitForAssertion(() => PromptRowReads(cut, "Continue?"), TimeSpan.FromSeconds(5));

		await cut.Find(".term-input").InputAsync("yes");
		await cut.Find(".term-input").KeyDownAsync("Enter");

		cut.WaitForAssertion(() => PromptRowReads(cut, null), TimeSpan.FromSeconds(5));
		cut.WaitForAssertion(() =>
		{
			var output = cut.Find(".sharp-terminal-output").TextContent;
			var asked = output.IndexOf("Continue?", StringComparison.Ordinal);
			if (asked < 0 || output.IndexOf("yes", asked, StringComparison.Ordinal) < 0)
				throw new InvalidOperationException($"output is \"{output}\"");
		}, TimeSpan.FromSeconds(5));
		await ws.Received(1).SendAsync("yes");
	}

	[Test]
	public async Task A_link_in_the_row_sends_its_command()
	{
		var (terminal, ws) = await ConnectedTerminalAsync();
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		var link = MarkupText.Wrap(AnsiMarkup.Create(linkUrl: "+bbread 1/2", linkKind: LinkKind.Command, linkText: "+bbread 1/2"),
			MarkupText.Plain("2"));
		var promptText = MarkupText.Concat(MarkupText.Plain("Read: "), link);

		Receive(cut, ws, PromptFrame(promptText, "s1"));
		cut.WaitForAssertion(() => cut.Find(".sharp-terminal-prompt a[xch_cmd]"), TimeSpan.FromSeconds(5));

		// The row's links get the same delegated click and Enter/Space handlers as the output's.
		var rowId = cut.Find(".sharp-terminal-prompt").Id;
		await Assert.That(JSInterop.Invocations
			.Any(i => i.Identifier == "SharpMUSH.Terminal.attachCommandLinks" && Equals(i.Arguments[0], rowId))).IsTrue();

		// What the handler does on a click: hands the link's command to the terminal.
		var command = cut.Find(".sharp-terminal-prompt a[xch_cmd]").GetAttribute("xch_cmd")!;
		await cut.InvokeAsync(() => cut.Instance.RunCommandLinkAsync(command));

		await ws.Received(1).SendAsync("+bbread 1/2");
		await Assert.That(terminal.Prompt?.Line.Text).IsEqualTo("Read: 2").Because("a session's prompt stays until its next prompt or its clear");
	}

	[Test]
	public async Task A_non_interactive_terminal_has_no_prompt_row()
	{
		var (terminal, ws) = await ConnectedTerminalAsync();
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal).Add(g => g.Interactive, false));

		Receive(cut, ws, PromptFrame(MarkupText.Plain("Continue?")));

		await Assert.That(cut.FindAll(".sharp-terminal-prompt")).IsEmpty();
		await Assert.That(JSInterop.Invocations.Count(i => i.Identifier == "SharpMUSH.Terminal.attachCommandLinks")).IsEqualTo(1);
	}
}
