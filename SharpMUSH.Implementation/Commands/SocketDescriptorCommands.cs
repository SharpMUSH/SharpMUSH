using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using System.Globalization;
using System.Text;
using System.Buffers;
using SharpMUSH.Library.Models;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// The descriptor-scoped half of PennMUSH's socket command set — the commands
/// <c>src/bsd.c do_command()</c> answers <i>before</i> it branches on <c>d-&gt;connected</c>, so each
/// of them works identically at the connect screen and in game. That placement is the whole point:
/// a crawler bot sends <c>MSSP-REQUEST</c> without logging in, and a logged-in player still expects
/// <c>SCREENWIDTH</c> to reach their own descriptor rather than an object's.
///
/// <para>
/// Every command here therefore carries <see cref="CommandBehavior.SOCKET"/>, which
/// <c>SharpMUSHParserVisitor.EvaluateCommands</c> dispatches by exact name for any handle, logged in
/// or not, and which keeps them out of the in-game abbreviation trie.
/// </para>
/// </summary>
public partial class Commands
{
	/// <summary>PennMUSH <c>INFO_VERSION</c> (hdrs/conf.h) — the version of the INFO reply format.</summary>
	private const string InfoVersion = "1.1";

	/// <summary>
	/// The connection that typed the command. Socket commands act on their own descriptor, never on
	/// "the executor's first connection": a player with two clients open must be able to set the
	/// screen width of the one they are typing into.
	/// </summary>
	private IConnectionService.ConnectionData? CurrentConnection(IMUSHCodeParser parser)
		=> parser.CurrentState.Handle is { } handle ? ConnectionService.Get(handle) : null;

	/// <summary>
	/// The single unparsed argument of a <c>SOCKET | NoParse</c> command: everything after the
	/// command word. PennMUSH reads these with <c>strncmp</c> and then takes the remainder verbatim,
	/// so no evaluation, splitting or trimming happens on the way in.
	/// </summary>
	private string SocketArgument(IMUSHCodeParser parser)
		=> parser.CurrentState.Arguments.TryGetValue("0", out var arg)
			? arg.Message?.ToPlainText() ?? string.Empty
			: string.Empty;

