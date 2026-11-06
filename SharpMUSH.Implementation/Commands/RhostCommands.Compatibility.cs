using System.Globalization;
using System.Net;
using System.Net.Sockets;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// RhostMUSH's <c>@rollback</c>, <c>@blacklist</c> and <c>@site</c>, and TinyMUX's <c>@folder</c>. <c>@site</c>
/// and <c>@folder</c> are reached only under their dialect, by the names those servers give them: as
/// global commands they would make <c>@sit</c> and <c>@fo</c> ambiguous where PennMUSH reads them as
/// <c>@sitelock</c> and <c>@force</c>.
/// </summary>
public partial class Commands
{
	/// <summary>How many times one <c>@rollback</c> runs its commands again at most.</summary>
	private const int RollbackLimit = 1000;

	/// <summary>
	/// RhostMUSH's <c>@rollback</c>: the commands before it in the same action list run again, each time
	/// with new <c>%0</c>-<c>%9</c> when arguments are given, which are evaluated again before every pass.
	/// <c>@rollback &lt;count&gt;[/&lt;repeats&gt;]</c> runs the last <c>&lt;count&gt;</c> commands
	/// <c>&lt;repeats&gt;</c> times; <c>/retry &lt;boolean&gt;</c> runs all of them for as long as the boolean
	/// is true. Afterwards the list goes on with its own <c>%0</c>-<c>%9</c>.
	/// </summary>
	[SharpCommand(Name = "@ROLLBACK", Output = CommandOutput.Runs, Switches = ["RETRY", "WAIT", "LABEL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0,
		ParameterNames = ["count", "arguments..."])]
	public async ValueTask<Option<CallState>> Rollback(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToArray();
		var args = parser.CurrentState.ArgumentsOrdered;

		if (switches.Contains("LABEL"))
		{
			return await RollbackRefusal(executor, "@ROLLBACK: SharpMUSH has no @goto labels, so /label has nowhere to go back to.");
		}

		if (switches.Contains("WAIT"))
		{
			return await RollbackRefusal(executor,
				"@ROLLBACK: /wait is not supported. Put the commands in an attribute and @wait a @trigger of it.");
		}

		if (!args.TryGetValue("0", out var first) || first.Message.ToPlainText().Trim().Length == 0)
		{
			return await RollbackRefusal(executor,
				"@ROLLBACK: Give a count, as in @rollback <count>[/<repeats>], or a condition with /retry.");
		}

		// The command list's commands so far, latest first; the first is this @rollback itself.
		var earlier = (parser.CurrentState.CommandSources?.ToArray() ?? []).Skip(1).Reverse().ToArray();
		var argumentTexts = args
			.Where(pair => pair.Key != "0")
			.OrderBy(pair => int.Parse(pair.Key, CultureInfo.InvariantCulture))
			.Select(pair => pair.Value.Message)
			.ToArray();

		var state = new RollbackState(parser, parser.CurrentState.EnvironmentRegisters, argumentTexts);
		var passes = switches.Contains("RETRY")
			? await RetryRollbackAsync(state, first.Message, earlier)
			: await CountedRollbackAsync(state, executor, first.Message, earlier);

		return passes is { } ran ? new CallState(ran) { HadErrors = state.HadErrors } : CallState.Empty;
	}

	private async ValueTask<Option<CallState>> RollbackRefusal(AnySharpObject executor, string message)
	{
		await NotifyService.Notify(executor, message, executor);
		return new CallState(ErrorMessages.Returns.InvalidArguments);
	}

	/// <summary>One <c>@rollback</c>'s <c>%0</c>-<c>%9</c>, as they change from pass to pass.</summary>
	private sealed class RollbackState(IMUSHCodeParser parser, Dictionary<string, CallState> environment, MString?[] argumentTexts)
	{
		public Dictionary<string, CallState> Environment { get; private set; } = environment;

		public bool HadErrors { get; private set; }

		/// <summary>Evaluates <paramref name="text"/> with the current <c>%0</c>-<c>%9</c>.</summary>
		public async ValueTask<MString> EvaluateAsync(MString text)
		{
			var environment = Environment;
			var result = await parser.With(state => state with { EnvironmentRegisters = environment },
				inner => inner.FunctionParse(text));
			HadErrors |= result?.HadErrors == true;
			return result?.Message ?? MarkupText.Empty;
		}

		/// <summary>Whether <paramref name="condition"/> is true with the current <c>%0</c>-<c>%9</c>.</summary>
		public async ValueTask<bool> HoldsAsync(MString condition) => (await EvaluateAsync(condition)).Truthy(parser);

		/// <summary>The arguments, evaluated with the current <c>%0</c>-<c>%9</c>, become the new ones.</summary>
		public async ValueTask NextArgumentsAsync()
		{
			if (argumentTexts.Length == 0) return;

			var next = new Dictionary<string, CallState>();
			for (var i = 0; i < argumentTexts.Length; i++)
			{
				next[i.ToString(CultureInfo.InvariantCulture)] = new CallState(await EvaluateAsync(argumentTexts[i] ?? MarkupText.Empty));
			}

			Environment = next;
		}

		/// <summary>Runs <paramref name="commands"/> as an action list with the current <c>%0</c>-<c>%9</c>.</summary>
		public async ValueTask RunAsync(MString commands)
		{
			var environment = Environment;
			var result = await parser.With(state => state with { EnvironmentRegisters = environment },
				inner => inner.CommandListParse(commands));
			HadErrors |= result?.HadErrors == true;
		}
	}

	/// <summary><c>/retry</c>: every earlier command again while the condition holds.</summary>
	private static async ValueTask<int?> RetryRollbackAsync(RollbackState state, MString condition, MString[] earlier)
	{
		if (earlier.Length == 0) return 0;

		var commands = MarkupText.Join(MarkupText.Plain(";"), earlier);
		var passes = 0;
		while (passes < RollbackLimit && await state.HoldsAsync(condition))
		{
			await state.NextArgumentsAsync();
			await state.RunAsync(commands);
			passes++;
		}

		return passes;
	}

	/// <summary><c>&lt;count&gt;[/&lt;repeats&gt;]</c>: the last <c>&lt;count&gt;</c> commands, repeated.</summary>
	private async ValueTask<int?> CountedRollbackAsync(RollbackState state, AnySharpObject executor, MString spec,
		MString[] earlier)
	{
		var parts = (await state.EvaluateAsync(spec)).ToPlainText().Trim().Split('/', 2);
		if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
		{
			await NotifyService.Notify(executor, "@ROLLBACK: The count must be a number of commands, zero or more.", executor);
			return null;
		}

		var repeats = 1;
		if (parts.Length > 1
				&& (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out repeats) || repeats < 1))
		{
			await NotifyService.Notify(executor, "@ROLLBACK: The number of repeats must be one or more.", executor);
			return null;
		}

		// A count of zero, or nothing before it, leaves the list to go on.
		var segment = earlier.TakeLast(count).ToArray();
		if (segment.Length == 0) return 0;

		var commands = MarkupText.Join(MarkupText.Plain(";"), segment);
		repeats = Math.Min(repeats, RollbackLimit);
		for (var pass = 0; pass < repeats; pass++)
		{
			await state.NextArgumentsAsync();
			await state.RunAsync(commands);
		}

		return repeats;
	}

