using OneOf.Types;
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
using System.Globalization;
using System.Text;

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
	private static IConnectionService.ConnectionData? CurrentConnection(IMUSHCodeParser parser)
		=> parser.CurrentState.Handle is { } handle ? ConnectionService!.Get(handle) : null;

	/// <summary>
	/// The single unparsed argument of a <c>SOCKET | NoParse</c> command: everything after the
	/// command word. PennMUSH reads these with <c>strncmp</c> and then takes the remainder verbatim,
	/// so no evaluation, splitting or trimming happens on the way in.
	/// </summary>
	private static string SocketArgument(IMUSHCodeParser parser)
		=> parser.CurrentState.Arguments.TryGetValue("0", out var arg)
			? arg.Message?.ToPlainText() ?? string.Empty
			: string.Empty;

	/// <summary>
	/// PennMUSH <c>show_tm()</c> (src/strutil.c): <c>asctime()</c> without its trailing newline, with
	/// the day-of-month zero-padded rather than space-padded.
	/// </summary>
	private static string ShowTime(DateTimeOffset when)
		=> when.ToLocalTime().ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);

	/// <summary>
	/// PennMUSH <c>count_players()</c> (src/bsd.c): connected descriptors that have a player behind
	/// them, skipping hidden (DARK) ones unless <c>count_all</c> is set.
	/// </summary>
	private static async ValueTask<int> CountPlayers()
	{
		var countAll = Configuration!.CurrentValue.Cosmetic.CountAll;
		var count = 0;

		await foreach (var connection in ConnectionService!.GetAll())
		{
			if (connection.Ref is not { } reference) continue;

			// GoodObject first, and unconditionally: a handle can outlive the object it is bound to, and
			// such a descriptor is not a connected player under any counting rule.
			var found = await Mediator!.Send(new GetObjectNodeQuery(reference));
			if (found.IsNone) continue;

			if (!countAll && await found.Known.IsDark()) continue;

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
	public static async ValueTask<Option<CallState>> Info(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var net = Configuration!.CurrentValue.Net;
		var uptime = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();
		var size = await Mediator!.CreateStream(new GetAllObjectsQuery()).CountAsync();

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

		await NotifyService!.Notify(parser.CurrentState.Handle!.Value, string.Join("\n", lines));

		return new None();
	}

	/// <summary>
	/// <c>MSSP-REQUEST</c> — PennMUSH <c>report_mssp()</c> (src/bsd.c) in its descriptor form: the
	/// same values the MSSP telnet option carries, as plain tab-separated text for crawlers that
	/// never negotiate telnet.
	/// </summary>
	[SharpCommand(Name = "MSSP-REQUEST", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> MsspRequest(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var net = Configuration!.CurrentValue.Net;
		var uptime = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();

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

		await NotifyService!.Notify(parser.CurrentState.Handle!.Value, string.Join("\n", lines));

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
	public static async ValueTask<Option<CallState>> SocketVersion(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var net = Configuration!.CurrentValue.Net;
		var uptime = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();

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

		await NotifyService!.Notify(parser.CurrentState.Handle!.Value, string.Join("\n", lines));

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
	public static async ValueTask<Option<CallState>> Idle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var echo = SocketArgument(parser);

		if (!string.IsNullOrEmpty(echo))
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, echo);
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
	public static ValueTask<Option<CallState>> ScreenWidth(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetScreenDimension(parser, "WIDTH");

	/// <summary>
	/// <c>SCREENHEIGHT &lt;rows&gt;</c> — the <c>d-&gt;height</c> counterpart of <see cref="ScreenWidth"/>.
	/// </summary>
	[SharpCommand(Name = "SCREENHEIGHT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["rows"])]
	public static ValueTask<Option<CallState>> ScreenHeight(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetScreenDimension(parser, "HEIGHT");

	private static ValueTask<Option<CallState>> SetScreenDimension(IMUSHCodeParser parser, string key)
	{
		// PennMUSH parse_integer() on a non-numeric argument yields 0, and the descriptor is set to it
		// silently. Storing the parsed value rather than the raw text keeps the metadata key in the
		// shape NAWS writes it, so readers never have to cope with two encodings of the same fact.
		var value = int.TryParse(SocketArgument(parser).Trim(), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: 0;

		ConnectionService!.Update(parser.CurrentState.Handle!.Value, key, value.ToString(CultureInfo.InvariantCulture));

		return ValueTask.FromResult<Option<CallState>>(new None());
	}

	/// <summary>
	/// <c>PROMPT_NEWLINES &lt;0|1&gt;</c> — whether a newline follows a prompt on this descriptor
	/// (PennMUSH <c>CONN_PROMPT_NEWLINES</c>). Silent, like the SCREEN* pair.
	/// </summary>
	[SharpCommand(Name = "PROMPT_NEWLINES", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["enabled"])]
	public static ValueTask<Option<CallState>> PromptNewlines(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enabled = int.TryParse(SocketArgument(parser).Trim(), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var parsed) && parsed != 0;

		ConnectionService!.Update(parser.CurrentState.Handle!.Value,
			SocketOptions.PromptNewlinesKey, enabled ? "1" : "0");

		return ValueTask.FromResult<Option<CallState>>(new None());
	}

	/// <summary>
	/// <c>SOCKSET [&lt;option&gt;=&lt;value&gt;]</c> — PennMUSH <c>sockset_wrapper()</c> (src/bsd.c).
	/// With no argument it reports the descriptor's settings; with <c>option=value</c> it sets one and
	/// echoes the result; with an argument but no <c>=</c> it complains. The wizard-only
	/// <c>@sockset</c> reaches the same engine against another descriptor.
	/// </summary>
	[SharpCommand(Name = "SOCKSET", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["option"])]
	public static async ValueTask<Option<CallState>> Sockset(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var handle = parser.CurrentState.Handle!.Value;
		var connection = CurrentConnection(parser);

		if (connection is null)
		{
			await NotifyService!.NotifyLocalized(handle, nameof(ErrorMessages.Notifications.SocksetNotConnected));
			return new None();
		}

		var argument = SocketArgument(parser).TrimStart();

		if (argument.Length == 0)
		{
			await NotifyService!.Notify(handle, SocketOptions.Show(connection, "\n"));
			return new None();
		}

		var separator = argument.IndexOf('=');
		if (separator < 0)
		{
			await NotifyService!.NotifyLocalized(handle, nameof(ErrorMessages.Notifications.SocksetNeedsOptionAndValue));
			return new None();
		}

		var result = SocketOptions.Set(connection, argument[..separator], argument[(separator + 1)..]);
		await NotifyService!.NotifyLocalized(handle, result.Key, result.Arguments);

		return new None();
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
	public static string Show(IConnectionService.ConnectionData connection, string newLine)
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
		Row("Telnet", YesNo(connection.ConnectionType == "telnet"));
		Row("Width", connection.Metadata.GetValueOrDefault("WIDTH", "78"));
		Row("Height", connection.Metadata.GetValueOrDefault("HEIGHT", "24"));
		Row("Terminal Type", connection.Metadata.GetValueOrDefault("TerminalType", "unknown"));
		Row("Stripaccents", YesNo(connection.Metadata.GetValueOrDefault(StripAccentsKey) == "1"));

		// PennMUSH reports "auto (<derived>)" until the style has been pinned explicitly, so the
		// player can tell a negotiated default apart from a choice they made.
		var colorStyle = connection.Metadata.GetValueOrDefault(ColorStyleKey);
		Row("Color Style", colorStyle is null ? "auto (xterm256)" : colorStyle);

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
				"plain" or "none" => "plain",
				"hilite" or "highlight" => "hilite",
				"16color" => "16color",
				"xterm256" or "256" => "xterm256",
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
