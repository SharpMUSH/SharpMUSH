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
/// Play's log export: it saves the chosen stretch as a file, from the saved log or, while that is off or no character is
/// playing, from what the terminal holds now, with a warning; and says so instead when the chosen times are out of order
/// or nothing was kept then.
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

	private async Task<IRenderedComponent<MudDialogProvider>> ShowAsync(TerminalIdentity? identity, bool saved = true,
		params TerminalLine[] screen)
	{
		await _log.SetAsync(saved);
		var provider = Render<MudDialogProvider>();
		var dialogs = Services.GetRequiredService<IDialogService>();
		var parameters = new DialogParameters<PlayLogExportDialog>
		{
			{ x => x.Identity, identity },
			{ x => x.Character, "Ada" },
			{ x => x.Now, () => new DateTimeOffset(Noon) },
			{ x => x.Screen, () => screen },
		};
		await provider.InvokeAsync(() => dialogs.ShowAsync<PlayLogExportDialog>("export", parameters));
		return provider;
	}

	private IEnumerable<JSRuntimeInvocation> Saves =>
		JSInterop.Invocations.Where(i => i.Identifier == "SharpMUSH.Files.saveText");

	private static IElement SaveButton(IRenderedComponent<MudDialogProvider> provider) =>
		provider.FindAll("button").Single(b => b.TextContent.Contains("PlayLogExportSave"));

	private static TerminalLine Server(string text, DateTime at) => new(at, text, TerminalLineSource.Server);

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task WithoutTheSavedLog_WhatTheTerminalHoldsIsSaved_WithAWarning(bool character)
	{
		var provider = await ShowAsync(character ? Ada : null, saved: false,
			Server("Long ago.", Noon.AddHours(-2)),
			Server("Ada says, \"hi\"", Noon.AddMinutes(-5)),
			new TerminalLine(Noon.AddMinutes(-4), "connect Ada hunter2", TerminalLineSource.Client));
		await Assert.That(provider.Markup).Contains("PlayLogExportScreenOnly(");
		await Assert.That(provider.Markup).DoesNotContain("PlayLogExportAll(").Because("everything there is the screen, not 30 days");

		await SaveButton(provider).ClickAsync(new());

		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "SharpMUSH.TerminalLog.between")).IsFalse();
		var text = (string?)Saves.Single().Arguments[2];
		await Assert.That(text).Contains("[2026-10-10 11:55:00] Ada says, \"hi\"");
		await Assert.That(text).DoesNotContain("Long ago.").Because("it is before the last hour");
		await Assert.That(text).DoesNotContain("hunter2").Because("what the player typed can carry a password");
	}

	[Test]
	public async Task WithTheSavedLog_ThereIsNoWarning()
	{
		var provider = await ShowAsync(Ada);

		await Assert.That(provider.Markup).DoesNotContain("PlayLogExportScreenOnly(");
	}

	[Test]
	public async Task NothingOnScreen_SaysSo_AndSavesNothing()
	{
		var provider = await ShowAsync(null, saved: false, Server("Long ago.", Noon.AddHours(-2)));

		await SaveButton(provider).ClickAsync(new());

		await Assert.That(provider.Markup).Contains("PlayLogExportNothing(");
		await Assert.That(Saves.Any()).IsFalse();
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
