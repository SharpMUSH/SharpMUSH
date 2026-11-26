using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models.PlayInput;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Play's input shortcuts editor: a key is recorded by pressing it, a lead is refused when it would take a pose, a
/// say or the escape, and Save keeps what the draft holds.
/// </summary>
public class PlayInputShortcutsDialogTests : BunitContext
{
	private readonly TestCommFeed _comms = new();

	public PlayInputShortcutsDialogTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		Services.AddSingleton<ICommFeed>(_comms);
		Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(sp => new AccountAuthService(sp.GetRequiredService<IHttpClientFactory>(),
			sp.GetRequiredService<IJSRuntime>(), NullLogger<AccountAuthService>.Instance, []));
		Services.AddSingleton<PreferencesService>();
		Services.AddSingleton<PlaySettings>();
		Services.AddSingleton<PlayInputShortcutStore>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private (IRenderedComponent<MudDialogProvider> Provider, Func<bool> Open) Show()
	{
		var provider = Render<MudDialogProvider>();
		var open = true;
		Render<PlayInputShortcutsDialog>(p => p
			.Add(d => d.Visible, true)
			.Add(d => d.VisibleChanged, (bool now) => open = now));
		provider.WaitForAssertion(() => provider.Find(".psd"));
		return (provider, () => open);
	}

	private PlayInputShortcutStore Store => Services.GetRequiredService<PlayInputShortcutStore>();

	[Test]
	public async Task A_pressed_key_and_its_command_are_kept_on_save()
	{
		var (provider, open) = Show();

		await provider.Find(".psd-recorder").KeyDownAsync(new KeyboardEventArgs { Code = "F2", Key = "F2", ShiftKey = true });
		await Assert.That(provider.Find(".psd-recorder").TextContent.Trim()).IsEqualTo("Shift+F2");
		await provider.FindAll(".psd-add--key input")[0].InputAsync("score");
		await provider.FindAll(".psd-add--key button").Last().ClickAsync(new MouseEventArgs());
		provider.WaitForAssertion(() => provider.Find(".psd-chord"));
		await Assert.That(provider.Find(".psd-chord").TextContent).IsEqualTo("Shift+F2");

		await provider.FindAll(".mud-dialog-actions button").Last().ClickAsync(new MouseEventArgs());

		await Assert.That(Store.Current.Keys).IsEquivalentTo([new KeyShortcut("Shift+F2", "score")]);
		await Assert.That(open()).IsFalse();
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "localStorage.setItem"
			&& (string?)i.Arguments[0] == "play.pref.shortcuts")).IsTrue()
			.Because("without a signed-in account, Play's settings are kept in this browser");
	}

	[Test]
	public async Task A_key_the_browser_keeps_is_refused()
	{
		var (provider, _) = Show();

		await provider.Find(".psd-recorder").KeyDownAsync(new KeyboardEventArgs { Code = "F5", Key = "F5" });

		await Assert.That(provider.Find(".psd-error").TextContent).Contains("NavShortcutKeyNotAllowed");
		await Assert.That(provider.FindAll(".psd-add--key button").Last().HasAttribute("disabled")).IsTrue();
	}

	[Test]
	[Arguments(":", "NavShortcutLeadReserved")]
	[Arguments("/x", "NavShortcutLeadEscape")]
	[Arguments("a b", "NavShortcutLeadSpace")]
	public async Task A_lead_that_would_take_a_pose_a_say_or_the_escape_is_refused(string lead, string reason)
	{
		var (provider, _) = Show();

		await provider.FindAll(".psd-add--lead input")[0].InputAsync(lead);
		await provider.FindAll(".psd-add--lead input")[1].InputAsync("Public");

		await Assert.That(provider.Find(".psd-error").TextContent).Contains(reason);
		await Assert.That(provider.FindAll(".psd-add--lead button").Last().HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task A_lead_shows_what_a_line_would_send_and_is_kept_on_save()
	{
		var (provider, _) = Show();

		await provider.FindAll(".psd-add--lead input")[0].InputAsync(",");
		await provider.FindAll(".psd-add--lead input")[1].InputAsync("Public");
		await Assert.That(provider.Find(".psd-preview").TextContent).Contains("@chat Public=NavShortcutPreviewText");

		await provider.FindAll(".psd-add--lead button").Last().ClickAsync(new MouseEventArgs());
		await provider.FindAll(".mud-dialog-actions button").Last().ClickAsync(new MouseEventArgs());

		await Assert.That(Store.Current.Leads).IsEquivalentTo([new ChannelLead(",", "Public")]);
		await Assert.That(Store.Current.Expand(",hi")).IsEqualTo("@chat Public=hi");
	}

	[Test]
	public async Task Cancel_keeps_nothing()
	{
		var (provider, open) = Show();

		await provider.FindAll(".psd-add--lead input")[0].InputAsync(",");
		await provider.FindAll(".psd-add--lead input")[1].InputAsync("Public");
		await provider.FindAll(".psd-add--lead button").Last().ClickAsync(new MouseEventArgs());
		await provider.FindAll(".mud-dialog-actions button").First().ClickAsync(new MouseEventArgs());

		await Assert.That(open()).IsFalse();
		await Assert.That(Store.Current.Leads).IsEmpty();
	}
}
