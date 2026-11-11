using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// TinyMUX's and RhostMUSH's <c>@program</c>, <c>@quitprogram</c> and RhostMUSH's <c>@progreset</c>, on SharpMUSH's
/// guided input: a program is an input session (<see cref="InputMatch.Program"/>) whose one route takes the
/// player's next line.
/// </summary>
public partial class Commands
{
	/// <summary>How long a program waits for its line, the longest an input session may.</summary>
	private static readonly TimeSpan ProgramWait = TimeSpan.FromHours(1);

	/// <summary>A player by name, dbref or <c>me</c>.</summary>
	private const LocateFlags ProgramPlayerFlags = LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference |
		LocateFlags.EnglishStyleMatching | LocateFlags.MatchOptionalWildCardForPlayerName | LocateFlags.AbsoluteMatch |
		LocateFlags.MatchMeForLooker;

	/// <summary>The prompt a program shows when it is given none, as TinyMUX and RhostMUSH show.</summary>
	private const string ProgramPrompt = ">";

	/// <summary>
	/// <c>@program &lt;player&gt;=&lt;object&gt;/&lt;attribute&gt;[:&lt;message&gt;]</c>: the player's next line runs the
	/// attribute, as the executor, with the line in <c>%0</c> and the player as enactor; then the program ends.
	/// The message is shown to the player first. A line starting with <c>|</c> runs as a command instead.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_PROGRAM", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["player", "object/attribute"])]
	public async ValueTask<Option<CallState>> DialectProgram(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? string.Empty;
		if (name.Length == 0)
			return await ProgramNotice(executor, "No valid player specified.");

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, ProgramPlayerFlags) switch
		{
			AnySharpObject player => await StartProgramAsync(parser, executor, player,
				args.GetValueOrDefault("1")?.Message.ToPlainText().Trim() ?? string.Empty),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> StartProgramAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject player, string specification)
	{
		if (!player.IsPlayer)
			return await ProgramNotice(executor, "That is not a player.");
		if (await ProgramRefusalAsync(executor, player) is { } refused) return refused;

		var sessions = parser.ServiceProvider.GetRequiredService<IInputSessionService>();
		var connections = await ConnectedHandlesAsync(player);
		if (connections.Count == 0)
			return await ProgramNotice(executor, "That player is not connected.");
		if (connections.Any(connection => sessions.GetCapturing(connection.Handle) is not null))
			return await ProgramNotice(executor, IsSelf(executor, player)
				? "You are already in a program."
				: "That player is already in a program.");

		// The message starts at the first colon after the slash: an object's objid has a colon of its own.
		var colon = specification.IndexOf(':', Math.Max(specification.IndexOf('/'), 0));
		var path = colon < 0 ? specification : specification[..colon];
		var message = colon < 0 ? string.Empty : specification[(colon + 1)..];
		var slash = path.IndexOf('/');
		if (path.Length == 0)
			return await ProgramNotice(executor, "No such attribute.");
		if (slash <= 0 || slash == path.Length - 1)
			return await ProgramNotice(executor, "No match.");

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, path[..slash], LocateFlags.All) switch
		{
			AnySharpObject holder => await WaitForLineAsync(parser, sessions, executor, player,
				ProgramConnection(parser, connections), new InputRouteSpec("*", holder.Object().DBRef, path[(slash + 1)..]), message),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> WaitForLineAsync(IMUSHCodeParser parser, IInputSessionService sessions,
		AnySharpObject executor, AnySharpObject player, IConnectionService.ConnectionData connection, InputRouteSpec route,
		string message)
	{
		if (message.Length > 0)
			await NotifyService.Notify(player, message);

		var error = await parser.With(state => state with
		{
			Handle = connection.Handle,
			ConnectionSessionId = connection.Metadata.GetValueOrDefault("SessionId"),
			Enactor = player.Object().DBRef
		}, waiting => sessions.StartAsync(waiting, MarkupText.Plain(ProgramPrompt), [route], InputMatch.Program, ProgramWait));

		return error is null ? CallState.Empty : await ProgramNotice(executor, error);
	}

	/// <summary>
	/// <c>@quitprogram[/quiet] [&lt;player&gt;]</c>: ends the player's program, or the executor's, without running it.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_QUITPROGRAM", Switches = ["QUIET"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> DialectQuitProgram(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.ArgumentsOrdered.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? string.Empty;
		if (name.Length == 0)
			return await QuitProgramAsync(parser, executor, executor);

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, ProgramPlayerFlags) switch
		{
			AnySharpObject player => await QuitProgramAsync(parser, executor, player),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> QuitProgramAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject player)
	{
		if (!player.IsPlayer)
			return await ProgramNotice(executor, "Invalid player specified.");
		if (await ProgramRefusalAsync(executor, player) is { } refused) return refused;

		var sessions = parser.ServiceProvider.GetRequiredService<IInputSessionService>();
		var connections = await ConnectedHandlesAsync(player);
		if (connections.Count == 0)
			return await ProgramNotice(executor, "That player is not connected.");

		var programs = Programs(sessions, connections);
		if (programs.Count == 0)
			return await ProgramNotice(executor, IsSelf(executor, player) ? "You are not in a program." : "That player is not in a program.");

		foreach (var program in programs) sessions.Discard(program);

		if (parser.CurrentState.Switches.Contains("QUIET")) return CallState.Empty;
		await NotifyService.Notify(executor, "@program cleared.");
		await NotifyService.Notify(player, IsSelf(executor, player)
			? "You have aborted your program."
			: $"Your @program has been terminated by {executor.Object().Name}.");
		return CallState.Empty;
	}

