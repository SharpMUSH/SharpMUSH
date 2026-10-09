using System.Globalization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Plugins.Scene.Commands;

/// <summary>
/// <c>ooc &lt;text&gt;</c> — speak out of character to the room, and into the scene when there is one.
///
/// <para>The room hears <c>&lt;OOC&gt; Name: text</c>. As with the usual MUSH OOC commands, a leading
/// <c>:</c> poses (<c>ooc :waves</c> → <c>&lt;OOC&gt; Name waves</c>) and a leading <c>;</c> semiposes
/// (<c>ooc ;'s back</c> → <c>&lt;OOC&gt; Name's back</c>). It is spoken as <c>say</c>/<c>pose</c> are: the
/// room's Speech lock applies, the speaker's SPEECHMOD transforms the words once (with <c>"</c>, <c>:</c>
/// or <c>;</c> as %1), the name is the speech name (NAMEACCENT, MONIKER, <c>Someone</c> for the
/// invisible), and a gagged player cannot use it.</para>
///
/// <para>When the speaker is focused on the active scene in the room they are standing in — the same
/// test the capture hooks apply to a pose — the line is also recorded there through the ordinary pose
/// path, with type and source <c>ooc</c>, and broadcast like any other pose. The recorded text
/// is exactly the line the room heard, less the <c>&lt;OOC&gt;</c> marker (the type carries it): the speech
/// name, not the scene persona, since out of character is the player speaking. Anywhere else nothing is
/// recorded. The speaker must also be approved when they speak, re-checked on every line as the scene
/// package's capture hooks do, because focus and membership survive a revoked approved role. The rule is
/// the package's own <c>FUN`IS`APPROVED</c>, evaluated on its Scene Logger as the logger, so a game that
/// redefines it is obeyed here too; without the package it is the package's default, a player that
/// <c>isapproved()</c>.</para>
///
/// <para>This is the "game's OOC command" the portal's OOC band assumes (design handoff §7.2): the band
/// draws a pose by its type.</para>
/// </summary>
public static class OocCommand
{
	/// <summary>The pose type and source an OOC line is recorded with.</summary>
	public const string Tag = PoseTypes.OutOfCharacter;

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

		// Spoken as SAY/POSE/SEMIPOSE are — Speech lock, SPEECHMOD once on the words, the speech name — in
		// the OOC frame; the scene records the line the room heard.
		var (token, words) = Split(message);
		var said = await communication.FramedSpeechAsync(parser, words, token,
			(name, transformed) => Band(Line(name, token, transformed)));
		if (!said.Admitted) return said.Result;

		await RecordAsync(parser, executor, Line(said.Name, token, said.Message));
		return said.Result;
	}

	/// <summary>
	/// The speech token a message is spoken with and the words after it: <c>:</c> (pose) and <c>;</c>
	/// (semipose) when it starts with one, else <c>"</c> (say). The token is also what SPEECHMOD sees as %1.
	/// </summary>
	public static (string Token, MString Words) Split(MString message) =>
		message.ToPlainText() switch
		{
			[':', ..] => (":", message.Substring(1)),
			[';', ..] => (";", message.Substring(1)),
			_ => ("\"", message),
		};

	/// <summary>
	/// What was said, without the marker: <c>Name: words</c>, or <c>Name words</c> for <c>:</c>, or
	/// <c>Namewords</c> for <c>;</c>.
	/// </summary>
	public static MString Line(MString name, string token, MString words) => token switch
	{
		":" => MarkupText.Concat([name, MarkupText.Plain(" "), words]),
		";" => MarkupText.Concat(name, words),
		_ => MarkupText.Concat([name, MarkupText.Plain(": "), words]),
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
			? argument.Message
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
			|| focused.Id != here.Id
			|| !await IsApprovedAsync(parser, executor))
			return;

		var recorded = await sceneService.AddPoseAsync(here.Id, speaker, showAs: string.Empty, room, type: Tag, source: Tag, tags: [],
			MarkupTextSerializer.Serialize(line));
		if (recorded is Contracts.ScenePose pose)
			await SceneBroadcast.PublishSceneEventAsync(parser, here.Id, "pose", pose);
	}

	private const string ApprovalRule = "FUN`IS`APPROVED";

	/// <summary>
	/// Whether the speaker may be written into a scene: the scene package's <c>FUN`IS`APPROVED</c>, the rule
	/// its capture hooks and verbs re-check on every write, or — when the package or that attribute is
	/// absent — the package's default, <c>and(hastype(%0,PLAYER),isapproved(%0))</c>.
	/// </summary>
	private static async ValueTask<bool> IsApprovedAsync(IMUSHCodeParser parser, AnySharpObject speaker)
	{
		if (await SceneLogger.FindAsync(parser.ServiceProvider) is AnySharpObject logger)
		{
			var attributes = parser.ServiceProvider.GetRequiredService<IAttributeService>();
			if (await attributes.GetAttributeAsync(logger, logger, ApprovalRule, IAttributeService.AttributeMode.Read)
				is SharpAttribute[])
			{
				var answer = await attributes.EvaluateAttributeFunctionAsync(parser, logger, logger, ApprovalRule,
					new Dictionary<string, CallState> { ["0"] = new(speaker.Object().DBRef.ToString()) },
					ignorePermissions: true);
				return IsTrue(answer.ToPlainText());
			}
		}

		return speaker.IsPlayer && await speaker.IsApproved();
	}

	/// <summary>MUSH truth: empty, a number equal to zero, and an error (<c>#-…</c>) are false.</summary>
	private static bool IsTrue(string text)
	{
		var value = text.Trim();
		return value.Length != 0
			&& !value.StartsWith("#-", StringComparison.Ordinal)
			&& !(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == 0);
	}
}
