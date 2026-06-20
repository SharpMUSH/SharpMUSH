using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// @SCENE — the wizard-only primitive surface for the graph-native Scene System
/// (<c>graph_sharp_sys_scene</c>). Phase 5: this surface now ships from the Scene PLUGIN, not the
/// engine. The engine ships no scene primitives; capture, permission, formatting and temp-room
/// orchestration remain softcode policy. Players never call this — the shipped <c>scene</c> softcode
/// package drives it (or the side-effect <c>scene…</c> functions). Scene-scoped switches take a
/// <c>&lt;sceneId&gt;</c>; pose-scoped switches take a <c>&lt;poseId&gt;</c>. Comma-separated arguments
/// carry <c>content</c> last; object references are dbrefs (the service resolves the vertex, manages the
/// edge, and snapshots the name). Pose mutations (addpose/editpose/delete/move) publish a realtime
/// <c>SceneEventMessage</c> on <c>game.scene.{id}</c> via <see cref="SceneBroadcast"/>.
///
/// <para>As a plugin command this resolves <c>IMediator</c>/<c>INotifyService</c>/<c>ISceneService</c> from
/// <c>parser.ServiceProvider</c> at call time (the supported, unload-friendly pattern) rather than from the
/// engine's static service fields, which do not exist in a plugin.</para>
/// </summary>
public static class SceneCommandModule
{
	[SharpCommand(Name = "@SCENE",
		CommandLock = "FLAG^WIZARD",
		Switches =
		[
			"LIST", "GET", "CREATE", "SET", "ADDPOSE", "SETPOSE", "EDITPOSE", "UNDO", "REDO",
			"MOVE", "DELETE", "MEMBER", "UNMEMBER", "FOCUS", "SHOWAS", "PLOT", "LINK", "UNLINK",
			"NOEVAL"
		],
		Behavior = CB.Default | CB.EqSplit | CB.NoParse, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["target", "content"])]
	public static async ValueTask<Option<CallState>> Scene(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
		var notifyService = parser.ServiceProvider.GetRequiredService<INotifyService>();
		var sceneService = parser.ServiceProvider.GetRequiredService<ISceneService>();

		parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		// Explicit wizard gate (the @SQL / AdminMail precedent). The FLAG^WIZARD
		// CommandLock guards dispatch too; this surfaces a clean permission error.
		if (!await executor.IsWizard())
		{
			await notifyService.Notify(executor, SceneCommandHelper.PermissionDeniedNotice);
			return new CallState(SceneCommandHelper.PermissionDeniedReturn);
		}

		MString? arg0, arg1;
		if (!switches.Contains("NOEVAL"))
		{
			arg0 = await (arg0CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
			arg1 = await (arg1CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
		}
		else
		{
			arg0 = arg0CallState?.Message;
			arg1 = arg1CallState?.Message;
		}

		// NOEVAL only affects argument evaluation; everything else is the action.
		var actions = switches.Where(s => s != "NOEVAL").ToArray();

		// PLOT may carry a sub-switch (CREATE/LINK/UNLINK); CREATE is reused as the scene
		// create switch, so only treat it as a plot sub-switch when PLOT is also present.
		var isPlot = actions.Contains("PLOT");
		var plotSub = actions.FirstOrDefault(s => s is "CREATE" or "LINK" or "UNLINK");

		var action = actions.FirstOrDefault(s =>
			s is "LIST" or "GET" or "SET" or "ADDPOSE" or "SETPOSE" or "EDITPOSE"
				or "UNDO" or "REDO" or "MOVE" or "DELETE" or "MEMBER" or "UNMEMBER"
				or "FOCUS" or "SHOWAS" or "PLOT"
				|| (s == "CREATE" && !isPlot));

		var hasArg0 = (arg0?.Length ?? 0) != 0;
		var hasArg1 = (arg1?.Length ?? 0) != 0;

		var response = action switch
		{
			"LIST" => await SceneRead.List(parser, sceneService, notifyService, executor, arg0),
			"GET" when hasArg0
				=> await SceneRead.Get(parser, sceneService, notifyService, executor, arg0!),
			"CREATE"
				=> await SceneWrite.Create(parser, sceneService, notifyService, executor, arg0),
			"SET" when hasArg0 && hasArg1
				=> await SceneWrite.Set(parser, sceneService, notifyService, executor, arg0!, arg1!),
			"ADDPOSE" when hasArg0 && hasArg1
				=> await ScenePoseHandlers.AddPose(parser, sceneService, notifyService, executor, arg0!, arg1!),
			"SETPOSE" when hasArg0 && hasArg1
				=> await ScenePoseHandlers.SetPose(parser, sceneService, notifyService, executor, arg0!, arg1!),
			"EDITPOSE" when hasArg0 && hasArg1
				=> await ScenePoseHandlers.EditPose(parser, sceneService, notifyService, executor, arg0!, arg1!),
			"UNDO" when hasArg0
				=> await ScenePoseHandlers.Undo(parser, sceneService, notifyService, executor, arg0!),
			"REDO" when hasArg0
				=> await ScenePoseHandlers.Redo(parser, sceneService, notifyService, executor, arg0!),
			"MOVE" when hasArg0
				=> await ScenePoseHandlers.Move(parser, sceneService, notifyService, executor, arg0!, arg1),
			"DELETE" when hasArg0
				=> await ScenePoseHandlers.Delete(parser, sceneService, notifyService, executor, arg0!),
			"MEMBER" when hasArg0 && hasArg1
				=> await SceneMemberHandlers.AddMember(parser, sceneService, notifyService, executor, arg0!, arg1!),
			"UNMEMBER" when hasArg0 && hasArg1
				=> await SceneMemberHandlers.RemoveMember(parser, sceneService, notifyService, executor, arg0!, arg1!),
			"FOCUS" when hasArg0
				=> await SceneMemberHandlers.Focus(parser, sceneService, notifyService, executor, arg0!, arg1),
			"SHOWAS" when hasArg0 && hasArg1
				=> await SceneMemberHandlers.ShowAs(parser, sceneService, notifyService, executor, arg0!, arg1!),
			"PLOT" when hasArg0
				=> await ScenePlotHandlers.Plot(parser, sceneService, notifyService, executor, plotSub, arg0!, arg1),
			null when hasArg0
				=> await SceneRead.Display(parser, sceneService, notifyService, executor, arg0!),
			_ => MModule.single(SceneCommandHelper.BadArguments),
		};

		return new CallState(response);
	}
}
