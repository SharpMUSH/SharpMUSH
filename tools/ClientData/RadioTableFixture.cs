using System.Reflection;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;

namespace SharpMUSH.Tools.ClientData;

/// <summary>Regenerates the browser regression fixture with the pinned renderer.</summary>
internal static class RadioTableFixture
{
	public static string Render()
	{
		var table = new Table(
		[
			new TableColumn(MarkupText.Plain("Frequency")) { Wrap = false },
			new TableColumn(MarkupText.Plain("Category")),
			new TableColumn(MarkupText.Plain("You")) { Wrap = false },
			new TableColumn(MarkupText.Plain("About")) { Grow = 1 }
		],
		[[MarkupText.Plain("Public"), MarkupText.Plain("General"), MarkupText.Plain("Off"),
			MarkupText.Plain("A public frequency for everyone.")]]);
		var registry = MarkupRegistry.Empty.WithAnsi().WithHtml();
		var html = BlockLayout.Build(table, 78).Render(MarkupFormat.Html, registry);
		var version = typeof(MarkupText).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
			.InformationalVersion.Split('+')[0];
		return $"<!-- Renderer: MarkupString {version} -->\n{html}\n";
	}
}
