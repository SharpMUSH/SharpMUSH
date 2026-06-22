using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// Plot switch: <c>@scene/plot[/create|/link|/unlink] &lt;plot&gt;[=&lt;sceneId&gt;]</c>.
/// The sub-switch selects the operation; bare <c>/plot &lt;plotId&gt;</c> displays the plot.
/// </summary>
public static class ScenePlotHandlers
{
	public static async ValueTask<MString> Plot(
		IMUSHCodeParser parser,
		ISceneService sceneService,
		INotifyService notifyService,
		AnySharpObject executor,
		string? subSwitch,
		MString plotArg,
		MString? sceneArg)
	{
		var sceneId = SceneCommandHelper.Plain(sceneArg);

		switch (subSwitch)
		{
			case "CREATE":
			{
				// /plot/create <ownerDbref>,<title>[,<description>]
				var fields = SceneCommandHelper.SplitFields(plotArg, 3);
				var ownerDbref = await SceneLocate.PlayerOrSelf(parser, fields[0]);
				var title = fields[1];
				var description = fields[2];

				if (string.IsNullOrEmpty(ownerDbref) || string.IsNullOrEmpty(title))
				{
					await notifyService.Notify(executor,
						"SCENE: /plot/create needs <ownerDbref>,<title>[,<description>].");
					return MModule.single(SceneCommandHelper.BadArguments);
				}

				var plot = await sceneService.UpsertPlotAsync(null, title, description, ownerDbref);
				await notifyService.Notify(executor, $"SCENE: Created plot #{plot.Id} '{plot.Title}'.");
				return MModule.single(plot.Id);
			}

			case "LINK":
			{
				// /plot/link <plotId>=<sceneId>
				var plotId = SceneCommandHelper.Plain(plotArg);
				var result = await sceneService.LinkSceneToPlotAsync(plotId, sceneId);
				return await result.Match(
					async _ =>
					{
						await notifyService.Notify(executor, $"SCENE: Linked scene #{sceneId} into plot #{plotId}.");
						return MModule.single(plotId);
					},
					async _ =>
					{
						await notifyService.Notify(executor, "SCENE: No such plot or scene.");
						return MModule.single(SceneCommandHelper.NotFound);
					});
			}

			case "UNLINK":
			{
				// /plot/unlink <plotId>=<sceneId>
				var plotId = SceneCommandHelper.Plain(plotArg);
				var result = await sceneService.UnlinkSceneFromPlotAsync(plotId, sceneId);
				return await result.Match(
					async _ =>
					{
						await notifyService.Notify(executor, $"SCENE: Unlinked scene #{sceneId} from plot #{plotId}.");
						return MModule.single(plotId);
					},
					async _ =>
					{
						await notifyService.Notify(executor, "SCENE: No such plot or scene.");
						return MModule.single(SceneCommandHelper.NotFound);
					});
			}

			default:
			{
				// Bare /plot <plotId> — display.
				var plotId = SceneCommandHelper.Plain(plotArg);
				var lookup = await sceneService.GetPlotAsync(plotId);
				return await lookup.Match(
					async plot =>
					{
						await notifyService.Notify(executor,
							$"SCENE: Plot #{plot.Id} '{plot.Title}' — owner {plot.OwnerName}. {plot.Description}");
						return MModule.single(plot.Id);
					},
					async _ =>
					{
						await notifyService.Notify(executor, $"SCENE: No plot '{plotId}'.");
						return MModule.single(SceneCommandHelper.NotFound);
					});
			}
		}
	}
}
