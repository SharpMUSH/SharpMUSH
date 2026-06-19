using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// @SCENE — the wizard-only primitive surface for the graph-native Scene System
	/// (<c>graph_sharp_sys_scene</c>). The engine ships these primitives; capture,
	/// permission, formatting and temp-room orchestration are softcode policy.
	/// Players never call this — the shipped <c>#SCENELOGGER</c> bootstrap drives it
	/// (or the side-effect <c>scene…</c> functions). Scene-scoped switches take a
	/// <c>&lt;sceneId&gt;</c>; pose-scoped switches take a <c>&lt;poseId&gt;</c>.
	/// Comma-separated arguments carry <c>content</c> last; object references are
	/// dbrefs (the service resolves the vertex, manages the edge, and snapshots the name).
	/// Publishing the realtime <c>SceneEventMessage</c> is a later phase and is not done here.
	/// </summary>
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
		parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		// Explicit wizard gate (the @SQL / AdminMail precedent). The FLAG^WIZARD
		// CommandLock guards dispatch too; this surfaces a clean permission error.
		if (!await executor.IsWizard())
		{
			await NotifyService!.Notify(executor, SceneCommand.SceneCommandHelper.PermissionDeniedNotice);
			return new CallState(SceneCommand.SceneCommandHelper.PermissionDeniedReturn);
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

		var sceneService = parser.ServiceProvider.GetRequiredService<ISceneService>();

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
			"LIST" => await SceneCommand.SceneRead.List(parser, sceneService, NotifyService!, executor, arg0),
			"GET" when hasArg0
				=> await SceneCommand.SceneRead.Get(parser, sceneService, NotifyService!, executor, arg0!),
			"CREATE"
				=> await SceneCommand.SceneWrite.Create(parser, sceneService, NotifyService!, executor, arg0),
			"SET" when hasArg0 && hasArg1
				=> await SceneCommand.SceneWrite.Set(parser, sceneService, NotifyService!, executor, arg0!, arg1!),
			"ADDPOSE" when hasArg0 && hasArg1
				=> await SceneCommand.ScenePoseHandlers.AddPose(parser, sceneService, NotifyService!, executor, arg0!, arg1!),
			"SETPOSE" when hasArg0 && hasArg1
				=> await SceneCommand.ScenePoseHandlers.SetPose(parser, sceneService, NotifyService!, executor, arg0!, arg1!),
			"EDITPOSE" when hasArg0 && hasArg1
				=> await SceneCommand.ScenePoseHandlers.EditPose(parser, sceneService, NotifyService!, executor, arg0!, arg1!),
			"UNDO" when hasArg0
				=> await SceneCommand.ScenePoseHandlers.Undo(parser, sceneService, NotifyService!, executor, arg0!),
			"REDO" when hasArg0
				=> await SceneCommand.ScenePoseHandlers.Redo(parser, sceneService, NotifyService!, executor, arg0!),
			"MOVE" when hasArg0
				=> await SceneCommand.ScenePoseHandlers.Move(parser, sceneService, NotifyService!, executor, arg0!, arg1),
			"DELETE" when hasArg0
				=> await SceneCommand.ScenePoseHandlers.Delete(parser, sceneService, NotifyService!, executor, arg0!),
			"MEMBER" when hasArg0 && hasArg1
				=> await SceneCommand.SceneMemberHandlers.AddMember(parser, sceneService, NotifyService!, executor, arg0!, arg1!),
			"UNMEMBER" when hasArg0 && hasArg1
				=> await SceneCommand.SceneMemberHandlers.RemoveMember(parser, sceneService, NotifyService!, executor, arg0!, arg1!),
			"FOCUS" when hasArg0
				=> await SceneCommand.SceneMemberHandlers.Focus(parser, sceneService, NotifyService!, executor, arg0!, arg1),
			"SHOWAS" when hasArg0 && hasArg1
				=> await SceneCommand.SceneMemberHandlers.ShowAs(parser, sceneService, NotifyService!, executor, arg0!, arg1!),
			"PLOT" when hasArg0
				=> await SceneCommand.ScenePlotHandlers.Plot(parser, sceneService, NotifyService!, executor, plotSub, arg0!, arg1),
			null when hasArg0
				=> await SceneCommand.SceneRead.Display(parser, sceneService, NotifyService!, executor, arg0!),
			_ => MModule.single(SceneCommand.SceneCommandHelper.BadArguments),
		};

		return new CallState(response);
	}
}