	/// <summary>The site lists <c>@blacklist</c> keeps, by the switch that names each.</summary>
	private static readonly (string Switch, string Marker, string Name, string[] Options)[] BlacklistKinds =
	[
		("", "blacklist", "Blacklist", [SitelockMatcher.ConnectFlag]),
		("NOGUEST", "blacklist_noguest", "NoGuest blacklist", [SitelockMatcher.GuestFlag]),
		("REGISTER", "blacklist_register", "Register blacklist", [SitelockMatcher.CreateFlag, "register"])
	];

	/// <summary>
	/// RhostMUSH's <c>@blacklist</c>: lists of single addresses or networks refused at the door, kept apart
	/// from the hand-written rules. Here each entry is a sitelock rule for the network, marked with the list
	/// it belongs to, so the rules that refuse connections stay in one place and <c>@sitelock</c> shows them.
	/// </summary>
	[SharpCommand(Name = "@BLACKLIST",
		Switches = ["ADD", "DEL", "LIST", "MASK", "SEARCH", "CLEAR", "LOAD", "NODNS", "NOGUEST", "REGISTER"],
		Behavior = CB.Default | CB.NoGagged, CommandLock = "PERM^players.moderate", MinArgs = 0, MaxArgs = 1,
		ParameterNames = ["address"])]
	public async ValueTask<Option<CallState>> Blacklist(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var argument = parser.CurrentState.Arguments.TryGetValue("0", out var arg0) ? arg0.Message.ToPlainText().Trim() : string.Empty;

		if (switches.Contains("NODNS"))
		{
			return await BlacklistNotice(executor, "BLACKLIST: SharpMUSH does not look up host names, so there is no NoDNS list.");
		}

		var kind = BlacklistKinds.First(candidate =>
			candidate.Switch.Length == 0 ? !switches.Contains("NOGUEST") && !switches.Contains("REGISTER") : switches.Contains(candidate.Switch));
		var actions = switches.Except(["NOGUEST", "REGISTER"], StringComparer.OrdinalIgnoreCase).ToArray();

		return actions switch
		{
			[] => await BlacklistSummary(executor),
			["LIST"] => await BlacklistShow(executor, kind, masks: false, pattern: null),
			["MASK"] => await BlacklistShow(executor, kind, masks: true, pattern: null),
			["SEARCH"] => await BlacklistShow(executor, kind, masks: false, pattern: argument.Length == 0 ? "*" : argument),
			["ADD"] => await BlacklistAdd(executor, kind, argument),
			["DEL"] => await BlacklistDelete(executor, kind, argument),
			["CLEAR"] => await BlacklistClear(executor, kind),
			["LOAD"] => await BlacklistNotice(executor,
				"BLACKLIST: SharpMUSH keeps the blacklist with its sitelock rules, not in a file. Add entries with @blacklist/add."),
			_ => await BlacklistNotice(executor, "BLACKLIST: Use one of /add, /del, /list, /mask, /search or /clear.")
		};
	}

