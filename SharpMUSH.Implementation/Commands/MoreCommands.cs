using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private const string AttrDrop = "DROP";
	private const string AttrODrop = "ODROP";
	private const string AttrADrop = "ADROP";
	private const string AttrSuccess = "SUCCESS";
	private const string AttrOSuccess = "OSUCCESS";
	private const string AttrASuccess = "ASUCCESS";
	private const string AttrGive = "GIVE";
	private const string AttrOGive = "OGIVE";
	private const string AttrAGive = "AGIVE";
	private const string AttrReceive = "RECEIVE";
	private const string AttrOReceive = "ORECEIVE";
	private const string AttrAReceive = "ARECEIVE";
	private const string AttrLinkType = "_LINKTYPE";
	private const string LinkTypeVariable = "variable";
	private const string LinkTypeHome = "home";
	private const string AttrFollowing = "FOLLOWING";
	private const string AttrFollowers = "FOLLOWERS";

	/// <summary>
	/// Clears a follower's FOLLOWING attribute on the engine's own authority.
	/// </summary>
	/// <remarks>
	/// FOLLOWING is seeded with the <c>wizard</c> attribute flag, so no mortal can write it
	/// under their own authority - which is why PennMUSH performs every FOLLOWING write as
	/// GOD: <c>atr_clr(follower, "FOLLOWING", GOD)</c> (src/move.c:1451) and
	/// <c>atr_add(follower, "FOLLOWING", ..., GOD, 0)</c> (src/move.c:1236, 1243, 1292).
	/// This is engine bookkeeping, not a player write; routing it through the executor would
	/// deny every mortal FOLLOW, DESERT, DISMISS and UNFOLLOW.
	/// </remarks>
	private async ValueTask<OneOf<Success, Error<string>>> ClearFollowingAsync(
		AnySharpObject follower)
		=> await AttributeService.ClearAttributeAsync(await HelperFunctions.GetGod(Mediator), follower,
			AttrFollowing, IAttributeService.AttributePatternMode.Exact);

	/// <inheritdoc cref="ClearFollowingAsync"/>
	private async ValueTask<OneOf<Success, Error<string>>> SetFollowingAsync(
		AnySharpObject follower, AnySharpObject leader)
		=> await AttributeService.SetAttributeAsync(await HelperFunctions.GetGod(Mediator), follower,
			AttrFollowing, MarkupText.Plain(leader.Object().DBRef.ToString()));

	/// <summary>
	/// The dbrefs on <paramref name="leader"/>'s <c>FOLLOWERS</c> list, in order.
	/// PennMUSH keeps this list beside each follower's <c>FOLLOWING</c> so that
	/// <c>follower_command</c> (<c>src/move.c:1458</c>) can read the leader's followers directly
	/// instead of scanning every object's <c>FOLLOWING</c> after every successful move.
	/// </summary>
	/// <remarks>
	/// Read as GOD for the same reason the writes are: <c>FOLLOWERS</c> carries the <c>wizard</c>
	/// attribute flag (<c>AttributeEntrySeed.cs:68</c>), and a mortal following someone else has to
	/// be able to reach the leader's copy.
	/// </remarks>
	private async ValueTask<string[]> FollowersOfAsync(AnySharpObject leader)
	{
		var followers = await AttributeService.GetAttributeAsync(
			await HelperFunctions.GetGod(Mediator), leader, AttrFollowers,
			IAttributeService.AttributeMode.Read, parent: false);

		return followers.IsAttribute
			? [.. followers.AsAttribute.Last().Value.ToPlainText()
				.Split(' ', StringSplitOptions.RemoveEmptyEntries)]
			: [];
	}

	/// <inheritdoc cref="FollowersOfAsync"/>
	private async ValueTask<OneOf<Success, Error<string>>> WriteFollowersAsync(
		AnySharpObject leader, IEnumerable<string> followers)
	{
		var god = await HelperFunctions.GetGod(Mediator);
		var value = string.Join(' ', followers);

		// PennMUSH's atr_add with an empty value removes the attribute (src/atr.c), which is what
		// del_follower relies on to leave no empty FOLLOWERS behind.
		return value.Length == 0
			? await AttributeService.ClearAttributeAsync(god, leader, AttrFollowers,
				IAttributeService.AttributePatternMode.Exact)
			: await AttributeService.SetAttributeAsync(god, leader, AttrFollowers, MarkupText.Plain(value));
	}

	/// <summary>PennMUSH <c>add_follower</c> (<c>src/move.c:1208</c>).</summary>
	private async ValueTask AddFollowerAsync(AnySharpObject leader, AnySharpObject follower)
	{
		var followerRef = follower.Object().DBRef.ToString();
		var current = await FollowersOfAsync(leader);

		if (current.Contains(followerRef))
		{
			return;
		}

		await WriteFollowersAsync(leader, [.. current, followerRef]);
	}

	/// <summary>PennMUSH <c>del_follower</c> (<c>src/move.c:1262</c>).</summary>
	private async ValueTask RemoveFollowerAsync(AnySharpObject leader, AnySharpObject follower)
	{
		var followerRef = follower.Object().DBRef.ToString();
		var current = await FollowersOfAsync(leader);

		if (!current.Contains(followerRef))
		{
			return;
		}

		await WriteFollowersAsync(leader, current.Where(x => x != followerRef));
	}

	/// <summary>
	/// Whoever <paramref name="follower"/> currently follows, or none. SharpMUSH's <c>FOLLOWING</c>
	/// holds one leader rather than PennMUSH's list, so a new FOLLOW replaces the old one — and has
	/// to take the follower off the previous leader's <c>FOLLOWERS</c> as it does.
	/// </summary>
	private async ValueTask<AnySharpObject?> LeaderOfAsync(AnySharpObject follower)
	{
		var following = await AttributeService.GetAttributeAsync(
			await HelperFunctions.GetGod(Mediator), follower, AttrFollowing,
			IAttributeService.AttributeMode.Read, parent: false);

		if (!following.IsAttribute
				|| !DBRef.TryParse(following.AsAttribute.Last().Value.ToPlainText().Trim(), out var leaderRef))
		{
			return null;
		}

		var node = await Mediator.Send(new GetObjectNodeQuery(leaderRef!.Value));

		return node.IsNone ? null : node.Known;
	}

	/// <summary>
	/// Stops <paramref name="follower"/> following anyone, taking them off their leader's
	/// <c>FOLLOWERS</c> too. PennMUSH <c>clear_following</c> (<c>src/move.c:1425</c>).
	/// </summary>
	private async ValueTask<OneOf<Success, Error<string>>> StopFollowingAsync(AnySharpObject follower)
	{
		var leader = await LeaderOfAsync(follower);

		if (leader is not null)
		{
			await RemoveFollowerAsync(leader, follower);
		}

		return await ClearFollowingAsync(follower);
	}

	/// <summary>
	/// Stops everyone following <paramref name="leader"/>, clearing each follower's
	/// <c>FOLLOWING</c> and then the leader's own list. PennMUSH <c>clear_followers</c>
	/// (<c>src/move.c:1400</c>). Answers with the followers that were actually cleared, so the
	/// caller can report them.
	/// </summary>
	private async ValueTask<AnySharpObject[]> ClearFollowersAsync(AnySharpObject leader)
	{
		var followers = await FollowersOfAsync(leader);
		var cleared = new List<AnySharpObject>(followers.Length);

		foreach (var token in followers)
		{
			if (!DBRef.TryParse(token, out var followerRef))
			{
				continue;
			}

			var node = await Mediator.Send(new GetObjectNodeQuery(followerRef!.Value));

			if (node.IsNone || (await ClearFollowingAsync(node.Known)).IsT1)
			{
				continue;
			}

			cleared.Add(node.Known);
		}

		await WriteFollowersAsync(leader, []);

		return [.. cleared];
	}

	/// <summary>
	/// Re-issues <paramref name="command"/> for every object following <paramref name="leader"/>
	/// that was standing with them. PennMUSH <c>follower_command</c> (<c>src/move.c:1458</c>).
	/// </summary>
	/// <remarks>
	/// Each follower's command is queued as that follower with the leader as enactor, not run
	/// inline: a chain of followers would otherwise recurse on the stack.
	/// </remarks>
	private async ValueTask FollowerCommand(
		IMUSHCodeParser parser,
		AnySharpObject leader,
		AnySharpContainer from,
		string command,
		DBRef? toward)
	{
		var followers = await FollowersOfAsync(leader);

		if (followers.Length == 0)
		{
			return;
		}

		var line = toward is null ? command : $"{command} {toward}";

		// Every follower that gets this far was standing in `from`, so Penn's Dark(Location(follower))
		// is one read rather than one per follower.
		var leaderIsHidden = await leader.IsDarkLegal();
		var roomIsDark = await from.WithExitOption().HasFlag("DARK");
		var leaderIsLight = await leader.HasFlag("LIGHT");
		var leaderIsUnseen = leaderIsHidden || (roomIsDark && !leaderIsLight);

		foreach (var token in followers)
		{
			if (!DBRef.TryParse(token, out var followerRef))
			{
				continue;
			}

			var node = await Mediator.Send(new GetObjectNodeQuery(followerRef!.Value));

			if (node.IsNone)
			{
				continue;
			}

			var follower = node.Known;

			if (!follower.IsContent)
			{
				continue;
			}

			var followerLocation = await follower.AsContent.Location();

			if (!followerLocation.Object().DBRef.Equals(from.Object().DBRef))
			{
				continue;
			}

			// Connected(follower) || IsThing(follower) (move.c:1481). A logged-out player stays where
			// they left off rather than being walked around the game by whoever they last followed.
			if (!follower.IsThing && !await ConnectionService.IsOnline(follower))
			{
				continue;
			}

			// !(DarkLegal(leader) || (Dark(Location(follower)) && !Light(leader))) || See_All(follower)
			// (move.c:1482): a departure the follower could not have seen is not one they can follow.
			if (leaderIsUnseen && !await follower.HasPower("See_All"))
			{
				continue;
			}

			await NotifyService.NotifyLocalized(follower.Object().DBRef,
				nameof(ErrorMessages.Notifications.YouFollowFormat), leader.Object().Name);

			// move.c:1484 queues with parse_que, which is PE_INFO_DEFAULT over a NULL parent queue
			// (hdrs/externs.h:178): the follower's command gets an entirely fresh pe_info, not a view
			// of the leader's and not a clone of it. Every collection and counter on ParserState is a
			// reference type, and the queue entry runs on the scheduler's thread while the leader's
			// command list is still going, so carrying the leader's over would be a data race as well
			// as a semantic leak — the leader pops its register frame and its SwitchStack entry long
			// before the consumer drains this, and a shared ExecutionStack would let the follower's
			// @break stop the leader's list. It is not direct input either, so it carries no handle.
			await Mediator.Send(new AdmitCommandListRequest(
				MarkupText.Plain(line),
				parser.CurrentState with
				{
					Executor = follower.Object().DBRef,
					Enactor = leader.Object().DBRef,
					Caller = leader.Object().DBRef,
					Handle = null,
					// parse_que passes no pe_regs, so %0-%9 and the q-registers start empty.
					Arguments = new Dictionary<string, CallState>(),
					EnvironmentRegisters = new Dictionary<string, CallState>(),
					CallerArguments = null,
					Registers = new([[]]),
					IterationRegisters = [],
					RegexRegisters = [],
					SwitchStack = [],
					ExecutionStack = [],
					CallDepth = new InvocationCounter(),
					FunctionRecursionDepths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
					TotalInvocations = new InvocationCounter(),
					LimitExceeded = new LimitExceededFlag(),
					MoveDepth = new InvocationCounter(),
					CommandHistory = null,
					BreakPropagation = null,
					HttpResponse = null
				},
				new DbRefAttribute(follower.Object().DBRef, DefaultSemaphoreAttributeArray),
				-1), ExecutionBudget.CurrentToken);
		}
	}


	[SharpCommand(Name = "@CLOCK", Switches = ["JOIN", "SPEAK", "MOD", "SEE", "HIDE"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 1, MaxArgs = 2, ParameterNames = [])]
	public async ValueTask<Option<CallState>> ChannelLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		var channelName = args["0"].Message!;
		var lockKey = args.TryGetValue("1", out var arg1) ? arg1.Message!.ToPlainText() : string.Empty;

		var lockType = switches.FirstOrDefault() ?? "JOIN";
		lockType = lockType.ToUpper();

		// Setting a lock on a channel you cannot see must be refused the same way as setting one on a
		// channel that does not exist, or @clock reports which names are taken. notify: true because the
		// gate emits ONE refusal for both cases: suppressing it does not make the two cases more alike, it
		// only makes a mistyped channel name fail in silence.
		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, notify: true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// This was Chan_Can_Modify rewritten by hand, and it carried the same defect: an unset ModLock made
		// `passesModLock` true for everybody, so any non-guest could set the join/speak/see/hide/mod lock on
		// any channel — and no channel has a ModLock, because CreateChannelCommand never writes one.
		// ChannelCanModifyAsync now skips an unset lock rather than evaluating it; going through it means
		// this command cannot drift away from that rule again.
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		UpdateChannelCommand updateCommand = lockType switch
		{
			"JOIN" => new UpdateChannelCommand(channel, null, null, null, lockKey, null, null, null, null, null, null),
			"SPEAK" => new UpdateChannelCommand(channel, null, null, null, null, lockKey, null, null, null, null, null),
			"SEE" => new UpdateChannelCommand(channel, null, null, null, null, null, lockKey, null, null, null, null),
			"HIDE" => new UpdateChannelCommand(channel, null, null, null, null, null, null, lockKey, null, null, null),
			"MOD" => new UpdateChannelCommand(channel, null, null, null, null, null, null, null, lockKey, null, null),
			_ => new UpdateChannelCommand(channel, null, null, null, null, null, null, null, null, null, null)
		};

		if (lockType is not ("JOIN" or "SPEAK" or "SEE" or "HIDE" or "MOD"))
		{
			await NotifyService.Notify(executor, $"Invalid lock type: {lockType}", executor);
			return new CallState(ErrorMessages.Returns.InvalidLockType);
		}

		await Mediator.Send(updateCommand);

		if (string.IsNullOrEmpty(lockKey))
		{
			await NotifyService.Notify(executor, $"{lockType} lock removed from channel {channel.Name.ToPlainText()}.", executor);
		}
		else
		{
			await NotifyService.Notify(executor, $"{lockType} lock set on channel {channel.Name.ToPlainText()}.", executor);
		}

		return CallState.Empty;
	}

	/// <summary>
	/// The eight things <c>@list</c> can list. PennMUSH spells each of them both ways — as a switch
	/// (<c>cmd_list</c>) and as an argument (<c>do_list</c>), both in src/cmds.c — so SharpMUSH does too.
	/// </summary>
	private enum ListKind
	{
		Motd,
		Functions,
		Commands,
		Attribs,
		Locks,
		Flags,
		Powers,
		Allocations
	}

	/// <summary>
	/// Resolves <c>@list &lt;type&gt;</c>'s argument the way PennMUSH's <c>do_list</c> (src/cmds.c) does:
	/// in that order, and with that mix of prefix and exact matching. "commands", "functions", "powers",
	/// "locks" and "allocations" accept any non-empty prefix (<c>string_prefixe</c>); "motd", "attribs"
	/// and "flags" must be spelled in full (<c>strcasecmp</c>).
	/// </summary>
	/// <remarks>
	/// The order is load-bearing, not incidental: "f" reaches <c>functions</c> by prefix before it can
	/// reach the exact-match-only <c>flags</c>, exactly as it does in PennMUSH.
	/// </remarks>
	private ListKind? ResolveListKind(string argument)
	{
		var arg = argument.Trim();
		if (arg.Length == 0) return null;

		bool Prefix(string full) => full.StartsWith(arg, StringComparison.OrdinalIgnoreCase);
		bool Exact(string full) => full.Equals(arg, StringComparison.OrdinalIgnoreCase);

		if (Prefix("commands")) return ListKind.Commands;
		if (Prefix("functions")) return ListKind.Functions;
		if (Exact("motd")) return ListKind.Motd;
		if (Exact("attribs")) return ListKind.Attribs;
		if (Exact("flags")) return ListKind.Flags;
		if (Prefix("powers")) return ListKind.Powers;
		if (Prefix("locks")) return ListKind.Locks;
		if (Prefix("allocations")) return ListKind.Allocations;

		return null;
	}

	[SharpCommand(Name = "@LIST",
		Switches =
		[
			"LOWERCASE", "MOTD", "LOCKS", "FLAGS", "FUNCTIONS", "POWERS", "COMMANDS", "ATTRIBS", "ALLOCATIONS", "ALL",
			"BUILTIN", "LOCAL"
		], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["type"])]
	public async ValueTask<Option<CallState>> List(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var useLowercase = switches.Contains("LOWERCASE");

		// PennMUSH's cmd_list consults the switches first and only falls through to do_list — which reads
		// the same eight names off the argument — when none of them is set. A switch therefore still wins
		// over a contradicting argument, and `@list/lowercase commands` keeps working.
		var kind =
			switches.Contains("MOTD") ? ListKind.Motd
			: switches.Contains("FUNCTIONS") ? ListKind.Functions
			: switches.Contains("COMMANDS") ? ListKind.Commands
			: switches.Contains("ATTRIBS") ? ListKind.Attribs
			: switches.Contains("LOCKS") ? ListKind.Locks
			: switches.Contains("FLAGS") ? ListKind.Flags
			: switches.Contains("POWERS") ? ListKind.Powers
			: switches.Contains("ALLOCATIONS") ? ListKind.Allocations
			: ResolveListKind(parser.CurrentState.Arguments.TryGetValue("0", out var typeArg)
				? typeArg.Message?.ToPlainText() ?? string.Empty
				: string.Empty);

		if (kind is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListNotUnderstood), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Motd)
		{
			var isWizard = await executor.IsWizard();

			var motdFile = Configuration.CurrentValue.Message.MessageOfTheDayFile;
			var motdHtmlFile = Configuration.CurrentValue.Message.MessageOfTheDayHtmlFile;

			await NotifyService.Notify(executor, "Current Message of the Day settings:", executor);
			await NotifyService.Notify(executor, $"  Connect MOTD File: {motdFile ?? "(not set)"}", executor);
			await NotifyService.Notify(executor, $"  Connect MOTD HTML: {motdHtmlFile ?? "(not set)"}", executor);

			if (isWizard)
			{
				var wizmotdFile = Configuration.CurrentValue.Message.WizMessageOfTheDayFile;
				var wizmotdHtmlFile = Configuration.CurrentValue.Message.WizMessageOfTheDayHtmlFile;

				await NotifyService.Notify(executor, $"  Wizard MOTD File: {wizmotdFile ?? "(not set)"}", executor);
				await NotifyService.Notify(executor, $"  Wizard MOTD HTML: {wizmotdHtmlFile ?? "(not set)"}", executor);
			}

			return CallState.Empty;
		}

		if (kind == ListKind.Flags)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Object Flags:" : "OBJECT FLAGS:";
			output.AppendLine(header);

			var headerLine = useLowercase
				? "name                 symbol type restrictions"
				: "NAME                 SYMBOL TYPE RESTRICTIONS";
			output.AppendLine(headerLine);
			output.AppendLine("-------------------- ------ -------------------");

			var flags = Mediator.CreateStream(new GetAllObjectFlagsQuery());
			await foreach (var flag in flags)
			{
				var flagName = useLowercase ? flag.Name?.ToLower() ?? "" : flag.Name ?? "";
				var symbol = useLowercase ? flag.Symbol?.ToLower() ?? "" : flag.Symbol ?? "";
				var types = string.Join(",", (flag.TypeRestrictions ?? []).Select(t => useLowercase ? t?.ToLower() ?? "" : t ?? ""));
				output.AppendLine($"{flagName,-20} {symbol,-6} {types}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Powers)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Object Powers:" : "OBJECT POWERS:";
			output.AppendLine(header);

			var headerLine = useLowercase
				? "name                 symbol alias              type restrictions"
				: "NAME                 SYMBOL ALIAS              TYPE RESTRICTIONS";
			output.AppendLine(headerLine);
			output.AppendLine("-------------------- ------ ------------------ -------------------");

			var powers = Mediator.CreateStream(new GetPowersQuery());
			await foreach (var power in powers)
			{
				var powerName = useLowercase ? power.Name.ToLower() : power.Name;
				var alias = useLowercase ? power.Alias.ToLower() : power.Alias;
				// A power's letter is case-sensitive, so /lowercase never folds it.
				var types = string.Join(",", power.TypeRestrictions.Select(t => useLowercase ? t.ToLower() : t));
				output.AppendLine($"{powerName,-20} {power.Symbol,-6} {alias,-18} {types}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Locks)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Lock Types:" : "LOCK TYPES:";
			output.AppendLine(header);

			var lockTypes = Enum.GetNames(typeof(LockType));
			foreach (var lockType in lockTypes.OrderBy(x => x))
			{
				var displayName = useLowercase ? lockType.ToLower() : lockType.ToUpper();
				output.AppendLine($"  {displayName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Attribs)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Standard Attributes:" : "STANDARD ATTRIBUTES:";
			output.AppendLine(header);

			var attributes = Mediator.CreateStream(new GetAllAttributeEntriesQuery());
			await foreach (var attr in attributes.OrderBy(x => x.Name))
			{
				var attrName = useLowercase ? attr.Name.ToLower() : attr.Name;
				output.AppendLine($"  {attrName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Commands)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Commands:" : "COMMANDS:";
			output.AppendLine(header);

			var filterBuiltin = switches.Contains("BUILTIN");
			var filterLocal = switches.Contains("LOCAL");

			var commandPairs = CommandLibrary.AsEnumerable();

			if (filterBuiltin && !filterLocal)
			{
				commandPairs = commandPairs.Where(kvp => kvp.Value.IsSystem);
			}
			else if (filterLocal && !filterBuiltin)
			{
				commandPairs = commandPairs.Where(kvp => !kvp.Value.IsSystem);
			}

			var commands = commandPairs
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);

			foreach (var displayName in commands.Select(cmdName => useLowercase ? cmdName.ToLower() : cmdName))
			{
				output.AppendLine($"  {displayName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Functions)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Functions:" : "FUNCTIONS:";
			output.AppendLine(header);

			var filterBuiltin = switches.Contains("BUILTIN");
			var filterLocal = switches.Contains("LOCAL");

			var functionPairs = FunctionLibrary.AsEnumerable();

			if (filterBuiltin && !filterLocal)
			{
				functionPairs = functionPairs.Where(kvp => kvp.Value.IsSystem);
			}
			else if (filterLocal && !filterBuiltin)
			{
				functionPairs = functionPairs.Where(kvp => !kvp.Value.IsSystem);
			}

			var functions = functionPairs
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);

			foreach (var displayName in functions.Select(funcName => useLowercase ? funcName.ToLower() : funcName))
			{
				output.AppendLine($"  {displayName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Allocations)
		{
			var isWizard = await executor.IsWizard();
			if (!isWizard)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var output = new System.Text.StringBuilder();
			output.AppendLine("Memory Allocations:");
			output.AppendLine($"  Total Memory: {GC.GetTotalMemory(false):N0} bytes");
			output.AppendLine($"  GC Gen 0 Collections: {GC.CollectionCount(0)}");
			output.AppendLine($"  GC Gen 1 Collections: {GC.CollectionCount(1)}");
			output.AppendLine($"  GC Gen 2 Collections: {GC.CollectionCount(2)}");

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		// Unreachable: every ListKind has a branch above, and a null kind returned early.
		throw new UnreachableException($"@list has no branch for {kind}.");
	}

	[SharpCommand(Name = "@LOGWIPE", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "ROTATE", "TRIM", "WIPE"],
		Behavior = CB.Default | CB.NoGagged | CB.God, MinArgs = 0, MaxArgs = 0, ParameterNames = ["type"])]
	public async ValueTask<Option<CallState>> LogWipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		if (!executor.IsGod())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var logTypes = new[] { "CMD", "CONN", "ERR", "TRACE", "WIZ" };
		var actions = new[] { "ROTATE", "TRIM", "WIPE", "CHECK" };

		var specifiedLogType = switches.FirstOrDefault(s => logTypes.Contains(s));
		var specifiedAction = switches.FirstOrDefault(s => actions.Contains(s)) ?? "CHECK";

		if (specifiedLogType == null && specifiedAction == "CHECK")
		{
			await NotifyService.Notify(executor, "Log Management Status:", executor);
			await NotifyService.Notify(executor, "  SharpMUSH uses .NET logging infrastructure", executor);
			await NotifyService.Notify(executor, "  Logs are managed by configured logging providers", executor);
			await NotifyService.Notify(executor, "  Available log types: CMD, CONN, ERR, TRACE, WIZ", executor);
			await NotifyService.Notify(executor, "  Available actions: ROTATE, TRIM, WIPE", executor);
			await NotifyService.Notify(executor, "  Note: Direct log file manipulation not yet implemented", executor);
			Logger?.LogInformation("@LOGWIPE/CHECK executed by {Executor}", executor.Object().Name);
		}
		else
		{
			var logDesc = specifiedLogType ?? "all logs";
			await NotifyService.Notify(executor, $"@LOGWIPE/{specifiedAction}: Would {specifiedAction.ToLower()} {logDesc}", executor);
			await NotifyService.Notify(executor, "Direct log file manipulation not yet implemented.", executor);
			await NotifyService.Notify(executor, "Configure log rotation through appsettings.json or hosting provider.", executor);
			Logger?.LogWarning("@LOGWIPE/{Action} requested for {LogType} by {Executor} - not implemented",
				specifiedAction, logDesc, executor.Object().Name);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@LSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 2, ParameterNames = ["list", "position", "value"])]
	public async ValueTask<Option<CallState>> LockSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var objectLock = args["0"].Message!.ToPlainText();
		var flagValue = args["1"].Message!.ToPlainText();

		var slashIndex = objectLock.LastIndexOf('/');
		if (slashIndex == -1)
		{
			await NotifyService.Notify(executor, "Invalid format. Use: @lset <object>/<lock type>=[!]<flag>", executor);
			return new CallState(ErrorMessages.Returns.InvalidFormat);
		}

		var objectName = objectLock[..slashIndex];
		var lockType = objectLock[(slashIndex + 1)..];

		var isClearing = flagValue.StartsWith('!');
		var flagName = isClearing ? flagValue[1..] : flagValue;

		if (!LockService.LockPrivileges.TryGetValue(flagName.ToLower(), out var flagInfo))
		{
			await NotifyService.Notify(executor, $"Invalid flag: {flagName}", executor);
			return new CallState(ErrorMessages.Returns.InvalidFlag);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService.Controls(executor, obj))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				if (!obj.Object().Locks.TryGetValue(LockNames.Canonical(lockType), out var lockData))
				{
					await NotifyService.Notify(executor, $"No such lock: {lockType}", executor);
					return new CallState(ErrorMessages.Returns.NoSuchLock);
				}

				var currentFlags = lockData.Flags;
				var newFlags = isClearing
					? currentFlags & ~flagInfo.Item2
					: currentFlags | flagInfo.Item2;

				var updatedLockData = new Library.Models.SharpLockData(lockData.LockString, newFlags);

				await Mediator.Send(new SetLockCommand(obj.Object(), lockType, updatedLockData.LockString));

				await NotifyService.Notify(executor, $"Flag {flagName} {(isClearing ? "cleared" : "set")} on {lockType} lock.", executor);
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@MALIAS",
		Switches =
		[
			"SET", "CREATE", "DESTROY", "DESCRIBE", "RENAME", "STATS", "CHOWN", "NUKE", "ADD", "REMOVE", "LIST", "ALL", "WHO",
			"MEMBERS", "USEFLAG", "SEEFLAG"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["alias", "list"])]
	public async ValueTask<Option<CallState>> MailAlias(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		var action = switches.FirstOrDefault() ?? "LIST";

		await NotifyService.Notify(executor, $"@MALIAS/{action}: Mail alias system not yet implemented.", executor);
		await NotifyService.Notify(executor, "This command would manage mail distribution lists and aliases.", executor);

		return CallState.Empty;
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

	private async ValueTask<IConnectionService.ConnectionData?> LeastIdleConnection(DBRef who)
		=> await ConnectionService.Get(who).MinByAsync(connection => connection.Idle ?? TimeSpan.MaxValue);

	[SharpCommand(Name = "@SLAVE", Switches = ["RESTART"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Slave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.Notify(executor, "Slave command does nothing for SharpMUSH.", executor);
		return new None();
	}

	[SharpCommand(Name = "@UNRECYCLE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> UnRecycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		await NotifyService.Notify(executor, "@UNRECYCLE: Object recovery system not yet implemented.", executor);
		await NotifyService.Notify(executor, "This command would restore objects from the recycle bin.", executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@WARNINGS", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Warnings(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.TryGetValue("0", out var objectArg) || string.IsNullOrWhiteSpace(objectArg.Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Usage: @warnings <object>=<warning list>", executor);
			await NotifyService.Notify(executor, "Available warnings: none, serious, normal, extra, all", executor);
			await NotifyService.Notify(executor, "Individual: exit-unlinked, exit-oneway, exit-multiple, exit-msgs, exit-desc,", executor);
			await NotifyService.Notify(executor, "           thing-msgs, thing-desc, room-desc, my-desc, lock-checks", executor);
			await NotifyService.Notify(executor, "Use !warning to negate (e.g., 'all !exit-desc')", executor);
			return CallState.Empty;
		}

		if (!args.TryGetValue("1", out var warningListArg))
		{
			await NotifyService.Notify(executor, "Usage: @warnings <object>=<warning list>", executor);
			return CallState.Empty;
		}

		var objectString = objectArg.Message?.ToString() ?? string.Empty;
		var target = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objectString,
			LocateFlags.All);

		if (target.IsError)
		{
			return target.AsError;
		}

		var targetObj = target.AsSharpObject.Object();

		if (!await PermissionService.Controls(executor, target.AsSharpObject))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var warningListString = warningListArg.Message?.ToPlainText() ?? string.Empty;
		var unknownWarnings = new List<string>();
		var newWarnings = WarningTypeHelper.ParseWarnings(warningListString, unknownWarnings);

		foreach (var unknown in unknownWarnings)
		{
			await NotifyService.Notify(executor, $"Unknown warning: {unknown}", executor);
		}

		var oldWarnings = targetObj.Warnings;

		await Mediator.Send(new SetObjectWarningsCommand(target.AsSharpObject, newWarnings));

		if (newWarnings != WarningType.None)
		{
			var warningString = WarningTypeHelper.UnparseWarnings(newWarnings);
			await NotifyService.Notify(executor, $"Warnings set to: {warningString}", executor);
		}
		else
		{
			await NotifyService.Notify(executor, "Warnings cleared.", executor);
		}

		Logger?.LogInformation("@WARNINGS: {Executor} set warnings on {Target} from {Old} to {New}",
			executor.Object().Name, targetObj.Name, oldWarnings, newWarnings);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@WCHECK", Switches = ["ALL", "ME"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> WizardCheck(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		var checkAll = switches.Contains("ALL");
		var checkMe = switches.Contains("ME");

		if (checkAll)
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.Notify(executor, "You'd better check your wizbit first.", executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			await NotifyService.Notify(executor, "Running database topology warning checks...", executor);
			var checkedCount = await WarningService.CheckAllObjectsAsync();
			await NotifyService.Notify(executor, $"Warning checks complete. Checked {checkedCount} objects.", executor);

			Logger?.LogInformation("@WCHECK/ALL executed by {Executor}, checked {Count} objects",
				executor.Object().Name, checkedCount);
		}
		else if (checkMe)
		{
			await NotifyService.Notify(executor, "Checking objects you own...", executor);
			var warningCount = await WarningService.CheckOwnedObjectsAsync(executor);

			Logger?.LogInformation("@WCHECK/ME executed by {Executor}, found {Count} warnings",
				executor.Object().Name, warningCount);
		}
		else
		{
			if (!args.TryGetValue("0", out var objectArg) || string.IsNullOrWhiteSpace(objectArg.Message?.ToPlainText()))
			{
				await NotifyService.Notify(executor, "Usage: @wcheck <object> or @wcheck/me or @wcheck/all", executor);
				return CallState.Empty;
			}

			var objectString = objectArg.Message?.ToString() ?? string.Empty;
			var target = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objectString,
				LocateFlags.All);

			if (target.IsError)
			{
				return target.AsError;
			}

			var targetObj = target.AsSharpObject.Object();
			var targetOwner = await targetObj.Owner.WithCancellation(CancellationToken.None);

			if (!(await executor.IsSee_All() || targetOwner.Object.DBRef.Equals(executor.Object().DBRef)))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			await WarningService.CheckObjectAsync(executor, target.AsSharpObject);
			await NotifyService.Notify(executor, "@wcheck complete.", executor);

			Logger?.LogInformation("@WCHECK executed by {Executor} on {Target}",
				executor.Object().Name, targetObj.Name);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "BUY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 3, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Buy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var itemName = args["0"].Message!.ToPlainText();
		await NotifyService.Notify(executor, $"You try to buy '{itemName}'.", executor);
		await NotifyService.Notify(executor, "The BUY command requires a full economy system implementation.", executor);
		await NotifyService.Notify(executor, "Features needed: PRICELIST attribute parsing, @lock/pay checking, penny transfers.", executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "BRIEF", Switches = ["OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Brief(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = (await parser.CurrentState.EnactorObject(Mediator)).WithoutNone();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		AnyOptionalSharpObject viewing;

		if (args.Count == 1)
		{
			var argText = args["0"].Message!.ToPlainText();

			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				argText,
				LocateFlags.All);

			if (locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
			else
			{
				return new None();
			}
		}
		else
		{
			viewing = (await Mediator.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var viewingKnown = viewing.Known();

		var canExamine = await PermissionService.CanExamine(executor, viewingKnown);

		if (!canExamine)
		{
			var limitedObj = viewingKnown.Object();
			var limitedOwnerObj = (await limitedObj.Owner.WithCancellation(CancellationToken.None)).Object;
			await NotifyService.Notify(enactor, $"{limitedObj.Name} is owned by {limitedOwnerObj.Name}.", enactor);
			return new CallState(limitedObj.DBRef.ToString());
		}

		var contents = (switches.Contains("OPAQUE") || viewing.IsExit)
			? []
			: await Mediator.CreateStream(new GetContentsQuery(viewingKnown.AsContainer))
				.ToArrayAsync();

		var obj = viewingKnown.Object()!;
		var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var objFlags = await obj.Flags.Value.ToArrayAsync();
		var objPowers = obj.Powers.Value;
		var objParent = await obj.Parent.WithCancellation(CancellationToken.None);

		var outputSections = new List<MString>();

		var showFlags = Configuration.CurrentValue.Cosmetic.FlagsOnExamine;
		var nameRow = showFlags
			? MarkupText.Concat([
				name.Hilight(),
				MarkupText.Space,
				MarkupText.Plain($"(#{obj.DBRef.Number}{MessageFormatting.FlagSymbols(objFlags)})")
			])
			: MarkupText.Concat(name.Hilight(), MarkupText.Plain($" (#{obj.DBRef.Number})"));

		outputSections.Add(nameRow);

		if (showFlags)
		{
			outputSections.Add(MarkupText.Plain($"Type: {obj.Type} Flags: {string.Join(" ", objFlags.Select(x => x.Name))}"));
		}
		else
		{
			outputSections.Add(MarkupText.Plain($"Type: {obj.Type}"));
		}

		var ownerRow = showFlags
			? MarkupText.Plain($"Owner: {ownerName.Hilight()}" +
											 $"(#{ownerObj.DBRef.Number}{await MessageFormatting.FlagSymbolsAsync(ownerObj)})")
			: MarkupText.Plain($"Owner: {ownerName.Hilight()}(#{ownerObj.DBRef.Number})");
		outputSections.Add(ownerRow);

		outputSections.Add(MarkupText.Plain($"Parent: {objParent.Object()?.Name ?? "*NOTHING*"}"));

		if (obj.Locks.Count > 0)
		{
			var lockLines = obj.Locks
				.Select(kvp =>
				{
					var lockName = kvp.Key;
					var lockData = kvp.Value;
					var flagsStr = LockService.FormatLockFlags(lockData.Flags);
					var flagsDisplay = string.IsNullOrEmpty(flagsStr) ? "" : $"[{flagsStr}]";
					return $"{lockName}{flagsDisplay}: {lockData.LockString}";
				});

			outputSections.Add(MarkupText.Plain($"Locks:"));
			foreach (var lockLine in lockLines)
			{
				outputSections.Add(MarkupText.Plain($"  {lockLine}"));
			}
		}

		var powersList = await objPowers.Select(x => x.Name).ToArrayAsync();
		if (powersList.Length > 0)
		{
			outputSections.Add(MarkupText.Plain($"Powers: {string.Join(" ", powersList)}"));
		}

		if (viewingKnown.IsPlayer || viewingKnown.IsThing)
		{
			var homeObj = (await viewingKnown.MinusRoom().Home()).WithoutNone();
			outputSections.Add(MarkupText.Plain($"Home: {homeObj.Object().Name}(#{homeObj.Object().DBRef.Number})"));

			var locationObj = await viewingKnown.Where();
			outputSections.Add(MarkupText.Plain($"Location: {locationObj.Object().Name}(#{locationObj.Object().DBRef.Number})"));
		}

		outputSections.Add(MarkupText.Plain($"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F}"));

		await NotifyService.Notify(enactor, MarkupText.Join(MarkupText.Plain("\n"), outputSections), enactor);

		if (!switches.Contains("OPAQUE") && contents.Length > 0)
		{
			var contentNames = contents.Select(x => x.Object().Name);
			await NotifyService.Notify(enactor, $"Contents:", enactor);
			foreach (var contentName in contentNames)
			{
				await NotifyService.Notify(enactor, $"  {contentName}", enactor);
			}
		}

		return new CallState(obj.DBRef.ToString());
	}

	[SharpCommand(Name = "DESERT", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 1, ParameterNames = ["follower"])]
	public async ValueTask<Option<CallState>> Desert(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			// clear_following then clear_followers (move.c:1201-1202), both of which keep the two
			// lists in step. The leader's FOLLOWERS is what names the followers, so no scan of every
			// object's FOLLOWING is needed to find them.
			var selfCleared = await StopFollowingAsync(executor);
			if (selfCleared.IsT1)
			{
				await NotifyService.Notify(executor, selfCleared.AsT1.Value, executor);
				return CallState.Empty;
			}

			await ClearFollowersAsync(executor);

			await NotifyService.Notify(executor, "You stop following and dismiss all followers.", executor);
			return CallState.Empty;
		}

		var targetName = args["0"].Message!.ToPlainText();

		var targetResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, targetName, LocateFlags.All);

		if (!targetResult.IsValid())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var target = targetResult.WithoutError().WithoutNone();

		var followingAttr = await AttributeService.GetAttributeAsync(executor, executor, AttrFollowing,
			IAttributeService.AttributeMode.Read, false);

		if (followingAttr.IsAttribute)
		{
			var followingDbref = followingAttr.AsAttribute.Last().Value.ToPlainText();
			if (followingDbref == target.Object().DBRef.ToString())
			{
				// del_follow(player, who) — both lists (move.c:1197).
				var cleared = await StopFollowingAsync(executor);
				if (cleared.IsT1)
				{
					await NotifyService.Notify(executor, cleared.AsT1.Value, executor);
					return CallState.Empty;
				}

				await NotifyService.Notify(executor, $"You stop following {target.Object().Name}.", executor);
			}
		}

		var targetFollowingAttr = await AttributeService.GetAttributeAsync(executor, target, AttrFollowing,
			IAttributeService.AttributeMode.Read, false);

		if (targetFollowingAttr.IsAttribute)
		{
			var targetFollowingDbref = targetFollowingAttr.AsAttribute.Last().Value.ToPlainText();
			if (targetFollowingDbref == executor.Object().DBRef.ToString())
			{
				// del_follow(who, player) — the other direction (move.c:1198).
				var dismissed = await StopFollowingAsync(target);
				if (dismissed.IsT1)
				{
					await NotifyService.Notify(executor, dismissed.AsT1.Value, executor);
					return CallState.Empty;
				}

				await NotifyService.Notify(executor, $"You dismiss {target.Object().Name}.", executor);
				await NotifyService.Notify(target, $"{executor.Object().Name} deserts you. You stop following.");
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "DISMISS", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Dismiss(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			// clear_followers (move.c:1163). The leader's own FOLLOWERS names them, so nothing has to
			// walk every object in the database to find out who was following.
			var dismissed = await ClearFollowersAsync(executor);

			foreach (var follower in dismissed)
			{
				await NotifyService.Notify(follower, $"{executor.Object().Name} dismisses you. You stop following.");
			}

			await NotifyService.Notify(executor, $"You dismiss all your followers. ({dismissed.Length} dismissed)", executor);
			return CallState.Empty;
		}

		var targetName = args["0"].Message!.ToPlainText();

		var targetResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, targetName, LocateFlags.All);

		if (!targetResult.IsValid())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var target = targetResult.WithoutError().WithoutNone();

		var followingAttr = await AttributeService.GetAttributeAsync(executor, target, AttrFollowing,
			IAttributeService.AttributeMode.Read, false);

		if (followingAttr.IsNone || followingAttr.IsError)
		{
			await NotifyService.Notify(executor, $"{target.Object().Name} is not following you.", executor);
			return CallState.Empty;
		}

		var followingDbref = followingAttr.AsAttribute.Last().Value.ToPlainText();
		if (followingDbref != executor.Object().DBRef.ToString())
		{
			await NotifyService.Notify(executor, $"{target.Object().Name} is not following you.", executor);
			return CallState.Empty;
		}

		// del_follow(player, follower) — both lists (move.c:1162).
		var targetDismissed = await StopFollowingAsync(target);
		if (targetDismissed.IsT1)
		{
			await NotifyService.Notify(executor, targetDismissed.AsT1.Value, executor);
			return CallState.Empty;
		}

		await NotifyService.Notify(executor, $"You dismiss {target.Object().Name}.", executor);
		await NotifyService.Notify(target, $"{executor.Object().Name} dismisses you. You stop following.");

		return CallState.Empty;
	}

	[SharpCommand(Name = "DROP", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Drop(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var objectName = args["0"].Message!.ToPlainText();

		var locateResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, LocateFlags.All);

		if (!locateResult.IsValid() || locateResult.IsRoom || locateResult.IsExit)
		{
			await NotifyService.Notify(executor, "You can't drop that.", executor);
			return CallState.Empty;
		}

		var objectToDrop = locateResult.WithoutError().WithoutNone();

		var executorLocation = await executor.Where();

		var objectLocation = await objectToDrop.Where();

		bool isCarrying = objectLocation.Match(
			player => player.Object.DBRef.Equals(executor.Object().DBRef),
			room => room.Object.DBRef.Equals(executor.Object().DBRef),
			thing => thing.Object.DBRef.Equals(executor.Object().DBRef));

		if (!isCarrying)
		{
			await NotifyService.Notify(executor, "You aren't carrying that.", executor);
			return CallState.Empty;
		}

		var currentRoom = executorLocation;

		// The drop lock fails on the object being dropped (move.c:736-738) and again on the room, when
		// the location is one (move.c:740-744); both return. The drop-in lock is the room's
		// (move.c:745-747), and its branch is the one `else if` in the chain with no `return` — the
		// object stays put, but do_drop still falls through to the DROP triad at move.c:768.
		if (!await LockService.Evaluate(LockType.Drop, objectToDrop, executor))
		{
			await DidItService.FailLock(parser, executor, objectToDrop, LockType.Drop,
				MarkupText.Plain(ErrorMessages.Notifications.CantSeemToGetRidOfThat));
			return CallState.Empty;
		}

		if (currentRoom.IsRoom && !await LockService.Evaluate(LockType.Drop, currentRoom.WithExitOption(), executor))
		{
			await DidItService.FailLock(parser, executor, currentRoom.WithExitOption(), LockType.Drop,
				MarkupText.Plain(ErrorMessages.Notifications.CantSeemToDropThingsHere));
			return CallState.Empty;
		}

		var dropInRefused = !await LockService.Evaluate(LockType.DropIn, currentRoom.WithExitOption(), executor);

		if (dropInRefused)
		{
			await DidItService.FailLock(parser, executor, currentRoom.WithExitOption(), LockType.DropIn,
				MarkupText.Plain(ErrorMessages.Notifications.CantSeemToDropThingsHere));
		}
		else
		{
			await DropTo(parser, executor, objectToDrop, currentRoom, "drop");
		}

		// did_it(player, thing, "DROP", "You drop X.", "ODROP", "drops X.", "ADROP", NOTHING)
		// (move.c:768-769). It is the tail of do_drop and runs whichever branch the object took —
		// the room, the drop-to, home, or the drop-in refusal that moves it nowhere — so it sits
		// after the drop-to handling rather than inside it, and it is the dropper's only success
		// message.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: objectToDrop,
			What: AttrDrop,
			Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.YouDrop, objectToDrop.Object().Name)),
			OWhat: AttrODrop,
			ODef: string.Format(ErrorMessages.Notifications.Drops, objectToDrop.Object().Name),
			AWhat: AttrADrop,
			Loc: currentRoom));

		return CallState.Empty;
	}

	/// <summary>
	/// Where a dropped object actually lands, and the single move that puts it there. PennMUSH
	/// <c>do_drop</c>'s three-way chain (<c>src/move.c:748-759</c>), shared with <c>EMPTY</c>, whose
	/// drop half is the same chain (<c>src/move.c:890-899</c>).
	/// </summary>
	/// <remarks>
	/// One move, straight to the destination. Landing the object in the room first and then pushing it
	/// through the drop-to would announce an arrival in a room it never stayed in, and would run the
	/// enter and leave triads for it.
	/// <para>
	/// DEVIATION on the STICKY branch, for <c>EMPTY</c> only: <c>do_empty</c> sends <c>thing</c> — the
	/// container being emptied — home rather than the item it just took out, and skips the
	/// <c>Dropped.</c> message <c>do_drop</c> sends. Both commands use <c>do_drop</c>'s shape here.
	/// </para>
	/// </remarks>
	private async ValueTask DropTo(
		IMUSHCodeParser parser,
		AnySharpObject dropper,
		AnySharpObject thing,
		AnySharpContainer location,
		string cause)
	{
		var content = thing.AsContent;
		AnySharpObject owner = await thing.Object().Owner.WithCancellation(CancellationToken.None);

		// move.c:748-750. Fixed(x) is a flag on the OWNER (hdrs/dbdefs.h:84), not on the object.
		if (await thing.HasFlag("STICKY") && !await owner.HasFlag("FIXED"))
		{
			await NotifyService.Notify(thing, ErrorMessages.Notifications.Dropped);

			var home = await content.Home();

			// safe_tel resolves HOME itself; MoveService takes a resolved container, so an object with
			// no home has nowhere to be sent and stays where it is.
			if (!home.IsNone)
			{
				await MoveService.SafeTel(parser, content, home.WithoutNone(), noMoveMsgs: false,
					dropper.Object().DBRef, cause);
			}

			return;
		}

		// move.c:751-755: the room's immediate drop-to, gated on the room NOT being STICKY — a STICKY
		// room holds its contents until the last Dropper leaves, which is what MoveService.EnterRoom's
		// maybe_dropto handles.
		var destination = location;

		if (location.IsRoom && !await location.WithExitOption().HasFlag("STICKY"))
		{
			var dropTo = await location.AsRoom.Location.WithCancellation(CancellationToken.None);

			// The drop-to lock is evaluated against the room with the OBJECT as the one being tested,
			// not the dropper (move.c:754).
			if (!dropTo.IsNone
					&& await LockService.Evaluate(LockType.DropTo, location.WithExitOption(), thing))
			{
				destination = dropTo.WithoutNone();
			}
		}

		// move.c:752 and :757 — the dropped object is told who dropped it, before the move.
		await NotifyService.Notify(thing,
			string.Format(ErrorMessages.Notifications.DropsYou, dropper.Object().Name));

		await MoveService.EnterRoom(parser, content, destination, noMoveMsgs: false,
			dropper.Object().DBRef, cause);
	}

	/// <summary>
	/// PennMUSH <c>do_empty</c> (<c>src/move.c:796</c>): every item in a container passes through the
	/// emptier's hands, so each one runs the same get and drop triads <c>GET</c> and <c>DROP</c> run.
	/// </summary>
	/// <remarks>
	/// The locks are re-evaluated per item rather than once for the whole container, which is Penn's
	/// documented choice (<c>src/move.c:783-789</c>): a lock that counts what is left has to see each
	/// move.
	/// <para>
	/// DEVIATION: Penn walks <c>first_visible</c> (<c>src/predicat.c:292</c>), so an item the emptier
	/// cannot see is skipped. SharpMUSH walks the whole contents list — the visibility walk is
	/// <c>LookService</c>'s and has no shared seam yet.
	/// </para>
	/// </remarks>
	[SharpCommand(Name = "EMPTY", Switches = [], CommandLock = "(TYPE^PLAYER|TYPE^THING)&!FLAG^GAGGED",
		Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Empty(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var objectName = args["0"].Message!.ToPlainText();

		// move.c:809-812: an unmatchable name is noisy_match_result's refusal, not a usage line.
		if (string.IsNullOrWhiteSpace(objectName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var locateResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, LocateFlags.All);

		if (!locateResult.IsValid())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var objectToEmpty = locateResult.WithoutError().WithoutNone();

		// move.c:809: TYPE_THING | TYPE_PLAYER only.
		if (!objectToEmpty.IsThing && !objectToEmpty.IsPlayer)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantEmptyThatFromHere), executor);
			return CallState.Empty;
		}

		var executorLocation = await executor.Where();
		var containerLocation = await objectToEmpty.Where();
		var containerObject = containerLocation.WithExitOption();

		var heldByEmptier = containerLocation.Object().DBRef.Equals(executor.Object().DBRef);
		var besideEmptier = containerLocation.Object().DBRef.Equals(executorLocation.Object().DBRef);

		// move.c:816-819: the container has to be in the emptier's inventory or beside them.
		if (!heldByEmptier && !besideEmptier)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantEmptyThatFromHere), executor);
			return CallState.Empty;
		}

		var emptyingSelf = objectToEmpty.Object().DBRef.Equals(executor.Object().DBRef);
		var contents = await objectToEmpty.AsContainer.Content(Mediator).ToListAsync();
		var count = 0;

		foreach (var item in contents)
		{
			var itemObject = item.WithRoomOption();

			// move.c:822-823: exits are not dropped.
			if (itemObject.IsExit)
			{
				continue;
			}

			bool emptyOk;

			if (emptyingSelf)
			{
				// move.c:825-832, "empty me": nothing is taken, because the items are already in hand,
				// so only the drop half is gated. This is the one branch that consults the drop-in lock,
				// and it consults it against the EMPTIER.
				emptyOk = await LockService.Evaluate(LockType.Drop, itemObject, executor)
									&& await LockService.Evaluate(LockType.DropIn, containerObject, executor)
									&& (!containerLocation.IsRoom
											|| await LockService.Evaluate(LockType.Drop, containerObject, executor));
			}
			else if (await PermissionService.Controls(executor, objectToEmpty)
							 || (await objectToEmpty.HasFlag("ENTER_OK")
									 && await LockService.Evaluate(LockType.Enter, objectToEmpty, executor)))
			{
				// move.c:838-843: could_doit on the ITEM, reported as a fail_lock on the CONTAINER with a
				// null default — silent unless the container carries a FAILURE attribute of its own.
				if (!await LockService.Evaluate(LockType.Basic, itemObject, executor))
				{
					await DidItService.FailLock(parser, executor, objectToEmpty, LockType.Basic);
					continue;
				}

				// move.c:846-853: taking it into your own hands is enough on its own; dropping it where
				// the container stands needs the drop locks as well.
				emptyOk = heldByEmptier
									|| (await LockService.Evaluate(LockType.Drop, itemObject, executor)
											&& (!containerLocation.IsRoom
													|| await LockService.Evaluate(LockType.Drop, containerObject, executor)));
			}
			else
			{
				emptyOk = false;
			}

			if (!emptyOk)
			{
				continue;
			}

			count++;

			var itemName = itemObject.Object().Name;

			// move.c:864-878, the get half — skipped when the emptier IS the container.
			if (!emptyingSelf)
			{
				await NotifyService.Notify(objectToEmpty,
					string.Format(ErrorMessages.Notifications.WasTakenFromYou, itemName));
				await NotifyService.Notify(itemObject,
					string.Format(ErrorMessages.Notifications.TookYou, executor.Object().Name));

				var takeMove = await MoveService.EnterRoom(parser, item, executor.AsContainer,
					noMoveMsgs: false, executor.Object().DBRef, "empty");

				// A refused take leaves the item in the container, so it is not one of the objects the
				// tally at the end reports, and none of its triads describe anything that happened.
				if (takeMove.IsT1)
				{
					count--;
					await NotifyService.Notify(executor, takeMove.AsT1.Value, executor);
					continue;
				}

				// did_it_with(player, item, "SUCCESS", …, NOTHING, thing_loc, NOTHING, NA_INTER_HEAR)
				// (move.c:874-876). The 8th argument is `loc` and it is NOTHING, which real_did_it
				// resolves to the emptier's own room; the 9th is `env0`, and it is the CONTAINER'S
				// location rather than the container itself — where do_get puts the source container.
				await DidItService.DidIt(parser, new DidItRequest(
					Player: executor, Thing: itemObject,
					What: AttrSuccess,
					Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.YouTakeFrom,
						itemName, objectToEmpty.Object().Name)),
					OWhat: AttrOSuccess,
					ODef: string.Format(ErrorMessages.Notifications.TakesFrom,
						itemName, objectToEmpty.Object().Name),
					AWhat: AttrASuccess,
					Env0: containerLocation.Object().DBRef.ToString()));

				// move.c:877-878: the emptier's own receive triad, with the item in %0.
				await DidItService.DidIt(parser, new DidItRequest(
					Player: executor, Thing: executor,
					What: AttrReceive, OWhat: AttrOReceive, AWhat: AttrAReceive,
					Env0: itemObject.Object().DBRef.ToString()));
			}

			// move.c:881-903, the drop half — skipped when the container is already in the emptier's
			// inventory, because its items have nowhere further to go.
			if (!heldByEmptier)
			{
				await DropTo(parser, executor, itemObject, containerLocation, "empty");

				// did_it(player, item, "DROP", …, "ADROP", NOTHING) (move.c:901-902): `loc` NOTHING is
				// the emptier's own room.
				await DidItService.DidIt(parser, new DidItRequest(
					Player: executor, Thing: itemObject,
					What: AttrDrop,
					Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.YouDrop, itemName)),
					OWhat: AttrODrop,
					ODef: string.Format(ErrorMessages.Notifications.Drops, itemName),
					AWhat: AttrADrop));
			}
		}

		// move.c:906-911: the tally, printed whatever the count — nothing moved still reports zero.
		await NotifyService.Notify(executor,
			count == 1
				? string.Format(ErrorMessages.Notifications.RemovedOneObjectFrom, objectToEmpty.Object().Name)
				: string.Format(ErrorMessages.Notifications.RemovedObjectsFrom, count, objectToEmpty.Object().Name),
			executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "ENTER", Switches = [], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Enter(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// move.c:928: do_enter refuses a non-Mobile before it matches anything, silently. ENTER is
		// CB.Default, so @force and @trigger can run it with a room or an exit as executor; a room has
		// no location to read and neither can be moved by enter_room (move.c:243).
		if (!executor.IsPlayer && !executor.IsThing)
		{
			return CallState.Empty;
		}

		var args = parser.CurrentState.Arguments;
		var objectName = args["0"].Message!.ToPlainText();

		// move.c:930-931: MAT_ABSOLUTE is added to the match flags only for Hasprivs — God, Wizard or
		// Royalty, which is IsPriv here. Without it "#N" is not a name a mortal can enter by, so the
		// only things they can name are the ones the remaining scopes already reach.
		var hasPrivs = await executor.IsPriv();
		var matchFlags = hasPrivs ? LocateFlags.All : LocateFlags.All & ~LocateFlags.AbsoluteMatch;

		var locateResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, matchFlags);

		if (!locateResult.IsValid())
		{
			// LocateAndNotifyIfInvalid has already told the mover what went wrong.
			return CallState.Empty;
		}

		var objectToEnter = locateResult.WithoutError().WithoutNone();

		if (!objectToEnter.IsThing && !objectToEnter.IsPlayer)
		{
			await NotifyService.Notify(executor, "You can't enter that.", executor);
			return CallState.Empty;
		}

		var currentLocation = await executor.Where();

		// move.c:946-948: only privileged players may enter something remotely. Paired with the
		// absolute-match gate above, this is what keeps "enter #N" from being a free teleport for a
		// mortal who learned the dbref of an ENTER_OK thing on the other side of the game.
		var targetLocation = await objectToEnter.Where();

		if (!hasPrivs && !targetLocation.Object().DBRef.Equals(currentLocation.Object().DBRef))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		// move.c:952-955: one condition, one failure. The container must be ENTER_OK or controlled
		// AND pass its enter lock; anything else is the same fail_lock, defaulting to
		// "Permission denied.".
		var mayEnter =
			(await objectToEnter.HasFlag("ENTER_OK") || await PermissionService.Controls(executor, objectToEnter))
			&& await PermissionService.PassesLock(executor, objectToEnter, LockType.Enter);

		if (!mayEnter)
		{
			await DidItService.FailLock(parser, executor, objectToEnter, LockType.Enter,
				MarkupText.Plain(ErrorMessages.Notifications.PermissionDenied));
			return CallState.Empty;
		}

		// move.c:957-959: entering yourself is its own refusal, after the lock, with its own wording.
		if (objectToEnter.Object().DBRef.Equals(executor.Object().DBRef))
		{
			await NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.MustRemainBesideYourself), executor);
			return CallState.Empty;
		}

		// move.c:962: do_enter teleports rather than plain enter_room, so a container owned by
		// someone else strips the STICKY possessions the mover does not control.
		var moveResult = await MoveService.SafeTel(parser, executor.AsContent, objectToEnter.AsContainer,
			noMoveMsgs: false, executor.Object().DBRef, "enter");

		if (moveResult.IsT1)
		{
			await NotifyService.Notify(executor, moveResult.AsT1.Value, executor);
			return CallState.Empty;
		}

		// move.c:964-965: followers trail the leader only if the leader actually went somewhere.
		var newLocation = await executor.Where();

		if (!newLocation.Object().DBRef.Equals(currentLocation.Object().DBRef))
		{
			await FollowerCommand(parser, executor, currentLocation, "ENTER", objectToEnter.Object().DBRef);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "FOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Follow(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Follow whom?", executor);
			return CallState.Empty;
		}

		var targetName = args["0"].Message!.ToPlainText();

		var targetResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, targetName, LocateFlags.All);

		if (!targetResult.IsValid())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var target = targetResult.WithoutError().WithoutNone();

		if (target.Object().DBRef.Equals(executor.Object().DBRef))
		{
			await NotifyService.Notify(executor, "You can't follow yourself.", executor);
			return CallState.Empty;
		}

		if (!target.IsPlayer && !target.IsThing)
		{
			await NotifyService.Notify(executor, "You can't follow that.", executor);
			return CallState.Empty;
		}

		// PennMUSH add_follow (move.c:1246) writes both halves: FOLLOWING on the follower and
		// FOLLOWERS on the leader. SharpMUSH's FOLLOWING holds one leader, so switching leaders has
		// to come off the previous one's list first or the two lists drift apart.
		var previousLeader = await LeaderOfAsync(executor);

		var followSet = await SetFollowingAsync(executor, target);
		if (followSet.IsT1)
		{
			await NotifyService.Notify(executor, followSet.AsT1.Value, executor);
			return CallState.Empty;
		}

		if (previousLeader is not null && !previousLeader.Object().DBRef.Equals(target.Object().DBRef))
		{
			await RemoveFollowerAsync(previousLeader, executor);
		}

		await AddFollowerAsync(target, executor);

		await NotifyService.Notify(executor, $"You are now following {target.Object().Name}.", executor);
		await NotifyService.Notify(target, $"{executor.Object().Name} is now following you.");

		return CallState.Empty;
	}

	[SharpCommand(Name = "GET", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 1, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Get(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var fullArg = args["0"].Message!.ToPlainText();

		if (string.IsNullOrWhiteSpace(fullArg))
		{
			await NotifyService.Notify(executor, "Get what?", executor);
			return CallState.Empty;
		}

		string objectName;
		AnySharpContainer sourceLocation;

		var possessiveIndex = fullArg.IndexOf("'s ", StringComparison.OrdinalIgnoreCase);
		if (possessiveIndex == -1)
		{
			possessiveIndex = fullArg.IndexOf("'S ", StringComparison.Ordinal);
		}

		if (possessiveIndex > 0)
		{
			var containerName = fullArg[..possessiveIndex].Trim();
			objectName = fullArg[(possessiveIndex + 3)..].Trim();

			var containerResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, containerName, LocateFlags.All);

			if (!containerResult.IsValid() || (!containerResult.IsPlayer && !containerResult.IsThing))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
				return CallState.Empty;
			}

			var container = containerResult.WithoutError().WithoutNone();

			if (!await container.HasFlag("ENTER_OK") && !await PermissionService.Controls(executor, container))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return CallState.Empty;
			}

			sourceLocation = container.AsContainer;
		}
		else
		{
			objectName = fullArg;
			sourceLocation = await executor.Where();
		}

		var locateResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, sourceLocation.WithExitOption(), objectName, LocateFlags.All);

		if (!locateResult.IsValid() || locateResult.IsRoom || locateResult.IsExit)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var objectToGet = locateResult.WithoutError().WithoutNone();

		var objectLocation = await objectToGet.Where();

		var alreadyCarrying = objectLocation.Match(
			player => player.Object.DBRef.Equals(executor.Object().DBRef),
			room => room.Object.DBRef.Equals(executor.Object().DBRef),
			thing => thing.Object.DBRef.Equals(executor.Object().DBRef));

		if (alreadyCarrying)
		{
			await NotifyService.Notify(executor, "You already have that.", executor);
			return CallState.Empty;
		}

		// The take lock is evaluated and failed against the object's own location, not against the
		// item and not against whatever container the player named: `box = Location(thing)`
		// (move.c:615) on the possessive path and `oldloc = Location(thing)` (move.c:649) on the
		// plain one, then `eval_lock_with(player, oldloc, Take_Lock, pe_info)` and
		// `fail_lock(player, oldloc, Take_Lock, ...)` (move.c:670-673). Aiming it at the item would
		// look for TAKE_LOCK`FAILURE on the item, so a container's own take-failure message and
		// action would never run. On the plain path it is also checked BEFORE the item's own basic
		// lock (move.c:668-675); the possessive path reports both as one failure, below.
		var takeSource = objectLocation.WithExitOption();
		var isPossessiveGet = possessiveIndex > 0;

		if (isPossessiveGet)
		{
			// The possessive path folds both locks into one `if` and has a single `else`
			// (move.c:635-642), so every refusal — the item's own basic lock or the container's take
			// lock — reports as `fail_lock(player, thing, Basic_Lock, "You can't take that from
			// there.")`: the FAILURE family on the *item*, carrying the take lock's text.
			var canSteal = await LockService.Evaluate(LockType.Basic, objectToGet, executor)
										 && await LockService.Evaluate(LockType.Take, takeSource, executor);

			if (!canSteal)
			{
				await DidItService.FailLock(parser, executor, objectToGet, LockType.Basic,
					MarkupText.Plain(ErrorMessages.Notifications.CantTakeThatFromThere));
				return CallState.Empty;
			}
		}
		else
		{
			if (!await LockService.Evaluate(LockType.Take, takeSource, executor))
			{
				await DidItService.FailLock(parser, executor, takeSource, LockType.Take,
					MarkupText.Plain(ErrorMessages.Notifications.CantTakeThatFromThere));
				return CallState.Empty;
			}

			if (!await LockService.Evaluate(LockType.Basic, objectToGet, executor))
			{
				await DidItService.FailLock(parser, executor, objectToGet, LockType.Basic,
					MarkupText.Plain(ErrorMessages.Notifications.CantPickThatUp));
				return CallState.Empty;
			}
		}

		var executorContainer = executor.AsContainer;
		var contentToGet = objectToGet.AsContent;
		var takenName = objectToGet.Object().Name;

		if (isPossessiveGet)
		{
			// move.c:627 — the robbed container hears about it too.
			await NotifyService.Notify(objectLocation.WithExitOption(),
				string.Format(ErrorMessages.Notifications.WasTakenFromYou, takenName));
		}

		await NotifyService.Notify(objectToGet,
			string.Format(ErrorMessages.Notifications.TookYou, executor.Object().Name));

		await MoveService.MoveIt(parser, contentToGet, executorContainer, noMoveMsgs: false,
			executor.Object().DBRef, "get");

		// did_it_with(player, thing, "SUCCESS", …, "OSUCCESS", …, "ASUCCESS", NOTHING, box, NOTHING, 0)
		// (move.c:634-636 possessive, :685-686 plain). The 8th argument is `loc` and the 9th is
		// `env0`: `loc` is NOTHING, which real_did_it resolves to Location(player) (predicat.c:230),
		// so the o-message audience is the taker's own room; the source container rides in %0. The
		// final `flags` is 0 on both get paths, so this o-message consults no interaction lock —
		// unlike the RECEIVE triad below, which Penn gives NA_INTER_HEAR.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: objectToGet,
			What: AttrSuccess,
			Def: MarkupText.Plain(isPossessiveGet
				? string.Format(ErrorMessages.Notifications.YouTakeFrom, takenName, objectLocation.Object().Name)
				: string.Format(ErrorMessages.Notifications.YouTake, takenName)),
			OWhat: AttrOSuccess,
			ODef: isPossessiveGet
				? string.Format(ErrorMessages.Notifications.TakesFrom, takenName, objectLocation.Object().Name)
				: string.Format(ErrorMessages.Notifications.Takes, takenName),
			AWhat: AttrASuccess,
			Env0: objectLocation.Object().DBRef.ToString(),
			Interact: IPermissionService.InteractType.None));

		// did_it_with(player, player, "RECEIVE", NULL, "ORECEIVE", NULL, "ARECEIVE", NOTHING, thing,
		// NOTHING, NA_INTER_HEAR, AN_MOVE) (move.c:637-639, :687-689): the taker's own receive triad.
		// `loc` is NOTHING — the room the taker is in — and the taken object is %0.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: executor,
			What: AttrReceive, OWhat: AttrOReceive, AWhat: AttrAReceive,
			Env0: objectToGet.Object().DBRef.ToString()));

		return CallState.Empty;
	}

	[SharpCommand(Name = "GIVE", Switches = ["SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 0, ParameterNames = ["player", "amount"])]
	public async ValueTask<Option<CallState>> Give(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var recipientName = args["0"].Message!.ToPlainText();
		var thingToGive = args["1"].Message!.ToPlainText();

		if (string.IsNullOrWhiteSpace(recipientName))
		{
			await NotifyService.Notify(executor, "Give to whom?", executor);
			return CallState.Empty;
		}

		if (string.IsNullOrWhiteSpace(thingToGive))
		{
			await NotifyService.Notify(executor, "Give what?", executor);
			return CallState.Empty;
		}

		var recipientResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, recipientName, LocateFlags.All);

		if (!recipientResult.IsValid() || recipientResult.IsRoom || recipientResult.IsExit)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var recipient = recipientResult.WithoutError().WithoutNone();

		if (!recipient.IsPlayer && !recipient.IsThing)
		{
			await NotifyService.Notify(executor, "You can't give things to that.", executor);
			return CallState.Empty;
		}

		// /SILENT belongs to this branch alone: it hushes the recipient's penny message
		// (rob.c:483) and has no bearing on the object form's triads, which rob.c fires
		// unconditionally.
		if (int.TryParse(thingToGive, out _))
		{
			await NotifyService.Notify(executor, "Money transfer will not be implemented.", executor);
			return CallState.Empty;
		}

		var objectResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, thingToGive, LocateFlags.All);

		if (!objectResult.IsValid() || objectResult.IsRoom || objectResult.IsExit)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontHaveThat), executor);
			return CallState.Empty;
		}

		var objectToGive = objectResult.WithoutError().WithoutNone();

		var objectLocation = await objectToGive.Where();

		var isCarrying = objectLocation.Match(
			player => player.Object.DBRef.Equals(executor.Object().DBRef),
			room => room.Object.DBRef.Equals(executor.Object().DBRef),
			thing => thing.Object.DBRef.Equals(executor.Object().DBRef));

		if (!isCarrying)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontHaveThat), executor);
			return CallState.Empty;
		}

		// rob.c:325-343 orders these give lock, from lock, receive lock, and only then the
		// ENTER_OK/controls gate. Of the three, only the give lock is a fail_lock — the other two
		// report a plain message and trigger nothing on the recipient.
		if (!await LockService.Evaluate(LockType.Give, objectToGive, executor))
		{
			await DidItService.FailLock(parser, executor, objectToGive, LockType.Give,
				MarkupText.Plain(ErrorMessages.Notifications.CantGiveThatAway));
			return CallState.Empty;
		}

		if (!await LockService.Evaluate(LockType.From, recipient, executor))
		{
			await NotifyService.Notify(executor,
				string.Format(ErrorMessages.Notifications.DoesntWantAnythingFromYou, recipient.Object().Name), executor);
			return CallState.Empty;
		}

		// The receive lock is evaluated with the OBJECT as the one being tested, not the giver
		// (rob.c:337).
		if (!await LockService.Evaluate(LockType.Receive, recipient, objectToGive))
		{
			await NotifyService.Notify(executor,
				string.Format(ErrorMessages.Notifications.DoesntWantThat, recipient.Object().Name), executor);
			return CallState.Empty;
		}

		if (!await recipient.HasFlag("ENTER_OK") && !await PermissionService.Controls(executor, recipient))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return CallState.Empty;
		}

		var recipientContainer = recipient.AsContainer;
		if (await MoveService.WouldCreateLoop(objectToGive.AsContent, recipientContainer))
		{
			await NotifyService.Notify(executor, "You can't give that - it would create a containment loop.", executor);
			return CallState.Empty;
		}

		// rob.c:346 hands the gift to moveto, and moveto IS enter_room (move.c:53-56): a gift changes
		// hands through the same pipeline every other move uses, and fires the same move triads.
		var contentToGive = objectToGive.AsContent;
		var giveMove = await MoveService.EnterRoom(parser, contentToGive, recipientContainer,
			noMoveMsgs: false, executor.Object().DBRef, "give");

		// A refused move leaves the gift where it was, so none of the triads below describe anything
		// that happened. rob.c has no analogue because moveto cannot fail there.
		if (giveMove.IsT1)
		{
			await NotifyService.Notify(executor, giveMove.AsT1.Value, executor);
			return CallState.Empty;
		}

		var giverName = executor.Object().Name;
		var giftName = objectToGive.Object().Name;
		var recipientDisplayName = recipient.Object().Name;

		// rob.c:357-358. GIVE/OGIVE/AGIVE live on the GIVER, not on the gift: did_it_with's `thing`
		// argument here is `player`. %0 is the gift and %1 the recipient.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: executor,
			What: AttrGive,
			Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.YouGaveTo, giftName, recipientDisplayName)),
			OWhat: AttrOGive,
			AWhat: AttrAGive,
			Env0: objectToGive.Object().DBRef.ToString(),
			Env1: recipient.Object().DBRef.ToString(),
			Interact: IPermissionService.InteractType.See));

		// rob.c:361 — the gift is told what happened to it.
		await NotifyService.Notify(objectToGive,
			string.Format(ErrorMessages.Notifications.GaveYouTo, giverName, recipientDisplayName));

		// rob.c:364-365: the GIFT's success triad, fired with the RECIPIENT as the enactor.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: recipient, Thing: objectToGive,
			What: AttrSuccess, OWhat: AttrOSuccess, AWhat: AttrASuccess));

		// rob.c:369-370: RECEIVE/ORECEIVE/ARECEIVE live on the RECIPIENT and run with the recipient
		// as the enactor, so a recipient who cannot see the giver still gets their own message.
		// %0 is the gift and %1 the giver.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: recipient, Thing: recipient,
			What: AttrReceive,
			Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.GaveYou, giverName, giftName)),
			OWhat: AttrOReceive,
			AWhat: AttrAReceive,
			Env0: objectToGive.Object().DBRef.ToString(),
			Env1: executor.Object().DBRef.ToString(),
			Interact: IPermissionService.InteractType.See));

		return CallState.Empty;
	}

	[SharpCommand(Name = "HOME", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Home(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// move.c:402-404: !Mobile, no home, a home the mover is carrying, and being its own home are
		// one refusal — "Bad destination.".
		if (!executor.IsPlayer && !executor.IsThing)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BadDestination), executor);
			return CallState.Empty;
		}

		// Guarded above: only players and things reach here, and both always have a home.
		var homeLocation = (await executor.MinusRoom().Home()).WithoutNone();
		var homeObj = homeLocation.Object();

		if (homeObj.DBRef.Number < 0
				|| homeObj.DBRef.Equals(executor.Object().DBRef)
				|| await MoveService.WouldCreateLoop(executor.AsContent, homeLocation))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BadDestination), executor);
			return CallState.Empty;
		}

		var currentLocation = await executor.Where();

		// move.c:407-412: neither the mover nor the room it stands in may be Dark for the room to be
		// told.
		if (!await executor.IsDark() && !await currentLocation.WithExitOption().IsDark())
		{
			await CommunicationService.SendToRoomAsync(
				executor,
				currentLocation,
				_ => MarkupText.Plain(string.Format(ErrorMessages.Notifications.GoesHomeFormat, executor.Object().Name)),
				INotifyService.NotificationType.Emit,
				excludeObjects: [executor],
				interact: IPermissionService.InteractType.See);
		}

		// PennMUSH sends all three (move.c:415-417); that is not a transcription slip.
		for (var i = 0; i < 3; i++)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoPlaceLikeHome), executor);
		}

		// move.c:418. safe_tel steals the possessions the mover does not control, and the automatic
		// look it reaches through enter_room is the only one the command needs.
		var moveResult = await MoveService.SafeTel(parser, executor.AsContent, homeLocation,
			noMoveMsgs: false, executor.Object().DBRef, "home");

		if (moveResult.IsT1)
		{
			await NotifyService.Notify(executor, moveResult.AsT1.Value, executor);
			return CallState.Empty;
		}

		return new CallState(homeObj.DBRef.ToString());
	}

	[SharpCommand(Name = "INVENTORY", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Inventory(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!executor.IsPlayer && !executor.IsThing)
		{
			await NotifyService.Notify(executor, "You can't carry anything.", executor);
			return CallState.Empty;
		}

		var container = executor.AsContainer;
		var contents = container.Content(Mediator);

		// PennMUSH: own inventory always shows Name(#dbrefFlags)
		var items = await contents
			.Select((AnySharpContent item, CancellationToken _) => MessageFormatting.FormatObjectWithDbref(item.Object()))
			.ToListAsync();

		if (items.Count == 0)
		{
			await NotifyService.Notify(executor, "You aren't carrying anything.", executor);
		}
		else
		{
			await NotifyService.Notify(executor, "You are carrying:", executor);
			foreach (var itemName in items)
			{
				await NotifyService.Notify(executor, itemName, executor);
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "LEAVE", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Leave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!executor.IsPlayer && !executor.IsThing)
		{
			await NotifyService.Notify(executor, "Only players and things can leave.", executor);
			return CallState.Empty;
		}

		var currentLocation = await executor.Where();
		var container = currentLocation.WithExitOption();

		var destinationLocation = await currentLocation.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async room => await ValueTask.FromResult<AnySharpContainer>(room),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));

		// move.c:981-983: standing in a room, a NO_LEAVE container, or one whose leave lock refuses,
		// are one and the same refusal — fail_lock on the container, defaulting to "You can't leave.".
		if (currentLocation.IsRoom
				|| await container.HasFlag("NO_LEAVE")
				|| !await PermissionService.PassesLock(executor, container, LockType.Leave))
		{
			await DidItService.FailLock(parser, executor, container, LockType.Leave,
				MarkupText.Plain(ErrorMessages.Notifications.CantLeave));
			return CallState.Empty;
		}

		// move.c:986. EnterRoom carries the automatic look, so the command adds none of its own.
		var moveResult = await MoveService.EnterRoom(parser, executor.AsContent, destinationLocation,
			noMoveMsgs: false, executor.Object().DBRef, "leave");

		if (moveResult.IsT1)
		{
			await NotifyService.Notify(executor, moveResult.AsT1.Value, executor);
			return CallState.Empty;
		}

		// move.c:987-988.
		var newLocation = await executor.Where();

		if (!newLocation.Object().DBRef.Equals(currentLocation.Object().DBRef))
		{
			await FollowerCommand(parser, executor, currentLocation, "leave", toward: null);
		}

		return new CallState(destinationLocation.Object().DBRef.ToString());
	}

	[SharpCommand(Name = "PAGE", Switches = ["LIST", "NOEVAL", "PORT", "OVERRIDE"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["player", "message"])]
	public async ValueTask<Option<CallState>> Page(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var isNoEval = parser.CurrentState.Switches.Contains("NOEVAL");
		var isOverride = parser.CurrentState.Switches.Contains("OVERRIDE");
		var isList = parser.CurrentState.Switches.Contains("LIST");
		if (isList)
		{
			var lastPagedAttr = await AttributeService.GetAttributeAsync(
				executor, executor, "LASTPAGED", IAttributeService.AttributeMode.Read, false);
			var lastPagedText = lastPagedAttr.Match(
				attr => attr.Last().Value.ToPlainText(),
				_ => string.Empty,
				_ => string.Empty);

			if (string.IsNullOrWhiteSpace(lastPagedText))
			{
				await NotifyService.Notify(executor, "You haven't paged anyone since connecting.", executor);
				return CallState.Empty;
			}

			var lastPagedNames = new List<string>();
			foreach (var recipientRef in lastPagedText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				if (!DBRef.TryParse(recipientRef, out var dbref))
				{
					continue;
				}

				var recipient = await Mediator.Send(new GetObjectNodeQuery(dbref!.Value));
				if (!recipient.IsNone)
				{
					lastPagedNames.Add(recipient.Known.Object().Name);
				}
			}

			if (lastPagedNames.Count == 0)
			{
				await NotifyService.Notify(executor, "I can't find who you last paged.", executor);
			}
			else
			{
				var recipientList = MessageFormatting.FormatWithOxfordComma(lastPagedNames);
				await NotifyService.Notify(executor, $"You last paged {recipientList}.", executor);
			}

			return CallState.Empty;
		}

		var recipientsArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);

		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MarkupText.Empty);

		string recipientsText;

		// If no recipients are provided, use the last successful page targets.
		if (string.IsNullOrWhiteSpace(recipientsArg.ToPlainText()) &&
			!string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			var lastPagedAttr = await AttributeService.GetAttributeAsync(
				executor, executor, "LASTPAGED", IAttributeService.AttributeMode.Read, false);
			recipientsText = lastPagedAttr.Match(
				attr => attr.Last().Value.ToPlainText(),
				_ => string.Empty,
				_ => string.Empty
			);

			if (string.IsNullOrWhiteSpace(recipientsText))
			{
				await NotifyService.Notify(executor, "Who do you want to page?", executor);
				return CallState.Empty;
			}
		}
		else
		{
			recipientsText = recipientsArg.ToPlainText();
		}

		if (string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			await NotifyService.Notify(executor, "What do you want to page?", executor);
			return CallState.Empty;
		}

		var pageType = messageArg.ToPlainText()[0] switch
		{
			':' => PageMessageType.Pose,
			';' => PageMessageType.SemiPose,
			_ => PageMessageType.Speech
		};
		var message = pageType == PageMessageType.Speech
			? messageArg
			: messageArg.Substring(1);

		var recipientNames = recipientsText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var successfulRecipients = new List<AnySharpObject>();

		foreach (var recipientName in recipientNames)
		{
			var recipientResult = await LocateService.LocateAndNotifyIfInvalidWithCallState(
				parser, executor, executor, recipientName, LocateFlags.All);

			if (!recipientResult.IsAnySharpObject)
			{
				continue;
			}

			var recipient = recipientResult.AsSharpObject;

			if (!isOverride)
			{
				var recipientFlags = recipient.Object().Flags.Value;
				if (await recipientFlags.AnyAsync(f => f.Name.Equals("HAVEN", StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService.Notify(executor, $"{recipient.Object().Name} is not accepting pages.", executor);
					continue;
				}
			}

			if (!isOverride)
			{
				// The interaction filter is its own gate and carries no failure triad: `fails_lock` at
				// speech.c:924-925 is `eval_lock_with(executor, target, Page_Lock, pe_info)` alone, and
				// only it reaches the fail_lock at :948.
				if (!await PermissionService.CanInteract(executor, recipient,
							IPermissionService.InteractType.Hear | IPermissionService.InteractType.Page))
				{
					await NotifyService.Notify(executor,
						string.Format(ErrorMessages.Notifications.NotAcceptingYourPages, recipient.Object().Name),
						executor);

					continue;
				}

				if (!await LockService.Evaluate(LockType.Page, recipient, executor))
				{
					// speech.c:944-948: the pager is told, and then
					// fail_lock(executor, target, Page_Lock, NULL, NOTHING). The Page lock is not in
					// lock_msgs, so its failure attributes are the derived PAGE_LOCK`FAILURE /
					// `OFAILURE / `AFAILURE (lock.c:861-870) that LockMessages.FailureAttributes
					// builds, and FailLock evaluates them as the recipient. No default: Penn passes
					// NULL.
					await NotifyService.Notify(executor,
						string.Format(ErrorMessages.Notifications.NotAcceptingYourPages, recipient.Object().Name),
						executor);

					await DidItService.FailLock(parser, executor, recipient, LockType.Page);

					continue;
				}
			}

			successfulRecipients.Add(recipient);
		}

		if (successfulRecipients.Count > 0)
		{
			var recipientList = MessageFormatting.FormatWithOxfordComma(
				successfulRecipients.Select(r => r.Object().Name).ToArray());
			var recipientRefs = string.Join(" ",
				successfulRecipients.Select(r => $"#{r.Object().DBRef.Number}"));
			var pageAlias = executor.IsPlayer
				? executor.AsPlayer.Aliases?.FirstOrDefault() ?? string.Empty
				: string.Empty;
			var senderName = Configuration.CurrentValue.Cosmetic.PageAliases && !string.IsNullOrEmpty(pageAlias)
				? $"{executor.Object().Name} ({pageAlias})"
				: executor.Object().Name;
			var recipientSuffix = successfulRecipients.Count > 1 ? $" (to {recipientList})" : string.Empty;

			var incomingDefault = pageType switch
			{
				PageMessageType.Speech => MarkupText.Concat([
					MarkupText.Plain(successfulRecipients.Count > 1
						? $"{senderName} pages {recipientList}: "
						: $"{senderName} pages: "),
					message
				]),
				PageMessageType.Pose => MarkupText.Concat([
					MarkupText.Plain($"From afar{recipientSuffix}, {senderName} "),
					message
				]),
				_ => MarkupText.Concat([
					MarkupText.Plain($"From afar{recipientSuffix}, {senderName}"),
					message
				])
			};
			var outgoingDefault = pageType switch
			{
				PageMessageType.Speech => MarkupText.Concat([
					MarkupText.Plain($"You paged {recipientList} with '"),
					message,
					MarkupText.Plain("'")
				]),
				PageMessageType.Pose => MarkupText.Concat([
					MarkupText.Plain($"Long distance to {recipientList}: {executor.Object().Name} "),
					message
				]),
				_ => MarkupText.Concat([
					MarkupText.Plain($"Long distance to {recipientList}: {executor.Object().Name}"),
					message
				])
			};
			var pageTypeToken = pageType switch
			{
				PageMessageType.Pose => ":",
				PageMessageType.SemiPose => ";",
				_ => "\""
			};
			var lastPagedText = string.Join(" ", successfulRecipients.Select(r => r.Object().DBRef));
			var lastPagedResult = await AttributeService.SetAttributeAsync(
				await HelperFunctions.GetGod(Mediator), executor, "LASTPAGED", MarkupText.Plain(lastPagedText));
			if (lastPagedResult.IsT1)
			{
				await NotifyService.Notify(executor, lastPagedResult.AsT1.Value, executor);
				return CallState.Empty;
			}

			var outPageFormatArgs = PageFormatArguments(
				message, pageTypeToken, pageAlias, recipientRefs, outgoingDefault);
			var outgoing = await parser.With(
				state => state with
				{
					Executor = executor.Object().DBRef,
					Caller = executor.Object().DBRef,
					Enactor = executor.Object().DBRef
				},
				pageParser => AttributeHelpers.EvaluateFormatAttribute(
					AttributeService, pageParser, executor, executor, "OUTPAGEFORMAT",
					outPageFormatArgs, outgoingDefault, checkParents: true));
			await NotifyService.Notify(executor, outgoing, executor);

			foreach (var recipient in successfulRecipients)
			{
				var pageFormatArgs = PageFormatArguments(
					message, pageTypeToken, pageAlias, recipientRefs, incomingDefault);
				var incoming = await parser.With(
					state => state with
					{
						Executor = recipient.Object().DBRef,
						Caller = recipient.Object().DBRef,
						Enactor = executor.Object().DBRef
					},
					pageParser => AttributeHelpers.EvaluateFormatAttribute(
						AttributeService, pageParser, recipient, recipient, "PAGEFORMAT",
						pageFormatArgs, incomingDefault, checkParents: true));
				await NotifyService.Notify(recipient, incoming, executor, INotifyService.NotificationType.Say);
			}
		}
		else if (recipientNames.Length > 0)
		{
			await NotifyService.Notify(executor, "No one to page.", executor);
		}

		return CallState.Empty;
	}

	private static Dictionary<string, CallState> PageFormatArguments(
		MString message, string pageType, string alias, string recipientRefs, MString defaultMessage) => new()
		{
			["0"] = new CallState(message),
			["1"] = new CallState(pageType),
			["2"] = new CallState(alias),
			["3"] = new CallState(recipientRefs),
			["4"] = new CallState(defaultMessage)
		};

	private enum PageMessageType
	{
		Speech,
		Pose,
		SemiPose
	}

	[SharpCommand(Name = "POSE", Switches = ["NOEVAL", "NOSPACE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 1, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Pose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var executorLocation = await executor.Where();
		var isNoSpace = parser.CurrentState.Switches.Contains("NOSPACE");
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);

		// Enforce Speech lock on the room (PennMUSH src/speech.c).
		if (!await LockService.Evaluate(LockType.Speech, executorLocation.WithExitOption(), executor))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MayNotSpeakHere), executor);
			return CallState.Empty;
		}

		var executorName = MarkupText.Plain(executor.Object().Name);
		var poseMessage = isNoSpace
			? MarkupText.Concat(executorName, message.Trim(global::MarkupString.TrimType.TrimStart, " "))
			: MarkupText.Concat([executorName, MarkupText.Space, message]);

		await CommunicationService.SendToRoomAsync(executor, executorLocation,
			_ => poseMessage,
			INotifyService.NotificationType.Pose);

		return new CallState(message);
	}

	[SharpCommand(Name = "SCORE", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Score(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		await NotifyService.Notify(executor, "The SCORE command is not supported.", executor);
		await NotifyService.Notify(executor, "SharpMUSH does not track money or pennies.", executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "SAY", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Say(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var executorLocation = await executor.Where();
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);

		// Enforce Speech lock on the room (PennMUSH src/speech.c).
		if (!await LockService.Evaluate(LockType.Speech, executorLocation.WithExitOption(), executor))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MayNotSpeakHere), executor);
			return CallState.Empty;
		}

		var executorName = MarkupText.Plain(executor.Object().Name);
		var youSayMessage = MarkupText.Concat([MarkupText.Plain("You say, \""), message, MarkupText.Plain("\"")]);
		var namesSaysMessage = MarkupText.Concat([executorName, MarkupText.Plain(" says, \""), message, MarkupText.Plain("\"")]);

		await NotifyService.Notify(executor, youSayMessage, executor, INotifyService.NotificationType.Say);

		await CommunicationService.SendToRoomAsync(executor, executorLocation,
			_ => namesSaysMessage,
			INotifyService.NotificationType.Say,
			excludeObjects: [executor]);

		return new CallState(message);
	}

	[SharpCommand(Name = "SEMIPOSE", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> SemiPose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var executorLocation = await executor.Where();
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);

		// Enforce Speech lock on the room (PennMUSH src/speech.c).
		if (!await LockService.Evaluate(LockType.Speech, executorLocation.WithExitOption(), executor))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MayNotSpeakHere), executor);
			return CallState.Empty;
		}

		var executorName = MarkupText.Plain(executor.Object().Name);
		var semiposeMessage = MarkupText.Concat(executorName, message);

		await CommunicationService.SendToRoomAsync(executor, executorLocation,
			_ => semiposeMessage,
			INotifyService.NotificationType.SemiPose);

		return new CallState(message);
	}

	[SharpCommand(Name = "TEACH", Switches = ["LIST"], Behavior = CB.Default | CB.NoParse, MinArgs = 1, MaxArgs = 1, ParameterNames = ["player", "attribute"])]
	public async ValueTask<Option<CallState>> Teach(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;

		if (switches.Contains("LIST"))
		{
			if (!args.ContainsKey("0"))
			{
				await NotifyService.Notify(executor, "Teach what action list?", executor);
				return CallState.Empty;
			}

			var actionList = args["0"].Message!.ToPlainText();

			var executorLocation = await executor.Where();
			await CommunicationService.SendToRoomAsync(executor, executorLocation,
				_ => MarkupText.Plain($"{executor.Object().Name} types --> {actionList}"),
				INotifyService.NotificationType.Emit);

			await parser.CommandListParse(MarkupText.Plain(actionList));

			return CallState.Empty;
		}

		if (!args.ContainsKey("0"))
		{
			await NotifyService.Notify(executor, "Teach what?", executor);
			return CallState.Empty;
		}

		var command = args["0"].Message!.ToPlainText();

		var location = await executor.Where();
		await CommunicationService.SendToRoomAsync(executor, location,
			_ => MarkupText.Plain($"{executor.Object().Name} types --> {command}"),
			INotifyService.NotificationType.Emit);

		await parser.CommandParse(MarkupText.Plain(command));

		return CallState.Empty;
	}

	[SharpCommand(Name = "UNFOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> UnFollow(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var followingAttr = await AttributeService.GetAttributeAsync(executor, executor, AttrFollowing,
			IAttributeService.AttributeMode.Read, false);

		if (followingAttr.IsNone || followingAttr.IsError)
		{
			await NotifyService.Notify(executor, "You aren't following anyone.", executor);
			return CallState.Empty;
		}

		// del_follow removes from both lists (move.c:1292).
		var unfollowed = await StopFollowingAsync(executor);
		if (unfollowed.IsT1)
		{
			await NotifyService.Notify(executor, unfollowed.AsT1.Value, executor);
			return CallState.Empty;
		}

		await NotifyService.Notify(executor, "You stop following.", executor);
		return CallState.Empty;
	}

	[SharpCommand(Name = "USE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Use(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Use what?", executor);
			return CallState.Empty;
		}

		var objectName = args["0"].Message!.ToPlainText();

		var locateResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, objectName, LocateFlags.All);

		if (!locateResult.IsValid())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var objectToUse = locateResult.WithoutError().WithoutNone();

		// fail_lock(player, thing, Use_Lock, T("Permission denied."), NOTHING) (set.c:1413): the use
		// lock fails on the thing being used, and its failure attributes are UFAIL/OUFAIL/AUFAIL
		// through lock_msgs (lock.c:102).
		if (!await LockService.Evaluate(LockType.Use, objectToUse, executor))
		{
			await DidItService.FailLock(parser, executor, objectToUse, LockType.Use,
				MarkupText.Plain(ErrorMessages.Notifications.PermissionDenied));
			return CallState.Empty;
		}

		// did_it(player, thing, "USE", T("Used."), "OUSE", NULL, "AUSE", NOTHING, AN_SYS)
		// (set.c:1416-1417). PennMUSH picks AUSE or RUNOUT there by charge_action (predicat.c:88),
		// which decrements a CHARGES attribute; SharpMUSH has no CHARGES at all, so there is nothing
		// yet to switch on and AUSE always runs.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: objectToUse,
			What: "USE",
			Def: MarkupText.Plain(ErrorMessages.Notifications.Used),
			OWhat: "OUSE",
			AWhat: "AUSE"));

		return CallState.Empty;
	}

	[SharpCommand(Name = "WHISPER", Switches = ["LIST", "NOISY", "SILENT", "NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["player", "message"])]
	public async ValueTask<Option<CallState>> Whisper(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var switches = parser.CurrentState.Switches;

		var executorLocation = await executor.Where();

		if (switches.Contains("LIST"))
		{
			var players = await executorLocation.Content(Mediator)
				.Where(obj => obj.IsPlayer && !obj.Object().DBRef.Equals(executor.Object().DBRef))
				.Select(obj => obj.Object().Name)
				.ToListAsync();

			if (players.Count == 0)
			{
				await NotifyService.Notify(executor, "There is no one here to whisper to.", executor);
			}
			else
			{
				await NotifyService.Notify(executor, $"You can whisper to: {string.Join(", ", players)}", executor);
			}

			return CallState.Empty;
		}

		var isNoEval = switches.Contains("NOEVAL");
		var targetArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);
		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MarkupText.Empty);

		if (string.IsNullOrWhiteSpace(targetArg.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Whisper to whom?", executor);
			return CallState.Empty;
		}

		if (string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Whisper what?", executor);
			return CallState.Empty;
		}

		var targetNames = targetArg.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var successfulTargets = new List<AnySharpObject>();

		foreach (var targetName in targetNames)
		{
			var targetResult = await LocateService.LocateAndNotifyIfInvalid(
				parser, executor, executorLocation.WithExitOption(), targetName, LocateFlags.All);

			if (!targetResult.IsValid() || !targetResult.IsPlayer)
			{
				await NotifyService.Notify(executor, $"I don't see {targetName} here.", executor);
				continue;
			}

			var target = targetResult.WithoutError().WithoutNone();

			if (target.Object().DBRef.Equals(executor.Object().DBRef))
			{
				await NotifyService.Notify(executor, "You can't whisper to yourself.", executor);
				continue;
			}

			var targetLocation = await target.Where();
			if (!targetLocation.Object().DBRef.Equals(executorLocation.Object().DBRef))
			{
				await NotifyService.Notify(executor, $"{target.Object().Name} is not here.", executor);
				continue;
			}

			successfulTargets.Add(target);
		}

		if (successfulTargets.Count == 0)
		{
			return CallState.Empty;
		}

		// PennMUSH cmd_whisper (src/cmds.c): `noisy = SW_ISSET(NOISY) || (!SW_ISSET(SILENT) &&
		// NOISY_WHISPER)`, and `noisy` governs ONLY whether the room may overhear. The whisperer's own
		// echo is unconditional — `whisper/silent X=hi` still says "You whisper, ..." to the whisperer.
		var isNoisy = switches.Contains("NOISY")
									|| (!switches.Contains("SILENT") && Configuration.CurrentValue.Command.NoisyWhisper);
		var messageText = messageArg.ToPlainText();

		// PennMUSH do_whisper (src/speech.c) reads the message type off the first character exactly as
		// do_pose does: ';' is a pose with no gap, ':' a pose with one, anything else plain speech.
		// The two kinds have completely different wording — the pose kind is "senses", not "whispers".
		var gap = messageText.StartsWith(';') ? string.Empty : " ";
		var isPose = messageText.StartsWith(':') || messageText.StartsWith(';');
		var body = isPose ? messageText[1..] : messageText;

		var targetList = MessageFormatting.FormatWithOxfordComma(
			[.. successfulTargets.Select(t => t.Object().Name)]);

		if (isPose)
		{
			var sensed = $"{executor.Object().Name}{gap}{body}";
			foreach (var target in successfulTargets)
			{
				await NotifyService.Notify(target, $"You sense: {sensed}", executor, INotifyService.NotificationType.Say);
			}

			var verb = successfulTargets.Count > 1 ? "sense" : "senses";
			await NotifyService.Notify(executor, $"{targetList} {verb}: {sensed}", executor);
		}
		else
		{
			var heading = successfulTargets.Count > 1
				? $"{executor.Object().Name} whispers to {targetList}"
				: $"{executor.Object().Name} whispers";
			foreach (var target in successfulTargets)
			{
				await NotifyService.Notify(target, $"{heading}: {body}", executor, INotifyService.NotificationType.Say);
			}

			await NotifyService.Notify(executor, $"You whisper, \"{body}\" to {targetList}.", executor);
		}

		if (isNoisy)
		{
			var contents = executorLocation.Content(Mediator);
			await foreach (var obj in contents)
			{
				if (obj.Object().DBRef.Equals(executor.Object().DBRef) ||
						successfulTargets.Any(t => t.Object().DBRef.Equals(obj.Object().DBRef)))
				{
					continue;
				}

				await NotifyService.Notify(obj.WithRoomOption(),
					$"{executor.Object().Name} whispers to {targetList}.");
			}
		}

		return new CallState(messageArg);
	}

	[SharpCommand(Name = "WITH", Switches = ["NOEVAL", "ROOM"], Behavior = CB.Player | CB.Thing | CB.EqSplit, MinArgs = 0,
		MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> With(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "With whom?", executor);
			return CallState.Empty;
		}

		if (!args.TryGetValue("1", out var arg1) || string.IsNullOrWhiteSpace(arg1.Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Do what with them?", executor);
			return CallState.Empty;
		}

		var targetName = args["0"].Message!.ToPlainText();
		var command = arg1.Message!;

		AnySharpObject searchLocation = switches.Contains("ROOM")
			? (await executor.Where()).WithExitOption()
			: executor;

		var targetResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, searchLocation, targetName, LocateFlags.All);

		if (!targetResult.IsValid())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var target = targetResult.WithoutError().WithoutNone();

		if (!target.IsPlayer && !target.IsThing)
		{
			await NotifyService.Notify(executor, "You can't do that with that.", executor);
			return CallState.Empty;
		}

		if (!await PermissionService.Controls(executor, target))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return CallState.Empty;
		}

		// Switch context: target becomes executor, original executor becomes enactor
		await parser.With(s => s with
		{
			Executor = target.Object().DBRef,
			Enactor = executor.Object().DBRef
		},
		async np => await np.CommandParse(command));

		return CallState.Empty;
	}

	[SharpCommand(Name = "DOING", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Doing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var isAdmin = await executor.IsWizard() ||
									await executor.IsRoyalty() ||
									await executor.IsSee_All();

		var pattern = args.ContainsKey("0") ? args["0"].Message?.ToPlainText() : null;

		var everyone = ConnectionService.GetAll();
		const string fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");

		var playerList = new List<string>();
		await foreach (var connection in everyone.Where(player => player.Ref.HasValue))
		{
			if (!isAdmin && connection.PresenceClass == PresenceClasses.Portal)
			{
				continue;
			}

			var obj = await Mediator.Send(new GetObjectNodeQuery(connection.Ref!.Value));
			var playerName = obj.Known.Object().Name;

			if (!isAdmin && await obj.Known.HasFlag("DARK"))
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(pattern) && !MatchesPattern(playerName, pattern))
			{
				continue;
			}

			var doingText = await GetDoingText(executor, obj.Known);

			playerList.Add(string.Format(
				fmt,
				playerName,
				TimeHelpers.TimeString(connection.Connected!.Value, accuracy: 3),
				TimeHelpers.TimeString(connection.Idle!.Value),
				doingText));
		}

		var footer = $"{playerList.Count} players logged in.";
		var message = $"{header}\n{string.Join('\n', playerList)}\n{footer}";

		await NotifyService.Notify(executor, message, executor);

		return new None();
	}

	private bool MatchesPattern(string playerName, string pattern)
	{
		if (pattern.Contains('*') || pattern.Contains('?'))
		{
			return MushText.IsWildcardMatch(MarkupText.Plain(playerName), pattern);
		}

		return playerName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
	}

	private async ValueTask<string> GetDoingText(AnySharpObject executor, AnySharpObject player)
	{
		var doingAttr = await AttributeService.GetAttributeAsync(
			executor,
			player,
			"DOING",
			mode: IAttributeService.AttributeMode.Read,
			parent: false);

		return doingAttr switch
		{
			{ IsError: true } or { IsNone: true } => string.Empty,
			_ => doingAttr.AsAttribute.Last().Value.ToPlainText()
		};
	}

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

	[SharpCommand(Name = "WARN_ON_MISSING", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> WarnOnMissing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Internal no-op command for warning system
		await ValueTask.CompletedTask;
		return new None();
	}

	[SharpCommand(Name = "UNIMPLEMENTED_COMMAND", Switches = [],
		Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> UnimplementedCommand(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownEnactorObject(Mediator);
		await NotifyService.Notify(executor, "Huh?  (Type \"help\" for help.)", executor);
		return new None();
	}
}
