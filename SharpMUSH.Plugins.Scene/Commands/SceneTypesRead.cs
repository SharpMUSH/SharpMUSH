using System.Globalization;
using MarkupString.Layout;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// <c>@scene/types</c>: the pose types as the plugin read them from the Scene Logger's <c>TYPE`</c>
/// attributes, and every such attribute it left out with the reason, so a mistake in one is found here
/// rather than by a type that silently draws as in character.
/// </summary>
public static class SceneTypesRead
{
	public static async ValueTask<MString> Types(IMUSHCodeParser parser, INotifyService notifyService, AnySharpObject executor)
	{
		if (await SceneLogger.FindAsync(parser.ServiceProvider) is not AnySharpObject)
		{
			await notifyService.Notify(executor, "SCENE: The scene package is not installed, so there are no pose types.");
			return MarkupText.Empty;
		}

		var catalogue = await SceneLogger.CatalogueAsync(parser.ServiceProvider);
		var types = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Key")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Label")) { Min = 8 },
				new TableColumn(MarkupText.Plain("Presentation")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Tone")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Icon")) { Wrap = false, Priority = 3 },
				new TableColumn(MarkupText.Plain("Hidden")) { Wrap = false, Priority = 3 },
				new TableColumn(MarkupText.Plain("Order")) { Alignment = Alignment.Right, Wrap = false, Priority = 3 },
			],
			catalogue.Types.Select(t => new[]
			{
				t.Key, t.Label, t.Presentation, t.Tone, t.Icon, t.Hidden ? "yes" : "no",
				t.Order.ToString(CultureInfo.InvariantCulture),
			}));

		var output = ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Pose types"), types), 78);
		await notifyService.Notify(executor, output);

		if (catalogue.Problems.Count > 0)
		{
			var problems = ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Attribute")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Why it was left out")) { Min = 20 },
				],
				catalogue.Problems.Select(p => new[] { $"TYPE`{p.Key.ToUpperInvariant()}", p.Reason }));
			await notifyService.Notify(executor,
				ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Not read"), problems), 78));
		}

		return MarkupText.Plain(string.Join(' ', catalogue.Types.Select(t => t.Key)));
	}
}