	private async ValueTask<CallState> BlacklistNotice(AnySharpObject executor, string message)
	{
		await NotifyService.Notify(executor, message, executor);
		return CallState.Empty;
	}

	private IEnumerable<KeyValuePair<string, string[]>> BlacklistEntries(string marker)
		=> Configuration.CurrentValue.SitelockRules.Rules
			.Where(rule => rule.Value.Contains(marker, StringComparer.OrdinalIgnoreCase));

	private async ValueTask<CallState> BlacklistSummary(AnySharpObject executor)
	{
		var counts = BlacklistKinds.Select(kind => $"{kind.Name}: {BlacklistEntries(kind.Marker).Count()}");
		return await BlacklistNotice(executor, $"BLACKLIST: {string.Join(", ", counts)}.");
	}

	private async ValueTask<CallState> BlacklistShow(AnySharpObject executor,
		(string Switch, string Marker, string Name, string[] Options) kind, bool masks, string? pattern)
	{
		var entries = BlacklistEntries(kind.Marker)
			.Select(rule => rule.Key)
			.Where(key => pattern is null || MushText.IsWildcardMatch(MarkupText.Plain(key.Split('/')[0]), pattern))
			.Order(StringComparer.Ordinal)
			.Select(key => masks ? $"{key.Split('/')[0]} {NetmaskText(key)}" : key.Split('/')[0])
			.ToArray();

		if (entries.Length == 0)
		{
			return await BlacklistNotice(executor, $"BLACKLIST: The {kind.Name} has no entries{(pattern is null ? "" : " that match")}.");
		}

		var perLine = masks ? 2 : 4;
		var lines = entries.Chunk(perLine).Select(line => string.Join("  ", line));
		return await BlacklistNotice(executor, $"{kind.Name} ({entries.Length}):\n{string.Join('\n', lines)}");
	}

