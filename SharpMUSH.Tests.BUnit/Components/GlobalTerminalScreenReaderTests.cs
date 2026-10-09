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
/// Screen reader mode on the play terminal: the announcer reads the game's lines and leaves out the player's own
/// commands, and the review keys read a recent line again.
/// </summary>
public class GlobalTerminalScreenReaderTests : BunitContext
{
	public GlobalTerminalScreenReaderTests()
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

	private static ITerminalService Terminal()
	{
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(false);
		terminal.Lines.Returns([]);
		return terminal;
	}

	private static TerminalLine Line(string text, TerminalLineSource source) => new(DateTime.Now, text, source);

	private static string Announced(IRenderedComponent<GlobalTerminal> cut) =>
		string.Join(" | ", cut.FindAll(".visually-hidden[role='log'] > div").Select(d => d.TextContent));

	[Test]
	public async Task Off_the_output_log_reads_everything_itself()
	{
		var terminal = Terminal();
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));

		await cut.InvokeAsync(() => terminal.LineReceived += Raise.Event<Action<TerminalLine>>(Line("Kestrel says, \"hi\"", TerminalLineSource.Server)));

		await Assert.That(cut.Find(".sharp-terminal-output").GetAttribute("aria-live")).IsEqualTo("polite");
		await Assert.That(Announced(cut)).IsEqualTo(string.Empty);
		await Assert.That(cut.Find(".term-input").HasAttribute("data-review-keys")).IsFalse();
	}

	[Test]
	public async Task On_the_announcer_reads_the_games_lines_but_not_the_players_own_commands()
	{
		var terminal = Terminal();
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		await cut.InvokeAsync(() => Services.GetRequiredService<ScreenReaderMode>().SetAsync(true));

		await cut.InvokeAsync(() =>
		{
			terminal.LineReceived += Raise.Event<Action<TerminalLine>>(Line("say hi", TerminalLineSource.Client));
			terminal.LineReceived += Raise.Event<Action<TerminalLine>>(Line("You say, \"hi\"", TerminalLineSource.Server));
			terminal.LineReceived += Raise.Event<Action<TerminalLine>>(Line("You say, \"hi\"", TerminalLineSource.Server));
		});

		await Assert.That(cut.Find(".sharp-terminal-output").GetAttribute("aria-live")).IsEqualTo("off")
			.Because("the log and the announcer would otherwise both read each line");
		await Assert.That(Announced(cut)).IsEqualTo("You say, \"hi\" | You say, \"hi\"")
			.Because("the same words twice are two lines, and the typed command is not read back");
		await Assert.That(cut.Find(".term-input").GetAttribute("data-review-keys")).IsEqualTo("on");
	}

	[Test]
	public async Task A_review_key_reads_that_recent_line_again()
	{
		var terminal = Terminal();
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		await cut.InvokeAsync(() =>
		{
			terminal.LineReceived += Raise.Event<Action<TerminalLine>>(Line("The harbour is quiet.", TerminalLineSource.Server));
			terminal.LineReceived += Raise.Event<Action<TerminalLine>>(Line("look", TerminalLineSource.Client));
			terminal.LineReceived += Raise.Event<Action<TerminalLine>>(Line("Mira arrives.", TerminalLineSource.Server));
		});
		await cut.InvokeAsync(() => Services.GetRequiredService<ScreenReaderMode>().SetAsync(true));

		await cut.Instance.ReviewLine(1);
		await cut.Instance.ReviewLine(2);
		await cut.Instance.ReviewLine(9);

		await Assert.That(Announced(cut)).IsEqualTo("Mira arrives. | The harbour is quiet. | TermReviewNone");
	}

	[Test]
	public async Task A_line_is_read_from_its_markup_not_its_drawn_plain_text()
	{
		var terminal = Terminal();
		JSInterop.Setup<string?>("sharpmushLayout.spokenText", i => (string?)i.Arguments[0] == "<div role=\"separator\"><span>God posed</span></div>")
			.SetResult("God posed");
		var cut = Render<GlobalTerminal>(p => p.Add(g => g.Terminal, terminal));
		await cut.InvokeAsync(() => Services.GetRequiredService<ScreenReaderMode>().SetAsync(true));

		await cut.InvokeAsync(() => terminal.LineReceived += Raise.Event<Action<TerminalLine>>(
			new TerminalLine(DateTime.Now, "═╡ God posed ╞════", "<div role=\"separator\"><span>God posed</span></div>", TerminalLineSource.Server)));
		await cut.Instance.ReviewLine(1);

		await Assert.That(Announced(cut)).IsEqualTo("God posed | God posed");
	}
}
