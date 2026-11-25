using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;
using static SharpMUSH.Client.Components.Play.PlayLogExportDialog;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Play's log export: it saves the kept lines of the chosen stretch as a file, and says so instead when the terminal is
/// playing no character, the chosen times are out of order, or nothing was kept then.
/// </summary>
public class PlayLogExportDialogTests : TrackingBunitContext
{
	private static readonly TerminalIdentity Ada = new("ada@example.com", "#7:1700000000000");
	private static readonly DateTime Noon = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Local);
	private readonly TerminalLog _log;

	public PlayLogExportDialogTests()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		Services.AddMudServices();
		Services.AddEchoLocalizer();
		_log = new TerminalLog(JSInterop.JSRuntime);
		Services.AddSingleton(_log);
	}

	private async Task<IRenderedComponent<MudDialogProvider>> ShowAsync(TerminalIdentity? identity)
	{
		await _log.SetAsync(true);
		var provider = Render<MudDialogProvider>();
		var dialogs = Services.GetRequiredService<IDialogService>();
		var parameters = new DialogParameters<PlayLogExportDialog>
		{
			{ x => x.Identity, identity },
			{ x => x.Character, "Ada" },
			{ x => x.Now, () => new DateTimeOffset(Noon) },
		};
		await provider.InvokeAsync(() => dialogs.ShowAsync<PlayLogExportDialog>("export", parameters));
		return provider;
	}

	private IEnumerable<JSRuntimeInvocation> Saves =>
		JSInterop.Invocations.Where(i => i.Identifier == "SharpMUSH.Files.saveText");

	private static IElement SaveButton(IRenderedComponent<MudDialogProvider> provider) =>
		provider.FindAll("button").Single(b => b.TextContent.Contains("PlayLogExportSave"));

	[Test]
	public async Task WithNoCharacter_SaysSo_AndOffersNoExport()
	{
		var provider = await ShowAsync(null);

		await Assert.That(provider.Markup).Contains("PlayLogExportNoCharacter");
		await Assert.That(provider.FindAll("button").Any(b => b.TextContent.Contains("PlayLogExportSave"))).IsFalse();
	}

	[Test]
	public async Task KeptLines_AreSavedAsText_ForTheLastHour()
	{
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.between", _ => true).SetResult(JsonSerializer.Serialize(new[]
		{
			new { t = "Ada says, \"hi\"", h = "Ada says, &quot;hi&quot;", at = new DateTimeOffset(Noon.AddMinutes(-5)).ToUnixTimeMilliseconds() }
		}));
		var provider = await ShowAsync(Ada);

		await SaveButton(provider).ClickAsync(new());

		var asked = JSInterop.Invocations.Single(i => i.Identifier == "SharpMUSH.TerminalLog.between").Arguments;
		await Assert.That((long)asked[1]!).IsEqualTo(new DateTimeOffset(Noon.AddHours(-1)).ToUnixTimeMilliseconds());
		await Assert.That((long)asked[2]!).IsEqualTo(new DateTimeOffset(Noon).ToUnixTimeMilliseconds());
		var save = Saves.Single().Arguments;
		await Assert.That((string?)save[0]).IsEqualTo("Ada 2026-10-10 1100 to 2026-10-10 1200.txt");
		await Assert.That((string?)save[1]).IsEqualTo("text/plain");
		await Assert.That((string?)save[2]).Contains("[2026-10-10 11:55:00] Ada says, \"hi\"");
		await Assert.That(provider.Markup).DoesNotContain("PlayLogExportSave").Because("the dialog closes once the file is handed over");
	}

	[Test]
	public async Task NothingKept_SaysSo_AndSavesNothing()
	{
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.between", _ => true).SetResult("[]");
		var provider = await ShowAsync(Ada);

		await SaveButton(provider).ClickAsync(new());

		await Assert.That(provider.Markup).Contains("PlayLogExportNothing(");
		await Assert.That(Saves.Any()).IsFalse();
	}

	[Test]
	public async Task LogOutOfReach_SaysSo()
	{
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.between", _ => true).SetResult(null);
		var provider = await ShowAsync(Ada);

		await SaveButton(provider).ClickAsync(new());

		await Assert.That(provider.Markup).Contains("PlayLogExportUnreadable");
		await Assert.That(Saves.Any()).IsFalse();
	}

	[Test]
	public async Task CustomTimesOutOfOrder_SaySo_AndCannotExport()
	{
		var provider = await ShowAsync(Ada);
		var select = provider.FindComponent<MudSelect<PlayLogRange>>();
		await provider.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(PlayLogRange.Custom));

		await provider.Find("#play-log-export-from").InputAsync("2026-10-10T12:30");
		await provider.Find("#play-log-export-to").InputAsync("2026-10-10T12:00");

		await Assert.That(provider.Markup).Contains("PlayLogExportOrder");
		await Assert.That(SaveButton(provider).HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task CustomTimes_OfOneMinute_CanExport()
	{
		var provider = await ShowAsync(Ada);
		var select = provider.FindComponent<MudSelect<PlayLogRange>>();
		await provider.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(PlayLogRange.Custom));

		await provider.Find("#play-log-export-from").InputAsync("2026-10-10T11:30");
		await provider.Find("#play-log-export-to").InputAsync("2026-10-10T11:30");

		await Assert.That(provider.Markup).DoesNotContain("PlayLogExportOrder");
		await Assert.That(SaveButton(provider).HasAttribute("disabled")).IsFalse();
	}

	[Test]
	public async Task AWarning_GoesOnceTheChoiceChanges()
	{
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.between", _ => true).SetResult("[]");
		var provider = await ShowAsync(Ada);
		await SaveButton(provider).ClickAsync(new());
		await Assert.That(provider.Markup).Contains("PlayLogExportNothing(");

		var format = provider.FindComponent<MudRadioGroup<TerminalLogFormat>>();
		await provider.InvokeAsync(() => format.Instance.ValueChanged.InvokeAsync(TerminalLogFormat.Html));

		await Assert.That(provider.Markup).DoesNotContain("PlayLogExportNothing(");
	}

	[Test]
	public async Task CustomTimes_StartAsTheLastHour_AndExportToTheEndOfTheLastMinute()
	{
		JSInterop.Setup<string?>("SharpMUSH.TerminalLog.between", _ => true).SetResult("[]");
		var provider = await ShowAsync(Ada);
		var select = provider.FindComponent<MudSelect<PlayLogRange>>();
		await provider.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(PlayLogRange.Custom));

		await SaveButton(provider).ClickAsync(new());

		var asked = JSInterop.Invocations.Single(i => i.Identifier == "SharpMUSH.TerminalLog.between").Arguments;
		await Assert.That((long)asked[1]!).IsEqualTo(new DateTimeOffset(Noon.AddHours(-1)).ToUnixTimeMilliseconds());
		await Assert.That((long)asked[2]!).IsEqualTo(new DateTimeOffset(Noon.AddSeconds(59.999)).ToUnixTimeMilliseconds());
	}
}