	private async ValueTask<CallState> BlacklistAdd(AnySharpObject executor,
		(string Switch, string Marker, string Name, string[] Options) kind, string argument)
	{
		if (SiteNetwork(argument) is not { } network)
		{
			return await BlacklistNotice(executor, "BLACKLIST: Give an address and an optional mask, as in @blacklist/add 192.0.2.7 255.255.255.255.");
		}

		var outcome = "added to";
		await ChangeSitelockRulesAsync(executor, rules =>
		{
			var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (rules.TryGetValue(network, out var existing))
			{
				markers.UnionWith(existing.Where(IsBlacklistMarker));
				if (markers.Count == 0)
				{
					outcome = "sitelock";
					return;
				}
			}

			if (!markers.Add(kind.Marker))
			{
				outcome = "already in";
				return;
			}

			rules[network] = BlacklistOptions(markers);
		});

		return await BlacklistNotice(executor, outcome == "sitelock"
			? $"BLACKLIST: {network} already has a sitelock rule. Change it with @sitelock."
			: $"BLACKLIST: {network} {outcome} the {kind.Name}.");
	}

	private async ValueTask<CallState> BlacklistDelete(AnySharpObject executor,
		(string Switch, string Marker, string Name, string[] Options) kind, string argument)
	{
		if (SiteNetwork(argument) is not { } network)
		{
			return await BlacklistNotice(executor, "BLACKLIST: Give an address and an optional mask, as in @blacklist/del 192.0.2.7 255.255.255.255.");
		}

		var removed = await ChangeSitelockRulesAsync(executor, rules => WithoutBlacklistMarker(rules, network, kind.Marker));
		return await BlacklistNotice(executor, removed == 0
			? $"BLACKLIST: {network} is not in the {kind.Name}."
			: $"BLACKLIST: {network} removed from the {kind.Name}.");
	}

	private async ValueTask<CallState> BlacklistClear(AnySharpObject executor,
		(string Switch, string Marker, string Name, string[] Options) kind)
	{
		var removed = await ChangeSitelockRulesAsync(executor, rules =>
		{
			foreach (var network in rules.Where(rule => rule.Value.Contains(kind.Marker, StringComparer.OrdinalIgnoreCase))
								 .Select(rule => rule.Key).ToArray())
			{
				WithoutBlacklistMarker(rules, network, kind.Marker);
			}
		});

		return await BlacklistNotice(executor, $"BLACKLIST: The {kind.Name} is cleared ({removed} removed).");
	}

	/// <summary>
	/// The rule for <paramref name="network"/> without one list's marker: the other lists' options stay, or
	/// the rule goes. Nothing changes when the rule is not on that list.
	/// </summary>
	private static void WithoutBlacklistMarker(Dictionary<string, string[]> rules, string network, string marker)
	{
		if (!rules.TryGetValue(network, out var options) || !options.Contains(marker, StringComparer.OrdinalIgnoreCase)) return;

		var markers = options.Where(IsBlacklistMarker)
			.Where(candidate => !candidate.Equals(marker, StringComparison.OrdinalIgnoreCase))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		if (markers.Count == 0) rules.Remove(network);
		else rules[network] = BlacklistOptions(markers);
	}

