using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// <c>ooc &lt;text&gt;</c> — speak out of character to the room, and into the scene when there is one.
///
/// <para>The room hears <c>&lt;OOC&gt; Name: text</c>. As with the usual MUSH OOC commands, a leading
/// <c>:</c> poses (<c>ooc :waves</c> → <c>&lt;OOC&gt; Name waves</c>) and a leading <c>;</c> semiposes
/// (<c>ooc ;'s back</c> → <c>&lt;OOC&gt; Name's back</c>). It is delivered like <c>@emit</c>: the room's
/// Speech lock applies, and a gagged player cannot use it.</para>
///
/// <para>When the speaker is focused on the active scene in the room they are standing in — the same
/// test the capture hooks apply to a pose — the line is also recorded there through the ordinary pose
/// path, with source <c>ooc</c> and tag <c>ooc</c>, and broadcast like any other pose. The recorded text
/// is the line without the <c>&lt;OOC&gt;</c> marker (the tag carries it), and it is the player's own
/// name, not their scene persona: out of character is the player speaking. Anywhere else nothing is
/// recorded. Approval is scene-package policy the plugin cannot see; a player who is focused on a scene
/// has already been let into it.</para>
///
/// <para>This is the "game's OOC command" the portal's OOC band assumes (design handoff §7.2): the band
/// keys off the <c>ooc</c> tag.</para>
/// </summary>
public static class OocCommand
{
	/// <summary>The pose tag and source an OOC line is recorded with.</summary>
	public const string Tag = "ooc";

	private const string Marker = "<OOC> ";

	[SharpCommand(Name = "OOC", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 1, ParameterNames = ["message"])]
	public static async ValueTask<Option<CallState>> Ooc(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
		var notifyService = parser.ServiceProvider.GetRequiredService<INotifyService>();
		var communication = parser.ServiceProvider.GetRequiredService<ICommunicationService>();
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		var message = await MessageAsync(parser);
		if (IsEmpty(message))
		{
			await notifyService.Notify(executor, "OOC: What do you want to say?", executor);
			return CallState.Empty;
		}

		var line = Line(executor.Object().Name, message);
		var said = await communication.EmitWithOutcomeAsync(parser, new EmitRequest(EmitScope.Immediate, Band(line), []));
		if (!said.Admitted) return said.Result;

		await RecordAsync(parser, executor, line);
		return said.Result;
	}

	/// <summary>
	/// What was said, without the marker: <c>Name: text</c>, or <c>Name text</c> after a <c>:</c>, or
	/// <c>Nametext</c> after a <c>;</c>.
	/// </summary>
	public static MString Line(string name, MString message) =>
		message.ToPlainText() switch
		{
			[':', ..] => MarkupText.Concat([MarkupText.Plain(name), MarkupText.Plain(" "), message.Substring(1)]),
			[';', ..] => MarkupText.Concat(MarkupText.Plain(name), message.Substring(1)),
			_ => MarkupText.Concat([MarkupText.Plain(name), MarkupText.Plain(": "), message]),
		};

	/// <summary>What the room hears: the line, marked <c>&lt;OOC&gt;</c>.</summary>
	public static MString Band(MString line) => MarkupText.Concat(MarkupText.Plain(Marker), line);

	/// <summary>True when there is nothing to say, a pose or semipose prefix alone included.</summary>
	public static bool IsEmpty(MString message) =>
		message.ToPlainText().TrimStart() is var text
		&& (text.Length == 0 || (text[0] is ':' or ';' && text[1..].Trim().Length == 0));

	private static async ValueTask<MString> MessageAsync(IMUSHCodeParser parser)
	{
		if (!parser.CurrentState.Arguments.TryGetValue("0", out var argument)) return MarkupText.Empty;

		return parser.CurrentState.Switches.Contains("NOEVAL")
			? argument.Message ?? MarkupText.Empty
			: await argument.ParsedMessage() ?? MarkupText.Empty;
	}

	/// <summary>Records the line in the speaker's scene when they are standing in it; otherwise nothing.</summary>
	private static async ValueTask RecordAsync(IMUSHCodeParser parser, AnySharpObject executor, MString line)
	{
		var sceneService = parser.ServiceProvider.GetRequiredService<ISceneService>();
		var speaker = executor.Object().DBRef.ToString();
		var room = (await executor.Where()).Object().DBRef.ToString();

		if (await sceneService.GetCurrentSceneAsync(speaker) is not Contracts.Scene focused
			|| await sceneService.GetActiveSceneInRoomAsync(room) is not Contracts.Scene here
			|| focused.Id != here.Id)
			return;

		var recorded = await sceneService.AddPoseAsync(here.Id, speaker, showAs: string.Empty, room, Tag, [Tag],
			MarkupTextSerializer.Serialize(line));
		if (recorded is Contracts.ScenePose pose)
			await SceneBroadcast.PublishSceneEventAsync(parser, here.Id, "pose", pose);
	}
}
