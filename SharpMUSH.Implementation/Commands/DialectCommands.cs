using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// Commands TinyMUX and RhostMUSH have and SharpMUSH has no name for, reached by their own names only under
/// those dialects (<see cref="Library.Softcode.SoftcodeDialects"/>). Each has a <c>@dialect_</c> name of its
/// own, so a SharpMUSH game's abbreviations (<c>@fe</c> for <c>@feed</c>) stay as they are.
/// </summary>
public partial class Commands
{
	/// <summary>
	/// <c>@femit[/here|/room] &lt;object&gt;=&lt;message&gt;</c>: the object emits the message where it is
	/// (<c>/here</c>, the default), in the room it is in at the outermost level (<c>/room</c>), or both. Both on an
	/// object standing in a room emit once, as the two name the same room.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_FEMIT", Switches = ["HERE", "ROOM"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "message"])]
	public ValueTask<Option<CallState>> DialectForcedEmit(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ForcedSpeechAsync(parser, async (forced, message) =>
		{
			var switches = parser.CurrentState.Switches.ToArray();
			var room = switches.Contains("ROOM");
			var here = switches.Contains("HERE") || !room;
			Option<CallState> result = CallState.Empty;
			if (here)
				result = await RunEmitCommand(Emitting(forced, message), attribute, EmitScope.Immediate, false);
			if (room && !(here && await StandsInRoomAsync(forced)))
				result = await RunEmitCommand(Emitting(forced, message), attribute, EmitScope.Outermost, false);
			return result;
		});

	/// <summary><c>@fpose[/nospace] &lt;object&gt;=&lt;message&gt;</c>: the object poses the message.</summary>
	[SharpCommand(Name = "@DIALECT_FPOSE", Switches = ["DEFAULT", "NOSPACE"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "message"])]
	public ValueTask<Option<CallState>> DialectForcedPose(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ForcedSpeechAsync(parser, async (forced, message) => await CommunicationService.SpeechAsync(forced, message,
			parser.CurrentState.Switches.Contains("NOSPACE") ? ";" : ":"));

	/// <summary><c>@fsay &lt;object&gt;=&lt;message&gt;</c>: the object says the message.</summary>
	[SharpCommand(Name = "@DIALECT_FSAY", Switches = [],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "message"])]
	public ValueTask<Option<CallState>> DialectForcedSay(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ForcedSpeechAsync(parser, async (forced, message) => await CommunicationService.SpeechAsync(forced, message, "\""));

	/// <summary>
	/// The object named before the <c>=</c> speaks the message after it, evaluated once, by the one who typed it.
	/// The speaker must be one the executor could <c>@force</c>.
	/// </summary>
	private async ValueTask<Option<CallState>> ForcedSpeechAsync(IMUSHCodeParser parser,
		Func<IMUSHCodeParser, MString, ValueTask<Option<CallState>>> speak)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!await executor.Can(PortalPermission.PlayerForce))
			return await NotifyService.PermissionDenied(executor, PermissionRefusal.Needs(PortalPermission.PlayerForce));

		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? string.Empty;
		var message = args.GetValueOrDefault("1")?.Message ?? MarkupText.Empty;
		if (message.Text.Length == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail), executor);
			return new CallState(ErrorMessages.Returns.NothingToDo);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, LocateFlags.All) switch
		{
			AnySharpObject speaker => await ForceRefusalAsync(executor, speaker) is { } refused
				? refused
				: await SpeakForcedAsync(parser, executor, speaker, message, speak),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> SpeakForcedAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject speaker, MString message, Func<IMUSHCodeParser, MString, ValueTask<Option<CallState>>> speak)
	{
		await LogForceAsync(executor, speaker, message);
		return await parser.With(state => state with { Executor = speaker.Object().DBRef, Caller = state.Executor },
			forced => speak(forced, message));
	}

	private async ValueTask<bool> StandsInRoomAsync(IMUSHCodeParser forced)
		=> (await forced.CurrentState.KnownExecutorObject(Mediator)).AsOptionalContent is AnySharpContent content
			&& (await content.Location()).IsRoom;

	/// <summary>The forced speaker's state with the message as an emit's only, already evaluated, argument.</summary>
	private static IMUSHCodeParser Emitting(IMUSHCodeParser forced, MString message)
		=> forced.Push(forced.CurrentState with
		{
			Switches = [],
			Arguments = new Dictionary<string, CallState> { ["0"] = new(message) }
		});

	/// <summary>
	/// <c>@last [&lt;player&gt;]</c>: where and when the player last connected, and the last connection that
	/// failed. Without a player, the executor's owner. Only staff and those who control the player may look.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_LAST", Switches = [], Behavior = CB.Default | CB.NoGuest, MinArgs = 0, MaxArgs = 1,
		ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> DialectLast(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.ArgumentsOrdered.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? string.Empty;
		if (name.Length == 0 || name.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			AnySharpObject owner = await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
			return await LastConnectsAsync(executor, owner);
		}

		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, name) switch
		{
			AnySharpObject player => await LastConnectsAsync(executor, player),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> LastConnectsAsync(AnySharpObject executor, AnySharpObject player)
	{
		if (!await executor.IsWizard() && !await executor.IsRoyalty() && !await PermissionService.Controls(executor, player))
			return await NotifyService.PermissionDeniedNotControlled(executor, player);

		var playerRef = player.Object().DBRef;
		var last = await PlainAttributeAsync(playerRef, "LAST");
		var site = await PlainAttributeAsync(playerRef, "LASTSITE");
		var failed = (await PlainAttributeAsync(playerRef, "LASTFAILED"))?.Trim();
		await NotifyService.Notify(executor, string.IsNullOrWhiteSpace(last)
			? $"{player.Object().Name} has no connection on record."
			: $"Last connect: from {(string.IsNullOrWhiteSpace(site) ? "an unknown site" : site)} on {last}.");
		await NotifyService.Notify(executor, string.IsNullOrEmpty(failed)
			? "No failed connect since then."
			: $"Last failed connect: from {failed}.");
		return CallState.Empty;
	}

	/// <summary>
	/// <c>PUEBLOCLIENT &lt;version&gt;</c>: a Pueblo client's answer to the game's Pueblo greeting, which turns
	/// the connection's Pueblo output on as <c>SOCKSET PUEBLO=</c> does. The greeting goes out, and the client's
	/// usual answer is read before it gets here, only while the <c>pueblo</c> option is on; while it is off the
	/// line does nothing.
	/// </summary>
	[SharpCommand(Name = "PUEBLOCLIENT", Switches = [], Behavior = CB.SOCKET | CB.NoParse, MinArgs = 0, MaxArgs = 0,
		ParameterNames = ["version"])]
	public async ValueTask<Option<CallState>> PuebloClient(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (!Configuration.CurrentValue.Net.Pueblo || CurrentConnection(parser) is not { } connection)
			return new None();

		var result = SocketOptions.Set(connection, "PUEBLO", $"PUEBLOCLIENT {SocketArgument(parser)}");
		await PublishSocketPinsAsync(connection, result.Probe, result.Pueblo, result.Telnet);
		return new None();
	}
}
