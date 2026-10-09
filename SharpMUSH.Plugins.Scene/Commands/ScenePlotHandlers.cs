using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
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
							SceneCommandHelper.Notice("/plot/create needs <ownerDbref>,<title>[,<description>].", NoticeKind.Warn));
						return MarkupText.Plain(SceneCommandHelper.BadArguments);
					}

					var plot = await sceneService.UpsertPlotAsync(null, title, description, ownerDbref);
					await notifyService.Notify(executor, SceneCommandHelper.Notice($"Created plot #{plot.Id} '{plot.Title}'.", NoticeKind.Ok));
					return MarkupText.Plain(plot.Id);
				}

			case "LINK":
				{
					// /plot/link <plotId>=<sceneId>
					var plotId = SceneCommandHelper.Plain(plotArg);
					if (await sceneService.LinkSceneToPlotAsync(plotId, sceneId) is NotFound)
					{
						await notifyService.Notify(executor, SceneCommandHelper.Notice("No such plot or scene.", NoticeKind.Warn));
						return MarkupText.Plain(SceneCommandHelper.NotFound);
					}

					await notifyService.Notify(executor, SceneCommandHelper.Notice($"Linked scene #{sceneId} into plot #{plotId}.", NoticeKind.Ok));
					return MarkupText.Plain(plotId);
				}

			case "UNLINK":
				{
					// /plot/unlink <plotId>=<sceneId>
					var plotId = SceneCommandHelper.Plain(plotArg);
					if (await sceneService.UnlinkSceneFromPlotAsync(plotId, sceneId) is NotFound)
					{
						await notifyService.Notify(executor, SceneCommandHelper.Notice("No such plot or scene.", NoticeKind.Warn));
						return MarkupText.Plain(SceneCommandHelper.NotFound);
					}

					await notifyService.Notify(executor, SceneCommandHelper.Notice($"Unlinked scene #{sceneId} from plot #{plotId}.", NoticeKind.Ok));
					return MarkupText.Plain(plotId);
				}

			default:
				{
					// Bare /plot <plotId> — display.
					var plotId = SceneCommandHelper.Plain(plotArg);
					if (await sceneService.GetPlotAsync(plotId) is not ScenePlot plot)
					{
						await notifyService.Notify(executor, SceneCommandHelper.Notice($"No plot '{plotId}'.", NoticeKind.Warn));
						return MarkupText.Plain(SceneCommandHelper.NotFound);
					}

					await notifyService.Notify(executor,
						SceneCommandHelper.Notice($"Plot #{plot.Id} '{plot.Title}' — owner {plot.OwnerName}. {plot.Description}"));
					return MarkupText.Plain(plot.Id);
				}
		}
	}
}