	/// <summary>
	/// RhostMUSH's <c>@progreset &lt;player&gt;[=&lt;prompt&gt;]</c>: gives a player's running program a new prompt,
	/// or, for a player in no program, clears a prompt left on their screen.
	/// </summary>
	[SharpCommand(Name = "@RHOST_PROGRESET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["player", "prompt"])]
	public async ValueTask<Option<CallState>> RhostProgramReset(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? string.Empty;
		var prompt = args.GetValueOrDefault("1")?.Message ?? MarkupText.Empty;
		if (name.Length == 0)
			return await ResetProgramAsync(parser, executor, executor, prompt);

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, ProgramPlayerFlags) switch
		{
			AnySharpObject player => await ResetProgramAsync(parser, executor, player, prompt),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> ResetProgramAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject player, MString prompt)
	{
		if (!await PermissionService.Controls(executor, player))
			return await ProgramNotice(executor, "No matching target found.");
		if (prompt.ToPlainText().Length > 80)
			return await ProgramNotice(executor, "Custom prompt exceeds 80 characters.  Not setting.");

		var sessions = parser.ServiceProvider.GetRequiredService<IInputSessionService>();
		var connections = await ConnectedHandlesAsync(player);
		var programs = Programs(sessions, connections);
		var hasPrompt = prompt.ToPlainText().Trim().Length > 0;

		if (programs.Count == 0)
		{
			if (hasPrompt)
				return await ProgramNotice(executor, "Can not assign a prompt to someone not in a program.");
			foreach (var connection in connections) await sessions.ClearPromptUnlessCapturingAsync(connection.Handle);
			return await ProgramNotice(executor, "Program prompt reset.", CallState.Empty);
		}

		if (!hasPrompt)
			return await ProgramNotice(executor, "Player is in a program, can not reset.");

		foreach (var program in programs)
		{
			await parser.With(state => state with
			{
				Handle = program.Connection.Handle,
				ConnectionSessionId = program.TransportSessionId,
				Executor = program.Character
			}, prompting => sessions.PromptAsync(prompting, prompt));
		}

		return await ProgramNotice(executor, "Program prompt customized.", CallState.Empty);
	}

	/// <summary>
	/// Why <paramref name="executor"/> may not run or end a program for <paramref name="player"/>, having said so:
	/// the player may for themselves, and so may whoever controls them.
	/// </summary>
	private async ValueTask<CallState?> ProgramRefusalAsync(AnySharpObject executor, AnySharpObject player)
		=> IsSelf(executor, player) || await PermissionService.Controls(executor, player)
			? null
			: await NotifyService.PermissionDeniedNotControlled(executor, player);

	private static bool IsSelf(AnySharpObject executor, AnySharpObject player)
		=> executor.Object().DBRef == player.Object().DBRef;

	/// <summary>The player's logged-in connections.</summary>
	private async ValueTask<List<IConnectionService.ConnectionData>> ConnectedHandlesAsync(AnySharpObject player)
		=> await ConnectionService.Get(player.Object().DBRef)
			.Where(connection => connection.State == IConnectionService.ConnectionState.LoggedIn)
			.ToListAsync();

	/// <summary>The programs open on <paramref name="connections"/>.</summary>
	private static List<InputSession> Programs(IInputSessionService sessions, IEnumerable<IConnectionService.ConnectionData> connections)
		=> connections
			.Select(connection => sessions.GetCapturing(connection.Handle))
			.OfType<InputSession>()
			.Where(session => session.Match == InputMatch.Program)
			.ToList();

	/// <summary>
	/// The connection a program waits on: the one that typed the command when it is the player's, else the one the
	/// player used last.
	/// </summary>
	private static IConnectionService.ConnectionData ProgramConnection(IMUSHCodeParser parser,
		IReadOnlyList<IConnectionService.ConnectionData> connections)
		=> connections.FirstOrDefault(connection => connection.Handle == parser.CurrentState.Handle)
			?? connections.MaxBy(connection => long.TryParse(connection.Metadata.GetValueOrDefault("LastConnectionSignal"), out var signal)
				? signal
				: long.MinValue)!;

	private async ValueTask<Option<CallState>> ProgramNotice(AnySharpObject executor, string message, CallState? result = null)
	{
		await NotifyService.Notify(executor, message);
		return result ?? new CallState(message.StartsWith("#-1", StringComparison.Ordinal) ? message : $"#-1 {message}");
	}
}