	/// <summary>
	/// Changes the sitelock rules in one write, starting from the rules as they stand then, and records and
	/// enforces what changed as <c>@sitelock</c> does. Returns how many rules changed.
	/// </summary>
	private async ValueTask<int> ChangeSitelockRulesAsync(AnySharpObject executor, Action<Dictionary<string, string[]>> change)
	{
		var set = new List<KeyValuePair<string, string[]>>();
		var removed = new List<string>();
		await ConfigWriter.UpdateAsync(current =>
		{
			set.Clear();
			removed.Clear();
			var before = current.SitelockRules.Rules;
			var rules = new Dictionary<string, string[]>(before);
			change(rules);

			removed.AddRange(before.Keys.Where(key => !rules.ContainsKey(key)));
			set.AddRange(rules.Where(rule => !before.TryGetValue(rule.Key, out var old) || !old.SequenceEqual(rule.Value)));
			return set.Count == 0 && removed.Count == 0
				? current
				: current with { SitelockRules = new SitelockRulesOptions(rules) };
		});

		foreach (var pattern in removed)
		{
			await Audit.RecordAsync(executor, AuditActions.SitelockRemove, AuditTargets.Of(AuditTargetKinds.Host, pattern));
		}

		foreach (var (pattern, options) in set)
		{
			await Audit.RecordAsync(executor, AuditActions.SitelockAdd, AuditTargets.Of(AuditTargetKinds.Host, pattern),
				string.Join(" ", options));
			await BanEnforcer.EnforceHostRuleAsync(pattern);
		}

		return set.Count + removed.Count;
	}

	private static bool IsBlacklistMarker(string option)
		=> BlacklistKinds.Any(kind => kind.Marker.Equals(option, StringComparison.OrdinalIgnoreCase));

	/// <summary>The rule's options for the lists in <paramref name="markers"/>: each marker and what it refuses.</summary>
	private static string[] BlacklistOptions(IReadOnlySet<string> markers)
		=> [.. BlacklistKinds
			.Where(kind => markers.Contains(kind.Marker))
			.SelectMany(kind => kind.Options.Prepend(kind.Marker))
			.Distinct(StringComparer.OrdinalIgnoreCase)];

	/// <summary>
	/// <c>&lt;address&gt;[ &lt;mask&gt;]</c> or <c>&lt;address&gt;=&lt;mask&gt;</c> as the network it names, written
	/// <c>&lt;network&gt;/&lt;bits&gt;</c>, or null when it names none. The mask is dotted (IPv4), a prefix length
	/// (<c>/24</c>), or absent for the one address.
	/// </summary>
	private static string? SiteNetwork(string address, string? mask = null)
	{
		var parts = address.Split([' ', '='], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0) return null;
		mask ??= parts.Length > 1 ? parts[1] : null;

		var host = parts[0];
		if (mask is null && host.IndexOf('/') is var slash and > 0)
		{
			mask = host[slash..];
			host = host[..slash];
		}

		if (!IPAddress.TryParse(host, out var ip)) return null;
		var width = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

		int bits;
		if (string.IsNullOrWhiteSpace(mask)) bits = width;
		else if (mask.Trim().TrimStart('/') is var prefix && int.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
		{
			if (length > width) return null;
			bits = length;
		}
		else if (IPAddress.TryParse(mask.Trim(), out var dotted) && dotted.AddressFamily == ip.AddressFamily
						 && MaskBits(dotted.GetAddressBytes()) is { } counted)
		{
			bits = counted;
		}
		else return null;

		var bytes = ip.GetAddressBytes();
		for (var i = 0; i < bytes.Length; i++)
		{
			var keep = Math.Clamp(bits - i * 8, 0, 8);
			bytes[i] &= (byte)(0xFF << (8 - keep));
		}

		return $"{new IPAddress(bytes)}/{bits}";
	}

	/// <summary>The prefix length of a contiguous mask, or null when its ones do not come first.</summary>
	private static int? MaskBits(byte[] mask)
	{
		var bits = 0;
		var ended = false;
		foreach (var octet in mask)
		{
			for (var bit = 7; bit >= 0; bit--)
			{
				var set = (octet >> bit & 1) == 1;
				if (set && ended) return null;
				if (set) bits++;
				else ended = true;
			}
		}

		return bits;
	}

	/// <summary>The mask of <c>&lt;network&gt;/&lt;bits&gt;</c>, dotted for IPv4 and as <c>/bits</c> otherwise.</summary>
	private static string NetmaskText(string network)
	{
		var slash = network.IndexOf('/');
		if (slash < 0 || !int.TryParse(network[(slash + 1)..], CultureInfo.InvariantCulture, out var bits)) return "255.255.255.255";
		if (network.Contains(':')) return $"/{bits}";

		var mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits);
		return string.Join('.', new[] { mask >> 24, mask >> 16 & 0xFF, mask >> 8 & 0xFF, mask & 0xFF });
	}

