using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// The Alerts dialog's "Try a line": the line typed there is read as the kind of line chosen beside it, so a rule kept to
/// pages or channels can be tried before it is saved.
/// </summary>
public class HighlightRulesDialogTests : TrackingBunitContext
{
	public HighlightRulesDialogTests()
	{
		Services.AddMudServices();
		Services.AddEchoLocalizer();
		Services.AddSingleton(new TerminalAlerts(JSInterop.JSRuntime));
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>The dialog over the rules given, kept in the browser as a signed-out player's are.</summary>
	private async Task<IRenderedComponent<MudDialogProvider>> ShowAsync(params HighlightRule[] rules)
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(Track(new HttpClient { BaseAddress = new Uri("https://localhost:8081/") }));
		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		await auth.InitAsync();
		var settings = new PlaySettings(new PreferencesService(factory), auth, JSInterop.JSRuntime);
		await settings.LoadAsync();
		var highlights = new HighlightRules(settings);
		await highlights.SaveAsync(rules);
		Services.AddSingleton(highlights);

		var provider = Render<MudDialogProvider>();
		var dialogs = Services.GetRequiredService<IDialogService>();
		await provider.InvokeAsync(() => dialogs.ShowAsync<HighlightRulesDialog>("alerts"));
		return provider;
	}

	private static async Task TypeAsync(IRenderedComponent<MudDialogProvider> provider, string line) =>
		await provider.InvokeAsync(() => provider.FindComponents<MudTextField<string>>().Single(f => f.Instance.Class == "hl-try-text")
			.Instance.ValueChanged.InvokeAsync(line));

	private static async Task ChooseKindAsync(IRenderedComponent<MudDialogProvider> provider, TerminalLineComm kind) =>
		await provider.InvokeAsync(() => provider.FindComponent<MudSelect<TerminalLineComm>>().Instance.ValueChanged.InvokeAsync(kind));

	private static string Result(IRenderedComponent<MudDialogProvider> provider) => provider.Find(".hl-try-result").TextContent;

	[Test]
	public async Task ARuleKeptToPagesMatchesTheSampleOnlyWhenItIsTriedAsAPage()
	{
		var provider = await ShowAsync(new HighlightRule("p", "Ann", Lines: HighlightLines.Pages));

		await TypeAsync(provider, "Ann pages: are you free?");
		await Assert.That(Result(provider)).IsEqualTo("TermHlTryNoMatch").Because("the sample starts as ordinary output");

		await ChooseKindAsync(provider, TerminalLineComm.Page);
		await Assert.That(Result(provider)).StartsWith("TermHlTryMatched(Ann, ");

		await ChooseKindAsync(provider, TerminalLineComm.Channel);
		await Assert.That(Result(provider)).IsEqualTo("TermHlTryNoMatch");
	}

	[Test]
	public async Task ARuleForEverythingElseStopsMatchingWhenTheSampleIsAChannelMessage()
	{
		var provider = await ShowAsync(new HighlightRule("o", "Ann", Lines: HighlightLines.Other));

		await TypeAsync(provider, "<Public> Ann says, \"Hi.\"");
		await Assert.That(Result(provider)).StartsWith("TermHlTryMatched(Ann, ");

		await ChooseKindAsync(provider, TerminalLineComm.Channel);
		await Assert.That(Result(provider)).IsEqualTo("TermHlTryNoMatch");
	}
}
