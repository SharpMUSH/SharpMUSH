using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// Play's settings menu always offers the export; while there is no saved log to read (it is off, or no character is
/// playing) a warning mark beside it says the export has only what the terminal holds now, and why.
/// </summary>
public class PlayTerminalSettingsExportTests : BunitContext
{
	public PlayTerminalSettingsExportTests()
	{
		Services.AddMudServices();
		Services.AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private sealed class Host : ComponentBase
	{
		[Parameter] public bool KeepLogsOn { get; set; }
		[Parameter] public bool PlayingCharacter { get; set; } = true;

		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			builder.OpenComponent<MudPopoverProvider>(0);
			builder.CloseComponent();
			builder.OpenComponent<PlayTerminalSettings>(1);
			builder.AddAttribute(2, nameof(PlayTerminalSettings.Menu), PlaySettingsMenu.Header);
			builder.AddAttribute(3, nameof(PlayTerminalSettings.Open), true);
			builder.AddAttribute(4, nameof(PlayTerminalSettings.KeepLogsOn), KeepLogsOn);
			builder.AddAttribute(5, nameof(PlayTerminalSettings.PlayingCharacter), PlayingCharacter);
			builder.CloseComponent();
		}
	}

	private IElement ExportItem(IRenderedComponent<Host> cut) =>
		cut.FindAll("button.play-cfg-item").Single(b => b.TextContent.Contains("PlayLogExportOpen"));

	[Test]
	public async Task WithTheSavedLogOn_TheExportHasNoWarning()
	{
		var cut = Render<Host>(p => p.Add(h => h.KeepLogsOn, true));

		var item = ExportItem(cut);
		await Assert.That(item.QuerySelector(".play-cfg-warn")).IsNull();
		await Assert.That(item.HasAttribute("title")).IsFalse();
	}

	[Test]
	public async Task WithTheSavedLogOff_TheExportIsStillOffered_WithAWarning()
	{
		var cut = Render<Host>(p => p.Add(h => h.KeepLogsOn, false));

		var item = ExportItem(cut);
		await Assert.That(item.QuerySelector(".play-cfg-warn")).IsNotNull();
		await Assert.That(item.GetAttribute("title")).IsEqualTo("PlayLogExportOffHint");
		await Assert.That(item.TextContent).Contains("PlayLogExportOffHint").Because("a screen reader reads the warning too");
	}

	[Test]
	public async Task WithNoCharacterPlaying_TheExportWarns_ThatOnlyACharactersLogIsSaved()
	{
		var cut = Render<Host>(p => p.Add(h => h.KeepLogsOn, true).Add(h => h.PlayingCharacter, false));

		var item = ExportItem(cut);
		await Assert.That(item.QuerySelector(".play-cfg-warn")).IsNotNull();
		await Assert.That(item.GetAttribute("title")).IsEqualTo("PlayLogExportGuestHint");
	}
}