	/// <summary>
	/// RhostMUSH's site lists by <c>@site</c>'s switch, as the sitelock options SharpMUSH keeps for them.
	/// Lists with nothing here (permit, trust, passproxy and the rest) are ones SharpMUSH does not keep.
	/// </summary>
	private static readonly Dictionary<string, string[]> SiteOptions = new(StringComparer.OrdinalIgnoreCase)
	{
		["FORBID"] = [SitelockMatcher.ConnectFlag],
		["REGISTER"] = [SitelockMatcher.CreateFlag, "register"],
		["NOGUEST"] = [SitelockMatcher.GuestFlag],
		["SUSPECT"] = [SitelockMatcher.SuspectOption],
		["NOAUTOREG"] = [],
		["FORBIDAPI"] = [],
		["PERMIT"] = [],
		["TRUST"] = [],
		["NOAUTH"] = [],
		["NOHOST"] = [],
		["PASSPROX"] = [],
		["PASSAPI"] = [],
		["HARDCONN"] = []
	};

	/// <summary>
	/// RhostMUSH's <c>@site/&lt;list&gt; &lt;address&gt;=&lt;mask&gt;</c>, reached as <c>@site</c> under the RhostMUSH
	/// dialect: takes the address out of that site list. Here that removes the list's options from the
	/// sitelock rule for the address, and the rule itself once nothing is left; <c>/all</c> removes the rule.
	/// <c>/list</c> shows the rules.
	/// </summary>
	[SharpCommand(Name = "@RHOST_SITE", Switches = ["ALL", "LIST", "FORBID", "REGISTER", "NOGUEST", "SUSPECT", "NOAUTOREG",
			"FORBIDAPI", "PERMIT", "TRUST", "NOAUTH", "NOHOST", "PASSPROX", "PASSAPI", "HARDCONN"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, CommandLock = "PERM^players.moderate", MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["address", "mask"])]
	public async ValueTask<Option<CallState>> RhostSite(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToArray();
		var args = parser.CurrentState.Arguments;

		if (switches is ["LIST"])
		{
			var rules = Configuration.CurrentValue.SitelockRules.Rules;
			return await BlacklistNotice(executor, rules.Count == 0
				? "SITE: There are no site rules."
				: string.Join('\n', rules.OrderBy(rule => rule.Key, StringComparer.Ordinal)
					.Select(rule => $"{rule.Key}: {string.Join(' ', rule.Value)}")));
		}

		if (switches.Length != 1)
		{
			return await BlacklistNotice(executor, "SITE: Name one list to remove from, such as /forbid or /all.");
		}

		var address = args.TryGetValue("0", out var arg0) ? arg0.Message.ToPlainText().Trim() : string.Empty;
		var mask = args.TryGetValue("1", out var arg1) ? arg1.Message.ToPlainText().Trim() : null;
		if (SiteNetwork(address, mask) is not { } network)
		{
			return await BlacklistNotice(executor, "SITE: Give an address and its mask, as in @site/forbid 192.0.2.0=255.255.255.0.");
		}

		// A single address may have been written bare, the way @sitelock takes it.
		var removing = switches[0] == "ALL" ? null : SiteOptions[switches[0]];
		if (removing is { Length: 0 })
		{
			return await BlacklistNotice(executor, $"SITE: SharpMUSH keeps no {switches[0].ToLowerInvariant()} site list, so there is nothing to remove.");
		}

		// A single address may have been written bare, the way @sitelock takes it.
		var single = network.EndsWith("/32", StringComparison.Ordinal) || network.EndsWith("/128", StringComparison.Ordinal)
			? network[..network.LastIndexOf('/')]
			: null;

		var changed = await ChangeSitelockRulesAsync(executor, rules =>
		{
			foreach (var (pattern, options) in rules
								 .Where(rule => rule.Key.Equals(network, StringComparison.OrdinalIgnoreCase)
									 || single is not null && rule.Key.Equals(single, StringComparison.OrdinalIgnoreCase))
								 .ToArray())
			{
				var left = removing is null
					? []
					: options.Where(option => !removing.Contains(option, StringComparer.OrdinalIgnoreCase)).ToArray();
				if (left.Length == options.Length) continue;

				if (left.All(IsBlacklistMarker)) rules.Remove(pattern);
				else rules[pattern] = left;
			}
		});

		return await BlacklistNotice(executor, changed == 0
			? $"SITE: Nothing for {network} to remove."
			: $"SITE: Removed {(removing is null ? "every rule" : switches[0].ToLowerInvariant())} for {network}.");
	}

	/// <summary>
	/// TinyMUX's <c>@folder</c>, reached as <c>@folder</c> under the TinyMUX dialect: <c>@mail</c>'s folder
	/// work under one name. Without a switch it lists the folders, makes one current, or reads
	/// <c>&lt;folder&gt;=&lt;messages&gt;</c>; <c>/read</c> and <c>/list</c> work in the folder named,
	/// <c>/file &lt;messages&gt;=&lt;folder&gt;</c> files messages, and <c>/set &lt;folder&gt;[=&lt;name&gt;]</c> makes
	/// it current or names it.
	/// </summary>
	[SharpCommand(Name = "@MUX_FOLDER", Switches = ["FILE", "LIST", "READ", "SET"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["folder", "messages"])]
	public async ValueTask<Option<CallState>> MuxFolder(IMUSHCodeParser parser, SharpCommandAttribute attribute)
	{
		var args = parser.CurrentState.Arguments;
		var first = args.TryGetValue("0", out var arg0) ? arg0.Message : MarkupText.Empty;
		var second = args.TryGetValue("1", out var arg1) ? arg1.Message : null;
		var hasFirst = first.ToPlainText().Trim().Length > 0;
		var hasSecond = second is not null && second.ToPlainText().Trim().Length > 0;

		MString InFolder(MString messages) => MarkupText.Concat(MarkupText.Concat(first, MarkupText.Plain(":")), messages);

		var (mailSwitch, mailArguments) = parser.CurrentState.Switches.ToArray() switch
		{
			["FILE"] => ("FILE", hasSecond ? new[] { first, second! } : [first]),
			["LIST"] => ("LIST", hasFirst ? [InFolder(hasSecond ? second! : MarkupText.Plain("all"))] : []),
			["READ"] => ("READ", new[] { hasSecond ? InFolder(second!) : first }),
			["SET"] => ("FOLDER", hasSecond ? new[] { first, second! } : hasFirst ? [first] : []),
			[] when !hasFirst => ("FOLDER", Array.Empty<MString>()),
			[] when hasSecond => ("READ", [InFolder(second!)]),
			[] => ("FOLDER", [first]),
			_ => (null, Array.Empty<MString>())
		};

		if (mailSwitch is null)
		{
			return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		// The arguments are evaluated already, and a state built from text evaluates to that text, so @mail
		// reads them as they are.
		var mailParser = parser.Push(parser.CurrentState with
		{
			Command = "@MAIL",
			Switches = [mailSwitch],
			Arguments = mailArguments
				.Select((argument, index) => (Key: index.ToString(CultureInfo.InvariantCulture), Value: new CallState(argument)))
				.ToDictionary(pair => pair.Key, pair => pair.Value)
		});

		return await Mail(mailParser, attribute);
	}
}