	/// <summary>
	/// PennMUSH <c>show_tm()</c> (src/strutil.c): <c>asctime()</c> without its trailing newline, with
	/// the day-of-month zero-padded rather than space-padded.
	/// </summary>
	private string ShowTime(DateTimeOffset when)
		=> when.ToLocalTime().ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);

	/// <summary>
	/// PennMUSH <c>count_players()</c> (src/bsd.c): connected descriptors that have a player behind
	/// them, skipping hidden (DARK) ones unless <c>count_all</c> is set.
	/// </summary>
	private async ValueTask<int> CountPlayers()
	{
		var countAll = Configuration.CurrentValue.Cosmetic.CountAll;
		var count = 0;

		await foreach (var connection in ConnectionService.GetAll())
		{
			if (connection.Ref is not { } reference) continue;

			// GoodObject first, and unconditionally: a handle can outlive the object it is bound to, and
			// such a descriptor is not a connected player under any counting rule.
			if (await Mediator.Send(new GetObjectNodeQuery(reference)) is not AnySharpObject found) continue;

			if (!countAll && await found.IsDark()) continue;

			count++;
		}

		return count;
	}

	/// <summary>
	/// <c>INFO</c> — PennMUSH <c>dump_info()</c> (src/bsd.c). A fixed, machine-readable block that
	/// MUD listing bots scrape; the field order and the <c>### Begin/End INFO</c> sentinels are part
	/// of the contract, so they are reproduced literally rather than prettified.
	/// </summary>
	[SharpCommand(Name = "INFO", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Info(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var net = Configuration.CurrentValue.Net;
		var uptime = await ObjectDataService.GetExpandedServerDataAsync<UptimeData>();
		var size = await Mediator.Send(new GetObjectCountQuery());

		// PennMUSH prints "Address:" unconditionally, even when mud_url is unset — unlike @version,
		// which omits the line. The block is a fixed-shape record for bots, so a field never vanishes.
		var lines = new[]
		{
			$"### Begin INFO {InfoVersion}",
			$"Name: {net.MudName}",
			$"Address: {net.MudUrl}",
			$"Uptime: {ShowTime(uptime?.StartTime ?? DateTimeOffset.UtcNow)}",
			$"Connected: {await CountPlayers()}",
			$"Size: {size}",
			$"Version: SharpMUSH {Implementation.Generated.VersionInfo.SharpMUSHVersion}",
			"### End INFO"
		};

		await NotifyService.Notify(parser.CurrentState.Handle!.Value, string.Join("\n", lines));

		return new None();
	}

	/// <summary>
	/// <c>MSSP-REQUEST</c> — PennMUSH <c>report_mssp()</c> (src/bsd.c) in its descriptor form: the
	/// same values the MSSP telnet option carries, as plain tab-separated text for crawlers that
	/// never negotiate telnet.
	/// </summary>
	[SharpCommand(Name = "MSSP-REQUEST", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> MsspRequest(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var net = Configuration.CurrentValue.Net;
		var uptime = await ObjectDataService.GetExpandedServerDataAsync<UptimeData>();

		// Leading blank line and tab separators are PennMUSH's, and the MSSP spec's.
		var lines = new List<string> { string.Empty, "MSSP-REPLY-START" };

		lines.Add($"NAME\t{net.MudName}");
		lines.Add($"PLAYERS\t{await CountPlayers()}");
		lines.Add($"UPTIME\t{(uptime?.StartTime ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()}");
		lines.Add($"PORT\t{net.Port}");
		if (net.SslPort != 0)
		{
			lines.Add($"SSL\t{net.SslPort}");
		}
		lines.Add($"PUEBLO\t{(net.Pueblo ? 1 : 0)}");
		lines.Add($"CODEBASE\tSharpMUSH {Implementation.Generated.VersionInfo.SharpMUSHVersion}");
		lines.Add("FAMILY\tTinyMUD");
		if (!string.IsNullOrEmpty(net.MudUrl))
		{
			lines.Add($"WEBSITE\t{net.MudUrl}");
		}

		// Deliberate divergence. PennMUSH nests the terminator inside `if (mssp)`, so a game with no
		// admin-defined mssp entries answers MSSP-REQUEST with a reply that never ends — a crawler
		// reading until MSSP-REPLY-END waits for a sentinel that is not coming. SharpMUSH has no
		// admin mssp option yet, so copying that would make every reply unterminated. The terminator
		// is unconditional here; the spec requires it.
		lines.Add("MSSP-REPLY-END");

		await NotifyService.Notify(parser.CurrentState.Handle!.Value, string.Join("\n", lines));

		return new None();
	}

	/// <summary>
	/// <c>VERSION</c> — a deliberate divergence from PennMUSH, which has no bare <c>VERSION</c> socket
	/// command (only <c>@version</c>). Crawlers and players arriving from MUX-family servers type it
	/// unprefixed, and answering costs nothing that <c>INFO</c> does not already publish, so it is
	/// accepted here and reports the same lines <c>@version</c> does.
	/// </summary>
	[SharpCommand(Name = "VERSION", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> SocketVersion(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var net = Configuration.CurrentValue.Net;
		var uptime = await ObjectDataService.GetExpandedServerDataAsync<UptimeData>();

		var lines = new List<string> { $"You are connected to {net.MudName}" };

		// Same omission rule as @version (PennMUSH do_version, src/version.c): an unset mud_url means
		// the game publishes no address, which is not the same fact as "the address is unknown".
		if (!string.IsNullOrWhiteSpace(net.MudUrl))
		{
			lines.Add($"Address: {net.MudUrl}");
		}

		if (uptime is not null)
		{
			lines.Add($"Last restarted: {ShowTime(uptime.LastRebootTime)}");
		}

		lines.Add(Implementation.Generated.VersionInfo.Version);

		await NotifyService.Notify(parser.CurrentState.Handle!.Value, string.Join("\n", lines));

		return new None();
	}

	/// <summary>
	/// <c>IDLE</c> — PennMUSH's anti-timeout no-op (src/bsd.c). Two details are load-bearing: any text
	/// after the command word is echoed straight back (one separating space consumed), and the
	/// command deliberately does <b>not</b> refresh the idle timer or bump the command count, because
	/// <c>do_command</c> handles IDLE above the lines that do. <c>EvaluateCommands</c> already carves
	/// IDLE out of both counters, so this only has to perform the echo.
	/// </summary>
	[SharpCommand(Name = "IDLE", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["echo"])]
	public async ValueTask<Option<CallState>> Idle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var echo = SocketArgument(parser);

		if (!string.IsNullOrEmpty(echo))
		{
			await NotifyService.Notify(parser.CurrentState.Handle!.Value, echo);
		}

		return new None();
	}

	/// <summary>
	/// <c>SCREENWIDTH &lt;columns&gt;</c> — PennMUSH sets <c>d-&gt;width</c> from the argument and says
	/// nothing back. It is the manual counterpart to the NAWS telnet option, which writes the same
	/// <c>WIDTH</c> value from the client side.
	/// </summary>
	[SharpCommand(Name = "SCREENWIDTH", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["columns"])]
	public ValueTask<Option<CallState>> ScreenWidth(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetScreenDimension(parser, "WIDTH");

	/// <summary>
	/// <c>SCREENHEIGHT &lt;rows&gt;</c> — the <c>d-&gt;height</c> counterpart of <see cref="ScreenWidth"/>.
	/// </summary>
	[SharpCommand(Name = "SCREENHEIGHT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["rows"])]
	public ValueTask<Option<CallState>> ScreenHeight(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetScreenDimension(parser, "HEIGHT");

	private ValueTask<Option<CallState>> SetScreenDimension(IMUSHCodeParser parser, string key)
	{
		// PennMUSH parse_integer() on a non-numeric argument yields 0, and the descriptor is set to it
		// silently. Storing the parsed value rather than the raw text keeps the metadata key in the
		// shape NAWS writes it, so readers never have to cope with two encodings of the same fact.
		var value = int.TryParse(SocketArgument(parser).Trim(), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: 0;

		ConnectionService.Update(parser.CurrentState.Handle!.Value, key, value.ToString(CultureInfo.InvariantCulture));

		return ValueTask.FromResult<Option<CallState>>(new None());
	}

	/// <summary>
	/// <c>PROMPT_NEWLINES &lt;0|1&gt;</c> — whether a newline follows a prompt on this descriptor
	/// (PennMUSH <c>CONN_PROMPT_NEWLINES</c>). Silent, like the SCREEN* pair.
	/// </summary>
	[SharpCommand(Name = "PROMPT_NEWLINES", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["enabled"])]
	public ValueTask<Option<CallState>> PromptNewlines(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enabled = int.TryParse(SocketArgument(parser).Trim(), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed) && parsed != 0;

		ConnectionService.Update(parser.CurrentState.Handle!.Value,
			SocketOptions.PromptNewlinesKey, enabled ? "1" : "0");

		return ValueTask.FromResult<Option<CallState>>(new None());
	}

	/// <summary>
	/// <c>LOGOUT</c> — PennMUSH <c>logout_sock()</c> (src/bsd.c). Leaves the character but keeps the
	/// socket: the player is returned to the connect screen and can log in again, or as someone else,
	/// without reconnecting. That is the whole distinction from QUIT, which takes the socket with it.
	///
	/// <para>
	/// The descriptor is deliberately left looking new — PennMUSH clears <c>output_prefix</c>,
	/// <c>output_suffix</c>, the command count and the hide flag, then calls the same
	/// <c>welcome_user</c> a fresh connection gets. A screen-scraping client's OUTPUTPREFIX surviving
	/// into someone else's session would be a leak between two logins on one socket.
	/// </para>
	/// </summary>
	[SharpCommand(Name = "LOGOUT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Logout(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var handle = parser.CurrentState.Handle!.Value;
		var connection = CurrentConnection(parser);

		if (connection is null)
		{
			return new None();
		}

		// PennMUSH logs "Logout, never connected. <Connection not dropped>" and does nothing else: at
		// the connect screen there is no session to end, and the socket must not be disturbed.
		if (connection.Ref is null)
		{
			Logger?.LogInformation("Logout on handle {Handle}, never connected. <Connection not dropped>", handle);
			return new None();
		}

		// disconnect_player dumps the quit file for a logout exactly as it does for a quit.
		var quitText = await ReadMessageFileAsync(Configuration.CurrentValue.Message.QuitFile);
		if (!string.IsNullOrWhiteSpace(quitText))
		{
			await NotifyService.Notify(handle, quitText);
		}

		// Cleared before the unbind so the connect screen, which the state change sends, is not itself
		// wrapped in the departing player's OUTPUTPREFIX.
		connection.Metadata.TryRemove("OutputPrefix", out _);
		connection.Metadata.TryRemove("OutputSuffix", out _);
		connection.Metadata["CommandCount"] = "0";

		await ConnectionService.Unbind(handle);

		Logger?.LogInformation("Logout by {Player} on handle {Handle} <Connection not dropped>",
			connection.Ref, handle);

		return new None();
	}

	/// <summary>
	/// <c>SOCKSET [&lt;option&gt;=&lt;value&gt;]</c> — PennMUSH <c>sockset_wrapper()</c> (src/bsd.c).
	/// With no argument it reports the descriptor's settings; with <c>option=value</c> it sets one and
	/// echoes the result; with an argument but no <c>=</c> it complains. The wizard-only
	/// <c>@sockset</c> reaches the same engine against another descriptor.
	/// </summary>
	[SharpCommand(Name = "SOCKSET", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["option"])]
	public async ValueTask<Option<CallState>> Sockset(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var handle = parser.CurrentState.Handle!.Value;
		var connection = CurrentConnection(parser);

		if (connection is null)
		{
			await NotifyService.NotifyLocalized(handle, nameof(ErrorMessages.Notifications.SocksetNotConnected));
			return new None();
		}

		var argument = SocketArgument(parser).TrimStart();

		if (argument.Length == 0)
		{
			await NotifyService.Notify(handle, SocketOptions.Show(connection, "\n",
				await ArgHelpers.ColorFlagsOfAsync(Mediator, connection.Ref)));
			return new None();
		}

		var separator = argument.IndexOf('=');
		if (separator < 0)
		{
			await NotifyService.NotifyLocalized(handle, nameof(ErrorMessages.Notifications.SocksetNeedsOptionAndValue));
			return new None();
		}

		var result = SocketOptions.Set(connection, argument[..separator], argument[(separator + 1)..]);
		await NotifyService.NotifyLocalized(handle, result.Key, result.Arguments);
		await PublishColorStyleAsync(connection);

		return new None();
	}

	/// <summary>
	/// A colour-style pin changes nothing until the socket owner knows about it — that process, not
	/// this one, renders output. The value is read back off the descriptor rather than out of the
	/// <see cref="SocketOptions.SocksetResult"/>, which carries the message and not the setting, so
	/// this stays correct for any option name that ends up writing the key.
	/// </summary>
	private async ValueTask PublishColorStyleAsync(IConnectionService.ConnectionData connection)
	{
		if (MessageBus is null)
		{
			return;
		}

		// Absent means "auto": the flags and the negotiated terminal decide again.
		await MessageBus.Publish(new UpdateColorStyleMessage(connection.Handle,
			connection.Metadata.GetValueOrDefault(SocketOptions.ColorStyleKey)));
	}

	/// <summary>
	/// <c>@sockset [&lt;descriptor&gt;]=&lt;option&gt;,&lt;value&gt;[,&lt;option&gt;,&lt;value&gt;…]</c> —
	/// PennMUSH <c>cmd_sockset</c> (src/cmds.c). The in-game face of the same option engine the
	/// <c>SOCKSET</c> socket command drives, with a descriptor argument so a wizard can adjust someone
	/// else's connection.
	///
	/// <para>
	/// Not wizard-only: PennMUSH lets anyone read and set options on their <i>own</i> descriptor here,
	/// and only requires privilege to reach another player's. Refusing mortals outright, as this
	/// command used to, made <c>@sockset</c> useless for the people it is mostly for.
	/// </para>
	/// </summary>
	[SharpCommand(Name = "@SOCKSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["socket", "option", "value"])]
	public async ValueTask<Option<CallState>> SocketSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var isWizard = await executor.IsWizard();

		var descriptorArg = args.TryGetValue("0", out var arg0) ? arg0.Message?.ToPlainText().Trim() ?? string.Empty : string.Empty;

		var target = await ResolveSocksetTarget(parser, executor, descriptorArg, isWizard);
		if (target is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SocksetInvalidDescriptor), executor);
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		// PennMUSH compares *player* identity here, not descriptor identity: a player with two clients
		// open may @sockset either of their own connections. Only reaching someone else's needs wizard.
		var isOwnDescriptor = target.Ref == executor.Object().DBRef;

		if (!isOwnDescriptor && !isWizard)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// PennMUSH walks args_right in (option, value) pairs starting at index 1, so the right-hand
		// side is "OPTION,VALUE" — not "OPTION=VALUE" — and several pairs may be set in one command.
		var pairs = args.Where(kv => kv.Key != "0")
			.OrderBy(kv => int.Parse(kv.Key))
			.Select(kv => kv.Value.Message?.ToPlainText() ?? string.Empty)
			.ToArray();

		if (pairs.Length == 0)
		{
			await NotifyService.Notify(executor, SocketOptions.Show(target, "\n",
				await ArgHelpers.ColorFlagsOfAsync(Mediator, target.Ref)), executor);
			return CallState.Empty;
		}

		for (var i = 0; i + 1 < pairs.Length; i += 2)
		{
			var result = SocketOptions.Set(target, pairs[i], pairs[i + 1]);
			await NotifyService.NotifyLocalized(executor, result.Key, result.Arguments);
		}

		// Once, after the whole run: several pairs may be set in one command, and only the descriptor's
		// final state is worth telling the socket owner about.
		await PublishColorStyleAsync(target);

		// An odd trailing element means the last option arrived without a value; PennMUSH answers the
		// same way it answers an empty option name.
		if (pairs.Length % 2 != 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SocksetSetWhatOption), executor);
		}

		return CallState.Empty;
	}

	/// <summary>
	/// PennMUSH <c>lookup_desc()</c> (src/bsd.c): an empty argument means "the descriptor I am on", a
	/// number means that descriptor, and anything else is a player name whose least-idle connection is
	/// used.
	///
	/// <para>
	/// A descriptor number resolves for an unprivileged executor only when the descriptor is theirs.
	/// PennMUSH returns NULL otherwise, so the caller reports "Invalid descriptor." rather than a
	/// permission error: refusing by permission would tell a mortal which handle numbers are live.
	/// </para>
	/// </summary>
	private async ValueTask<IConnectionService.ConnectionData?> ResolveSocksetTarget(
		IMUSHCodeParser parser, AnySharpObject executor, string descriptorArg, bool isWizard)
	{
		if (descriptorArg.Length == 0)
		{
			return CurrentConnection(parser) ?? await LeastIdleConnection(executor.Object().DBRef);
		}

		if (long.TryParse(descriptorArg, out var handle))
		{
			var connection = ConnectionService.Get(handle);

			return connection is not null && (isWizard || connection.Ref == executor.Object().DBRef)
				? connection
				: null;
		}

		var player = await Mediator.CreateStream(new GetPlayerQuery(descriptorArg)).FirstOrDefaultAsync();
		if (player is null) return null;

		var playerRef = new DBRef(player.Object.Key, player.Object.CreationTime);

		return isWizard || playerRef == executor.Object().DBRef
			? await LeastIdleConnection(playerRef)
			: null;
	}

	/// <inheritdoc cref="ArgHelpers.LeastIdleConnectionAsync"/>
	private ValueTask<IConnectionService.ConnectionData?> LeastIdleConnection(DBRef who)
		=> ArgHelpers.LeastIdleConnectionAsync(ConnectionService, who);

	[SharpCommand(Name = "SESSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Session(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var connection = await ConnectionService.Get(executor.Object().DBRef).FirstOrDefaultAsync();

		if (connection == null)
		{
			await NotifyService.Notify(executor, "No session information available.", executor);
			return CallState.Empty;
		}

		var output = new System.Text.StringBuilder();
		output.AppendLine("Session Information:");
		output.AppendLine($"  Player: {executor.Object().Name} (#{executor.Object().DBRef.Number})");

		if (connection.Connected.HasValue)
		{
			output.AppendLine($"  Connected: {TimeHelpers.TimeString(connection.Connected.Value)} ago");
		}

		if (connection.Idle.HasValue)
		{
			output.AppendLine($"  Idle: {TimeHelpers.TimeString(connection.Idle.Value)}");
		}

		if (!string.IsNullOrEmpty(connection.HostName))
		{
			output.AppendLine($"  Host: {connection.HostName}");
		}

		await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
		return CallState.Empty;
	}

	/// <summary>
	/// <c>OUTPUTPREFIX &lt;text&gt;</c> — PennMUSH src/bsd.c, <c>set_userstring(&amp;d-&gt;output_prefix, ...)</c>.
	/// A descriptor setting, not a player one: it is handled above the <c>d-&gt;connected</c> branch in
	/// <c>do_command</c>, so it answers at the connect screen too, and it applies to the socket that
	/// typed it rather than to whichever of the player's clients happens to be listed first.
	/// PennMUSH says nothing back — robot clients set this on every command and would drown in
	/// acknowledgements — so the confirmation lives only on <c>SOCKSET OUTPUTPREFIX=...</c>.
	/// </summary>
	[SharpCommand(Name = "OUTPUTPREFIX", Switches = [], Behavior = CB.SOCKET | CB.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = ["prefix"])]
	public ValueTask<Option<CallState>> OutputPrefix(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetUserString(parser, "OutputPrefix");

	/// <summary>
	/// <c>OUTPUTSUFFIX &lt;text&gt;</c> — the trailing counterpart of <see cref="OutputPrefix"/>, and
	/// silent for the same reason.
	/// </summary>
	[SharpCommand(Name = "OUTPUTSUFFIX", Switches = [], Behavior = CB.SOCKET | CB.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = ["suffix"])]
	public ValueTask<Option<CallState>> OutputSuffix(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetUserString(parser, "OutputSuffix");

	/// <summary>
	/// PennMUSH <c>set_userstring()</c> (src/bsd.c): leading whitespace is skipped, an otherwise empty
	/// value clears the setting, and trailing whitespace is kept — a prefix of <c>"&gt;&gt; "</c> is a
	/// legitimate thing to ask for.
	/// </summary>
	private ValueTask<Option<CallState>> SetUserString(IMUSHCodeParser parser, string key)
	{
		var connection = CurrentConnection(parser);
		if (connection is null)
		{
			return ValueTask.FromResult<Option<CallState>>(new None());
		}

		var value = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			.ToPlainText().TrimStart();

		if (string.IsNullOrEmpty(value))
		{
			connection.Metadata.TryRemove(key, out _);
		}
		else
		{
			connection.Metadata[key] = value;
		}

		return ValueTask.FromResult<Option<CallState>>(new None());
	}

	/// <summary>
	/// @locale [locale]
	/// With no argument: displays the executor's current locale.
	/// With an empty argument (@locale =): clears the locale back to the server default ("en").
	/// With a non-empty argument: validates and sets the locale for the current session and persists it
	/// as the LOCALE attribute on the player object.
	/// Locale strings are BCP-47 tags (e.g. "en", "fr", "de").
	/// </summary>
	[SharpCommand(Name = "@LOCALE", Switches = [], Behavior = CB.Default | CB.NoParse | CB.EqSplit, MinArgs = 0, MaxArgs = 1, ParameterNames = ["locale"])]
	public async ValueTask<Option<CallState>> SetLocale(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;

		// No '=' sign at all → display current locale.
		if (args.Count == 0)
		{
			var current = "en";
			var handle = parser.CurrentState.Handle;
			if (handle.HasValue)
			{
				// Use the specific connection that ran @locale to avoid multi-session ambiguity.
				var conn = ConnectionService.Get(handle.Value);
				if (conn is not null && conn.Metadata.TryGetValue("Locale", out var stored) && !string.IsNullOrEmpty(stored))
				{
					current = stored;
				}
			}
			else
			{
				// No direct handle (e.g. @force context) — fall back to persisted LOCALE attribute.
				// Through the Mediator, not the store: GetAttributeQuery is ICacheable, and reading the
				// same attribute around the cache is what leaves a write's invalidation with nothing
				// to invalidate (engine data trunk §1).
				current = await Mediator.CreateStream(new GetAttributeQuery(executor.Object().DBRef, ["LOCALE"]))
					.Select(attr => attr.Value.ToPlainText())
					.FirstOrDefaultAsync(saved => !string.IsNullOrEmpty(saved)) ?? current;
			}
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleCurrentFormat), executor, current);
			return CallState.Empty;
		}

		var locale = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText().Trim();

		// Explicit empty argument (@locale =) → clear locale back to server default.
		if (string.IsNullOrEmpty(locale))
		{
			await AttributeService.ClearAttributeAsync(executor, executor, "LOCALE",
				IAttributeService.AttributePatternMode.Exact);

			await foreach (var conn in ConnectionService.Get(executor.Object().DBRef))
			{
				if (conn.State == IConnectionService.ConnectionState.LoggedIn)
				{
					ConnectionService.Update(conn.Handle, "Locale", string.Empty);
				}
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleCleared), executor);
			return CallState.Empty;
		}

		System.Globalization.CultureInfo? culture;
		try
		{
			culture = System.Globalization.CultureInfo.GetCultureInfo(locale);
		}
		catch (System.Globalization.CultureNotFoundException)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleInvalidFormat), executor, locale);
			return CallState.Empty;
		}

		var canonicalLocale = culture.Name; // e.g. "en-US" → "en-US", "fr" → "fr"

		// Persist to the player's LOCALE attribute so it survives reconnects.
		await AttributeService.SetAttributeAsync(executor, executor, "LOCALE", MarkupText.Plain(canonicalLocale));

		await foreach (var conn in ConnectionService.Get(executor.Object().DBRef))
		{
			if (conn.State == IConnectionService.ConnectionState.LoggedIn)
			{
				ConnectionService.Update(conn.Handle, "Locale", canonicalLocale);
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleSetFormat), executor, canonicalLocale);
		return CallState.Empty;
	}
}

/// <summary>
/// PennMUSH's <c>sockset_show()</c> / <c>sockset()</c> pair (src/bsd.c), lifted out of the command so
/// the socket <c>SOCKSET</c> and the wizard <c>@sockset</c> share one implementation and cannot drift.
/// </summary>
public static class SocketOptions
{
	internal const string PromptNewlinesKey = "PROMPT_NEWLINES";
	internal const string StripAccentsKey = "STRIPACCENTS";
	internal const string NoQuotaKey = "NOQUOTA";
	internal const string ColorStyleKey = "COLORSTYLE";

	/// <summary>
	/// The settings report. PennMUSH lays this out as a 15-column label followed by two spaces and the
	/// value, and omits the prefix/suffix rows entirely when they are unset.
	/// </summary>
	/// <param name="colorFlags">
	/// The colour flags of whoever is behind the descriptor, or null at the connect screen. They can
	/// raise the depth above what the terminal negotiated, so the "auto (...)" reading is wrong
	/// without them.
	/// </param>
	public static string Show(IConnectionService.ConnectionData connection, string newLine,
		PlayerColorFlags? colorFlags = null)
	{
		var builder = new StringBuilder();
		builder.Append(newLine);

		void Row(string label, string value) => builder.Append($"{label,-15}:  {value}").Append(newLine);

		if (connection.Metadata.TryGetValue("OutputPrefix", out var prefix) && !string.IsNullOrEmpty(prefix))
		{
			Row("OUTPUTPREFIX", prefix);
		}

		if (connection.Metadata.TryGetValue("OutputSuffix", out var suffix) && !string.IsNullOrEmpty(suffix))
		{
			Row("OUTPUTSUFFIX", suffix);
		}

		Row("Pueblo", YesNo(connection.Metadata.GetValueOrDefault("PUEBLO") == "1"));
		// Whether the client answered telnet negotiation, not which port it arrived on — the same
		// CONN_TELNET terminfo() reports, so the two cannot disagree about a raw socket.
		Row("Telnet", YesNo(connection.Metadata.GetValueOrDefault("TELNET") == "1"));
		Row("Width", connection.Metadata.GetValueOrDefault("WIDTH", "78"));
		Row("Height", connection.Metadata.GetValueOrDefault("HEIGHT", "24"));
		Row("Terminal Type", connection.Metadata.GetValueOrDefault("TerminalType", "unknown"));
		Row("Stripaccents", YesNo(connection.Metadata.GetValueOrDefault(StripAccentsKey) == "1"));

		// PennMUSH reports "auto (<derived>)" until the style has been pinned explicitly, so the
		// player can tell a negotiated default apart from a choice they made. The derived half is the
		// one terminfo() reports, read from the client's own terminal types rather than assumed.
		var colorStyle = connection.Metadata.GetValueOrDefault(ColorStyleKey);
		Row("Color Style", colorStyle
			?? $"auto ({TerminalCapabilityReader.ColorStyleFor(connection.Metadata, colorFlags)})");

		builder.Append($"{"Prompt Newlines",-15}:  {YesNo(connection.Metadata.GetValueOrDefault(PromptNewlinesKey) == "1")}");

		return builder.ToString();

		static string YesNo(bool value) => value ? "Yes" : "No";
	}

	/// <summary>
	/// The message an option assignment produced, as a resource key plus its format arguments, so the
	/// caller can render it in the reader's locale. The socket <c>SOCKSET</c> answers a descriptor and
	/// <c>@sockset</c> answers an object; both go through <c>NotifyLocalized</c>.
	/// </summary>
	public readonly record struct SocksetResult(string Key, object[] Arguments)
	{
		public static SocksetResult Of(string key) => new(key, []);
		public static SocksetResult Of(string key, params object[] arguments) => new(key, arguments);
	}

	/// <summary>
	/// Sets one option and reports the message PennMUSH would echo. Option names are matched
	/// case-insensitively; an unknown one is reported rather than silently ignored.
	/// </summary>
	public static SocksetResult Set(IConnectionService.ConnectionData connection, string name, string value)
	{
		name = name.Trim();

		if (name.Length == 0)
		{
			return SocksetResult.Of(nameof(ErrorMessages.Notifications.SocksetSetWhatOption));
		}

		switch (name.ToUpperInvariant())
		{
			case "OUTPUTPREFIX":
				return SetOrClear("OutputPrefix", value,
					nameof(ErrorMessages.Notifications.OutputPrefixSet),
					nameof(ErrorMessages.Notifications.OutputPrefixCleared));

			case "OUTPUTSUFFIX":
				return SetOrClear("OutputSuffix", value,
					nameof(ErrorMessages.Notifications.OutputSuffixSet),
					nameof(ErrorMessages.Notifications.OutputSuffixCleared));

			case "WIDTH":
				return SetDimension("WIDTH", value,
					nameof(ErrorMessages.Notifications.SocksetWidthSet),
					nameof(ErrorMessages.Notifications.SocksetWidthNeedsPositiveInteger));

			case "HEIGHT":
				return SetDimension("HEIGHT", value,
					nameof(ErrorMessages.Notifications.SocksetHeightSet),
					nameof(ErrorMessages.Notifications.SocksetHeightNeedsPositiveInteger));

			case "TERMINALTYPE":
				connection.Metadata["TerminalType"] = value;
				return SocksetResult.Of(nameof(ErrorMessages.Notifications.SocksetTerminalTypeSet));

			case "PROMPT_NEWLINES":
				connection.Metadata[PromptNewlinesKey] = IsYes(value) ? "1" : "0";
				return SocksetResult.Of(IsYes(value)
					? nameof(ErrorMessages.Notifications.SocksetPromptNewlinesOn)
					: nameof(ErrorMessages.Notifications.SocksetPromptNewlinesOff));

			case "STRIPACCENTS":
			case "NOACCENTS":
				connection.Metadata[StripAccentsKey] = IsYes(value) ? "1" : "0";
				return SocksetResult.Of(IsYes(value)
					? nameof(ErrorMessages.Notifications.SocksetStripAccentsOn)
					: nameof(ErrorMessages.Notifications.SocksetStripAccentsOff));

			case "COLORSTYLE":
			case "COLOURSTYLE":
				return SetColorStyle(value);

			default:
				return SocksetResult.Of(nameof(ErrorMessages.Notifications.SocksetInvalidOptionFormat), name);
		}

		SocksetResult SetOrClear(string key, string newValue, string setKey, string clearedKey)
		{
			// PennMUSH routes both the socket command and this option through the same set_userstring:
			// leading whitespace is skipped and a value that is empty afterwards clears the setting.
			// Without the TrimStart, "SOCKSET OUTPUTPREFIX=   " would store spaces and Show() would
			// report a prefix as set, while the bare OUTPUTPREFIX command cleared it.
			newValue = newValue.TrimStart();

			if (string.IsNullOrEmpty(newValue))
			{
				connection.Metadata.TryRemove(key, out _);
				return SocksetResult.Of(clearedKey);
			}

			connection.Metadata[key] = newValue;
			return SocksetResult.Of(setKey);
		}

		SocksetResult SetDimension(string key, string newValue, string setKey, string errorKey)
		{
			if (!int.TryParse(newValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
					|| parsed < 1)
			{
				return SocksetResult.Of(errorKey);
			}

			connection.Metadata[key] = parsed.ToString(CultureInfo.InvariantCulture);
			return SocksetResult.Of(setKey);
		}

		SocksetResult SetColorStyle(string newValue)
		{
			// "auto" clears the pin and lets the negotiated capabilities decide again, exactly as
			// PennMUSH clearing CONN_COLORSTYLE does.
			var style = newValue.Trim().ToLowerInvariant() switch
			{
				"auto" => "auto",
				"plain" or "none" => ColorStyles.Plain,
				"hilite" or "highlight" => ColorStyles.Hilite,
				"16color" => ColorStyles.SixteenColor,
				"xterm256" or "256" => ColorStyles.Xterm256,
				// Not a PennMUSH style: PennMUSH predates clients that render ESC[38;2;r;g;b, but
				// SharpMUSH emits those for hex ansi() codes, so a player has to be able to ask for
				// them — or refuse them — the same way they can for the 256 palette.
				"truecolor" or "truecolour" or "rgb" or "24bit" => ColorStyles.Truecolor,
				_ => null
			};

			if (style is null)
			{
				return SocksetResult.Of(nameof(ErrorMessages.Notifications.SocksetUnknownColorStyle));
			}

			if (style == "auto")
			{
				connection.Metadata.TryRemove(ColorStyleKey, out _);
			}
			else
			{
				connection.Metadata[ColorStyleKey] = style;
			}

			return SocksetResult.Of(nameof(ErrorMessages.Notifications.SocksetColorStyleSetFormat), style);
		}
	}

	/// <summary>PennMUSH <c>isyes()</c>: a leading y/t or a non-zero number.</summary>
	private static bool IsYes(string value)
	{
		value = value.Trim();

		if (value.Length == 0) return false;
		if (value[0] is 'y' or 'Y' or 't' or 'T') return true;

		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed != 0;
	}
}
