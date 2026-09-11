using Mediator;
using System.Runtime.CompilerServices;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.ObjectModel;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

[Union]
public sealed class ChannelOrError : IUnion
{
	public ChannelOrError(SharpChannel value) => Value = value;
	public ChannelOrError(Error<CallState> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is ChannelOrError other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsError => Value is Error<CallState>;

	public SharpChannel AsChannel => Value as SharpChannel
		?? throw new InvalidOperationException($"Expected a channel, but the value is {Value?.GetType().Name ?? "null"}.");

	public Error<CallState> AsError => Value is Error<CallState> error
		? error
		: throw new InvalidOperationException($"Expected an error, but the value is {Value?.GetType().Name ?? "null"}.");
}

[Union]
public sealed class PrivilegeOrError : IUnion
{
	public PrivilegeOrError(string[] value) => Value = value;
	public PrivilegeOrError(Error<string[]> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is PrivilegeOrError other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsError => Value is Error<string[]>;

	public string[] AsPrivileges => Value as string[]
		?? throw new InvalidOperationException($"Expected privileges, but the value is {Value?.GetType().Name ?? "null"}.");

	public Error<string[]> AsError => Value is Error<string[]> error
		? error
		: throw new InvalidOperationException($"Expected an error, but the value is {Value?.GetType().Name ?? "null"}.");
}

public static class ChannelHelper
{
	/// <summary>
	/// PennMUSH <c>CHAN_NAME_LEN</c> (<c>hdrs/extchat.h:105</c>), which is 31 counting the terminator.
	/// It is a constant there rather than a configuration option, and <c>@channel/list</c>'s Name column
	/// is this wide because of it — <see cref="ChannelList"/> reads the same constant so the two cannot
	/// drift. <c>chan_title_len</c> bounds a member's TITLE and is a different setting.
	/// </summary>
	public const int MaxChannelNameLength = 30;

	/// <summary>
	/// Channel privilege names and their single-character abbreviations, matching PennMUSH's
	/// <c>priv_table</c> (<c>src/extchat.c:118</c>). The characters are case-sensitive: 'O' is Object
	/// and 'o' is Open.
	///
	/// <para>The ORDER is part of the contract, not an accident of how it was typed: PennMUSH's
	/// <c>privs_to_letters</c> and <c>privs_to_string</c> walk the table in order, so it is what
	/// <c>cflags()</c>, <c>clflags()</c>, <c>@channel/what</c> and <c>@channel/decompile</c> print.</para>
	/// </summary>
	private static readonly (string Name, char Letter)[] ChannelPrivilegeTable =
	[
		("Disabled", 'D'),
		("Player", 'P'),
		("Admin", 'A'),
		("Wizard", 'W'),
		("Object", 'O'),
		("Quiet", 'Q'),
		("Open", 'o'),
		("Hide_Ok", 'H'),
		("NoTitles", 'T'),
		("NoNames", 'N'),
		("NoCemit", 'C'),
		("Interact", 'I')
	];

	private static readonly ReadOnlyDictionary<string, char> ChannelPrivileges = new(
		ChannelPrivilegeTable.ToDictionary(x => x.Name, x => x.Letter, StringComparer.OrdinalIgnoreCase));

	/// <summary>
	/// PennMUSH <c>chanuser_priv</c> (<c>src/extchat.c:134</c>): the per-member flags, in the table's
	/// order, which is the order both <c>privs_to_letters</c> and <c>privs_to_string</c> emit them in.
	/// <c>Quiet</c> is the flag <c>@channel/mute</c> sets.
	/// </summary>
	private static readonly (string Name, char Letter)[] MemberPrivileges =
	[
		("Quiet", 'Q'),
		("Hide", 'H'),
		("Gag", 'G'),
		("Combine", 'C')
	];

	/// <summary>
	/// PennMUSH <c>privs_to_letters(priv_table, ChanType(c))</c> (<c>src/privtab.c:118</c>), which is what
	/// <c>cflags()</c> and <c>@channel/decompile</c> print: one character per privilege, in table order.
	/// </summary>
	public static string PrivilegeLetters(IEnumerable<string> privileges)
	{
		var set = privileges.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return string.Concat(ChannelPrivilegeTable
			.Where(x => set.Contains(x.Name))
			.Select(x => x.Letter));
	}

	/// <summary>
	/// PennMUSH <c>privs_to_string</c> (<c>src/privtab.c:96</c>): the same set spelled out and space
	/// separated, in the table's canonical casing. <c>clflags()</c> and <c>@channel/what</c> print this.
	/// </summary>
	public static string PrivilegeNames(IEnumerable<string> privileges)
	{
		var set = privileges.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return string.Join(" ", ChannelPrivilegeTable.Where(x => set.Contains(x.Name)).Select(x => x.Name));
	}

	/// <summary>
	/// PennMUSH <c>list_cuflags</c> (<c>src/extchat.c:2905</c>): a member's own channel flags, as letters
	/// or as names. <c>Hide</c> comes first in both spellings because Penn emits it before the table.
	/// </summary>
	public static string MemberFlags(SharpChannelStatus status, bool verbose)
	{
		var present = new List<(string Name, char Letter)>();

		if (status.Hide ?? false)
		{
			present.Add(MemberPrivileges[1]);
		}

		if (status.Mute ?? false)
		{
			present.Add(MemberPrivileges[0]);
		}

		if (status.Gagged ?? false)
		{
			present.Add(MemberPrivileges[2]);
		}

		if (status.Combine ?? false)
		{
			present.Add(MemberPrivileges[3]);
		}

		return verbose
			? string.Join(" ", present.Select(x => x.Name))
			: string.Concat(present.Select(x => x.Letter));
	}

	private static readonly ReadOnlyDictionary<char, string?> ChannelPrivilegesReverse =
		new(ChannelPrivileges.ToDictionary(x => x.Value, string? (x) => x.Key));

	public static async ValueTask<bool> IsMemberOfChannel(AnySharpObject member, SharpChannel channel)
		=> await channel.Members
			.Value
			.AnyAsync(x =>
				x.Member.Id() == member.Id()
				);

	public static async ValueTask<SharpChannel.MemberAndStatus?> ChannelMemberStatus(
		AnySharpObject member, SharpChannel channel) =>
		await channel.Members.Value.FirstOrDefaultAsync(x => x.Member.Id() == member.Id());

	/// <summary>
	/// PennMUSH <c>string_to_privs(table, str, origprivs)</c> (<c>src/privtab.c:36</c>): applies a
	/// space-separated privilege list to an existing set rather than replacing it, and honours a leading
	/// <c>!</c> as removal. An empty list leaves <paramref name="originalPrivileges"/> untouched.
	///
	/// <para>Names come back in the canonical casing of the <c>chan_privs</c> table, never in whatever
	/// casing the player typed, because the permission checks compare <c>Privs</c> ordinally.</para>
	///
	/// <para>Unlike PennMUSH this matches full names exactly (case-insensitively) rather than by prefix;
	/// single-character aliases are matched case-sensitively, as 'O' (Object) and 'o' (Open) differ.</para>
	/// </summary>
	public static PrivilegeOrError StringToChannelPrivileges(MString privileges, string[] originalPrivileges)
	{
		var tokens = privileges.ToPlainText()
			.Split(' ')
			.Where(x => !string.IsNullOrWhiteSpace(x))
			.ToArray();

		var badList = tokens
			.Select(TrimNegation)
			.Where(x => x.Length != 0 && ResolvePrivilege(x) is null)
			.ToArray();

		if (badList.Length != 0)
		{
			return new PrivilegeOrError(new Error<string[]>(badList));
		}

		var result = new List<string>(originalPrivileges
			.Select(x => ResolvePrivilege(x) ?? x)
			.Distinct(StringComparer.OrdinalIgnoreCase));

		foreach (var token in tokens)
		{
			var negated = token.StartsWith('!');
			var name = ResolvePrivilege(TrimNegation(token));

			if (name is null)
			{
				continue;
			}

			result.RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));

			if (!negated)
			{
				result.Add(name);
			}
		}

		return new PrivilegeOrError([.. result]);
	}

	private static string TrimNegation(string token)
		=> token.StartsWith('!') ? token[1..] : token;

	private static string? ResolvePrivilege(string token)
		=> token.Length switch
		{
			0 => null,
			1 => ChannelPrivilegesReverse.GetValueOrDefault(token[0]),
			_ => ChannelPrivileges.Keys.FirstOrDefault(x => x.Equals(token, StringComparison.OrdinalIgnoreCase))
		};

	public static bool IsValidChannelName(IOptionsWrapper<SharpMUSHOptions> Configuration, MString channelName)
		=> IsValidChannelName(Configuration, channelName.ToPlainText());

	/// <summary>
	/// PennMUSH <c>ok_channel_name</c> (<c>src/extchat.c:1855-1895</c>) minus its uniqueness check, which
	/// the storage layer owns: non-empty, no leading or trailing whitespace, printable characters only,
	/// no <c>|</c> (it separates the names in a combined connect announcement), and within the configured
	/// length.
	///
	/// <para>There is no minimum length: <c>OOC</c> and <c>RP</c> are legal names. The one deliberate
	/// divergence is the space — Penn permits them inside a channel name and this does not, because the
	/// <c>+&lt;channel&gt; &lt;message&gt;</c> token form splits on the first space and could never
	/// address such a channel.</para>
	/// </summary>
	public static bool IsValidChannelName(IOptionsWrapper<SharpMUSHOptions> Configuration, string channelName)
		=> channelName.Length != 0
			 && channelName.Length <= MaxChannelNameLength
			 && !channelName.Contains(' ')
			 && !channelName.Contains('|')
			 && channelName.All(x => !char.IsControl(x));

	/// <summary>
	/// Looks a channel up by exact name. This is PennMUSH's <c>find_channel()</c> stripped of both of its
	/// other jobs: it does not prefix-match and it does not decide who may see what it returns.
	///
	/// <para>Only the two call sites that genuinely need an unfiltered exact lookup use it — the
	/// uniqueness probe in <c>@channel/add</c> and the membership cleanup in <c>@delcom</c>. Everything a
	/// player names goes through <see cref="MatchChannel"/> or <see cref="GetVisibleChannelOrError"/>,
	/// which gate on visibility and accept an abbreviation.</para>
	/// </summary>
	public static async ValueTask<ChannelOrError> GetChannelOrError(IMediator mediator, MString channelName)
		=> await mediator.Send(new GetChannelQuery(NormalizeChannelName(channelName.ToPlainText()))) is { } channel
			? new ChannelOrError(channel)
			: new ChannelOrError(new Error<CallState>(new CallState(ErrorMessages.Returns.ChannelNotFound)));

	/// <summary>
	/// Whether <paramref name="viewer"/> may be told this channel exists at all.
	///
	/// <para>PennMUSH's <c>find_channel</c> (<c>src/extchat.c:959</c>) resolves a name only when
	/// <c>Chan_Can_See(chan, player) || onchannel(player, chan)</c>. The membership half matters on its own:
	/// <c>Chan_Can_See</c> requires a member to also pass <c>Chan_Can_Speak</c>, so without it a gagged or
	/// speak-locked member would be told their own channel does not exist.</para>
	/// </summary>
	public static async ValueTask<bool> CanSeeChannel(IPermissionService permissionService, AnySharpObject viewer,
		SharpChannel channel)
		=> await permissionService.ChannelCanSeeAsync(viewer, channel)
			 || await IsMemberOfChannel(viewer, channel);

	/// <summary>
	/// Which channels a name is allowed to resolve against. PennMUSH keeps three near-identical copies of
	/// its matcher for this — <c>find_channel</c>, <c>find_channel_partial_on</c> and
	/// <c>find_channel_partial_off</c> (<c>src/extchat.c:943-1160</c>) — and the distinction is not
	/// cosmetic: <c>@channel/on pub</c> must resolve against channels the joiner is NOT on, so that a
	/// second channel they already joined cannot make the abbreviation ambiguous, and so that the
	/// "you are already on" answer is reachable at all.
	/// </summary>
	public enum ChannelMatchScope
	{
		/// <summary>PennMUSH <c>find_channel</c>: every channel the viewer may see.</summary>
		Any,

		/// <summary>PennMUSH <c>find_channel_partial_on</c>: only channels the viewer is a member of.</summary>
		Member,

		/// <summary>PennMUSH <c>find_channel_partial_off</c>: only visible channels the viewer is NOT on.</summary>
		NonMember
	}

	/// <summary>PennMUSH's <c>cmatch_type</c> (<c>hdrs/extchat.h</c>).</summary>
	public enum ChannelMatchKind
	{
		None,
		Exact,
		Partial,
		Ambiguous
	}

	/// <summary>
	/// The outcome of resolving a channel name. <see cref="Candidates"/> is populated only for
	/// <see cref="ChannelMatchKind.Ambiguous"/>, where PennMUSH lists the possibilities
	/// (<c>list_partial_matches</c>, <c>src/extchat.c:1035</c>).
	/// </summary>
	public readonly record struct ChannelMatch(ChannelMatchKind Kind, SharpChannel? Channel, SharpChannel[] Candidates)
	{
		public bool Found => Channel is not null && Kind is ChannelMatchKind.Exact or ChannelMatchKind.Partial;

		public static ChannelMatch NoMatch { get; } = new(ChannelMatchKind.None, null, []);
	}

	/// <summary>
	/// PennMUSH <c>normalize_channel_name</c> (<c>src/extchat.c:905</c>): markup off — which
	/// <c>ToPlainText</c> has already done — and one layer of surrounding angle brackets off, so that the
	/// <c>&lt;Public&gt;</c> a player copies out of channel output resolves to <c>Public</c>.
	/// </summary>
	private static string NormalizeChannelName(string name)
		=> name.Length > 1 && name[0] == '<' && name[^1] == '>'
			? name[1..^1]
			: name;

	/// <summary>Whether a channel is a candidate for this scope at all.</summary>
	private static async ValueTask<bool> InScope(IPermissionService permissionService, AnySharpObject viewer,
		SharpChannel channel, ChannelMatchScope scope)
		=> scope switch
		{
			ChannelMatchScope.Member => await IsMemberOfChannel(viewer, channel),
			ChannelMatchScope.NonMember => !await IsMemberOfChannel(viewer, channel)
																		 && await permissionService.ChannelCanSeeAsync(viewer, channel),
			_ => true
		};

	/// <summary>
	/// Whether an in-scope candidate is one the viewer may be told about. Only
	/// <see cref="ChannelMatchScope.Any"/> still has a test left to make; the other two scopes already
	/// established visibility (or membership, which implies it) in <see cref="InScope"/>.
	/// </summary>
	private static async ValueTask<bool> VisibleInScope(IPermissionService permissionService, AnySharpObject viewer,
		SharpChannel channel, ChannelMatchScope scope)
		=> scope != ChannelMatchScope.Any || await CanSeeChannel(permissionService, viewer, channel);

	/// <summary>
	/// PennMUSH's channel matcher (<c>src/extchat.c:943-1160</c>): an exact, case-insensitive name match
	/// wins outright, and failing that the name is taken as an abbreviation and prefix-matched against
	/// every channel the viewer may see. One surviving candidate is the answer; several are
	/// <see cref="ChannelMatchKind.Ambiguous"/>. This is why <c>@channel/on pub</c> joins <c>Public</c>.
	///
	/// <para>An exact name match ends the search whether or not the viewer may see it, as Penn's
	/// <c>find_channel</c> does — a hidden <c>Wizards</c> channel therefore answers "I don't recognize
	/// that channel" rather than resolving to a visible <c>WizardsLounge</c> behind it.</para>
	///
	/// <para>The exact store lookup up front is a fast path and nothing more: it can only produce the same
	/// answer the scan would, since Penn returns on an exact match before considering any prefix. It keeps
	/// the common case — a player typing a channel's full name — from enumerating every channel's
	/// membership.</para>
	/// </summary>
	public static async ValueTask<ChannelMatch> MatchChannel(
		IPermissionService permissionService,
		IMediator mediator,
		AnySharpObject viewer,
		MString channelName,
		ChannelMatchScope scope = ChannelMatchScope.Any)
	{
		var name = NormalizeChannelName(channelName.ToPlainText());

		if (name.Length == 0)
		{
			return ChannelMatch.NoMatch;
		}

		if (await mediator.Send(new GetChannelQuery(name)) is { } stored
				&& stored.Name.ToPlainText().Equals(name, StringComparison.OrdinalIgnoreCase)
				&& await InScope(permissionService, viewer, stored, scope))
		{
			return await VisibleInScope(permissionService, viewer, stored, scope)
				? new ChannelMatch(ChannelMatchKind.Exact, stored, [])
				: ChannelMatch.NoMatch;
		}

		var candidates = new List<SharpChannel>();

		await foreach (var channel in mediator.CreateStream(new GetChannelListQuery()))
		{
			var candidateName = channel.Name.ToPlainText();

			// Name first: InScope reads the membership store, and there is no reason to pay for that on a
			// channel whose name cannot match either way.
			if (!candidateName.StartsWith(name, StringComparison.OrdinalIgnoreCase)
					|| !await InScope(permissionService, viewer, channel, scope))
			{
				continue;
			}

			if (candidateName.Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				return await VisibleInScope(permissionService, viewer, channel, scope)
					? new ChannelMatch(ChannelMatchKind.Exact, channel, [])
					: ChannelMatch.NoMatch;
			}

			if (await VisibleInScope(permissionService, viewer, channel, scope))
			{
				candidates.Add(channel);
			}
		}

		// Penn walks an alphabetically ordered channel list, so its "first match wins" is really
		// "alphabetically first"; neither store returns channels in any guaranteed order, so sort.
		var ordered = candidates
			.OrderBy(x => x.Name.ToPlainText(), StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return ordered.Length switch
		{
			0 => ChannelMatch.NoMatch,
			1 => new ChannelMatch(ChannelMatchKind.Partial, ordered[0], ordered),
			_ => new ChannelMatch(ChannelMatchKind.Ambiguous, null, ordered)
		};
	}

	/// <summary>A refusal carrying PennMUSH's <c>#-2 AMBIGUOUS CHANNEL NAME</c> (<c>extchat.h:171</c>).</summary>
	public static ChannelOrError AmbiguousChannel()
		=> new(new Error<CallState>(new CallState(ErrorMessages.Returns.AmbiguousChannelName)));

	/// <summary>
	/// A refusal carrying PennMUSH's <c>#-1 NO SUCH CHANNEL</c> (<c>extchat.h:167</c>), or
	/// <paramref name="returns"/> where the caller has a more specific answer to give — Penn's
	/// <c>channel_join_self</c> and <c>channel_leave_self</c> both refuse with a message about the
	/// channel the player named, having already established that it exists.
	/// </summary>
	public static ChannelOrError NoSuchChannel(string? returns = null)
		=> new(new Error<CallState>(new CallState(returns ?? ErrorMessages.Returns.NoSuchChannel)));

	/// <summary>
	/// PennMUSH <c>list_partial_matches</c> (<c>src/extchat.c:1035</c>): the possibilities an ambiguous
	/// abbreviation could have meant, appended to the header one space apart. Only channels that already
	/// passed the visibility gate reach here, so this cannot name a channel the viewer may not see.
	/// </summary>
	public static string PartialMatchList(IEnumerable<SharpChannel> candidates)
		=> ErrorMessages.Notifications.ChatPartialMatchesAre
			 + string.Concat(candidates.Select(x => $" {x.Name.ToPlainText()}"));

	/// <summary>
	/// Lookup plus PennMUSH's visibility gate, which is inside <c>find_channel</c> itself
	/// (<c>src/extchat.c:943-972</c>) and therefore applies to every command and function that resolves a
	/// channel by name.
	///
	/// <para><b>A channel that does not exist and a channel the viewer may not see produce the same
	/// notification AND the same return value, deliberately.</b> Anything else is an enumeration oracle:
	/// a caller who can tell "no such channel" from "not for you" can walk the channel list. PennMUSH
	/// agrees — <c>test_channel_fun</c> (<c>extchat.h:161</c>) answers a missing channel with
	/// "CHAT: I don't recognize that channel." and <c>#-1 NO SUCH CHANNEL</c>, and every
	/// <c>Chan_Can_See</c> refusal in <c>extchat.c</c> answers with exactly the same pair.</para>
	///
	/// <para>This repository has already made this decision once, for the same reason: PR #750 gave a
	/// missing scene and an invisible scene one identical answer, recorded at
	/// <c>SharpMUSH.Plugins.Scene/Web/SceneHub.cs:52-57</c> and <c>SceneLive.razor:120-124</c>. Any future
	/// edit that wants to explain a not-found here has to keep the two cases sharing an answer.</para>
	/// </summary>
	public static async ValueTask<ChannelOrError> GetVisibleChannelOrError(
		IPermissionService permissionService,
		IMediator mediator,
		INotifyService notifyService,
		AnySharpObject viewer,
		MString channelName,
		bool notify = false,
		ChannelMatchScope scope = ChannelMatchScope.Any)
	{
		var match = await MatchChannel(permissionService, mediator, viewer, channelName, scope);

		if (match.Found)
		{
			return new ChannelOrError(match.Channel!);
		}

		if (match.Kind == ChannelMatchKind.Ambiguous)
		{
			if (notify)
			{
				await notifyService.Notify(viewer, ErrorMessages.Notifications.DontKnowWhichChannel, viewer);
				await notifyService.Notify(viewer, PartialMatchList(match.Candidates), viewer);
			}

			return AmbiguousChannel();
		}

		if (notify)
		{
			await notifyService.Notify(viewer, ErrorMessages.Notifications.DontRecognizeThatChannel, viewer);
		}

		return NoSuchChannel();
	}

	/// <summary>
	/// The channels <paramref name="viewer"/> may be told exist, for the switches that operate on every
	/// channel at once. Resolving one channel by name goes through
	/// <see cref="GetVisibleChannelOrError"/>; enumerating them has to apply the same rule or
	/// <c>@channel/hide</c> with no argument becomes a way to list what that gate hides.
	/// </summary>
	public static async ValueTask<SharpChannel[]> VisibleChannels(IPermissionService permissionService,
		AnySharpObject viewer, IAsyncEnumerable<SharpChannel> channels)
	{
		var visible = new List<SharpChannel>();

		await foreach (var channel in channels)
		{
			if (await CanSeeChannel(permissionService, viewer, channel))
			{
				visible.Add(channel);
			}
		}

		return [.. visible];
	}

	/// <summary>
	/// PennMUSH <c>Chan_Ok_Type</c> (hdrs/extchat.h:196) with the refusal <c>src/extchat.c:1533</c>
	/// prints. Returns <see langword="null"/> when the object is of a type the channel accepts.
	/// </summary>
	public static string? WrongTypeRefusal(IPermissionService permissionService, AnySharpObject who,
		SharpChannel channel)
		=> permissionService.ChannelOkType(who, channel)
			? null
			: string.Format(ErrorMessages.Notifications.ChatWrongTypeForChannel, channel.Name.ToPlainText());

	/// <summary>
	/// The outcome of PennMUSH's join checks (<c>src/extchat.c:1241-1268</c> for a third party,
	/// <c>:1347-1362</c> for oneself). A wizard who fails only the join lock is warned and joined anyway,
	/// so a plain refusal string cannot express the result.
	/// </summary>
	public readonly record struct JoinCheck(string? Refusal, string? Warning)
	{
		public bool Refused => Refusal is not null;
	}

	/// <summary>
	/// PennMUSH <c>Chan_Ok_Type</c> then <c>Chan_Can_Join</c>, with the wizard override
	/// (<c>src/extchat.c:1261-1268</c>): a wizard actor who fails the check is warned rather than
	/// refused. The override is the ACTOR's privilege, not the victim's.
	/// </summary>
	public static async ValueTask<JoinCheck> JoinRefusal(IPermissionService permissionService,
		AnySharpObject actor, AnySharpObject victim, SharpChannel channel)
	{
		if (WrongTypeRefusal(permissionService, victim, channel) is not null)
		{
			return new JoinCheck(
				string.Format(ErrorMessages.Notifications.ChatWrongTypeOfThingForChannel, channel.Name.ToPlainText()),
				null);
		}

		if (await permissionService.ChannelCanJoin(victim, channel))
		{
			return new JoinCheck(null, null);
		}

		return await actor.IsWizard()
			? new JoinCheck(null, actor.Id() == victim.Id()
				? ErrorMessages.Notifications.ChatJoinOverrideSelf
				: ErrorMessages.Notifications.ChatJoinOverrideTarget)
			: new JoinCheck(ErrorMessages.Notifications.ChatJoinDenied, null);
	}

	/// <summary>
	/// PennMUSH <c>do_chat</c> (<c>src/extchat.c:1533-1546</c>): the type gate, then the speak gate that
	/// <c>LOUD</c> bypasses. A speaker who cannot even see the channel is told it does not exist rather
	/// than that they may not speak on it.
	/// </summary>
	public static async ValueTask<string?> SpeechRefusal(IPermissionService permissionService, AnySharpObject who,
		SharpChannel channel)
	{
		if (WrongTypeRefusal(permissionService, who, channel) is { } wrongType)
		{
			return wrongType;
		}

		if (await who.IsLoud() || await permissionService.ChannelCanSpeak(who, channel))
		{
			return null;
		}

		return await permissionService.ChannelCanSeeAsync(who, channel)
			? string.Format(ErrorMessages.Notifications.ChatNotAllowedToSpeak, channel.Name.ToPlainText())
			: ErrorMessages.Notifications.ChatNoSuchChannel;
	}

	/// <summary>
	/// The outcome of <c>do_cemit</c>'s permission checks (<c>src/extchat.c:1649-1665</c>).
	/// <paramref name="Overridden"/> reports the See_All + Pemit_All bypass, which the caller needs
	/// because the same bypass also skips the open-channel rule further down.
	/// </summary>
	public readonly record struct CemitCheck(string? Refusal, bool Overridden)
	{
		public bool Refused => Refusal is not null;
	}

	/// <summary>
	/// PennMUSH <c>do_cemit</c>'s gates (<c>src/extchat.c:1649-1665</c>): See_All + Pemit_All skips them
	/// entirely, since such a player could enumerate the channel's members and <c>@pemit</c> them anyway.
	/// Otherwise the type gate and <c>Chan_Can_Cemit</c> apply. Note <c>LOUD</c> does NOT bypass this —
	/// Penn only consults it in <c>do_chat</c>.
	/// </summary>
	public static async ValueTask<CemitCheck> CemitRefusal(IPermissionService permissionService,
		AnySharpObject who, SharpChannel channel)
	{
		if (await who.IsSee_All() && await who.HasPower("Pemit_All"))
		{
			return new CemitCheck(null, true);
		}

		if (WrongTypeRefusal(permissionService, who, channel) is { } wrongType)
		{
			return new CemitCheck(wrongType, false);
		}

		return new CemitCheck(
			await permissionService.ChannelCanCemit(who, channel)
				? null
				: string.Format(ErrorMessages.Notifications.ChatNotAllowedToCemit, channel.Name.ToPlainText()),
			false);
	}

	/// <summary>
	/// PennMUSH's "if the channel isn't open, you must hear it in order to speak"
	/// (<c>src/extchat.c:1553-1562</c> for <c>do_chat</c>, <c>:1667-1676</c> for <c>do_cemit</c>), which
	/// is the whole of what the <c>Open</c> privilege means. Returns <see langword="null"/> when the
	/// speaker may go ahead.
	/// </summary>
	public static string? OpenChannelRefusal(SharpChannel channel, SharpChannel.MemberAndStatus? membership)
		=> channel.Privs.Contains("Open", StringComparer.OrdinalIgnoreCase)
			? null
			: membership switch
			{
				null => ErrorMessages.Notifications.ChatMustBeOnChannelToSpeak,
				{ Status.Gagged: true } => ErrorMessages.Notifications.ChatMustStopGaggingToSpeak,
				_ => null
			};

	/// <summary>
	/// One member of a channel as <c>@channel/who</c> and <c>cwho()</c> see them: PennMUSH's
	/// <c>CHANUSER</c> plus whether the object behind it is presently reachable.
	/// </summary>
	public readonly record struct ChannelMember(AnySharpObject Object, SharpChannelStatus Status, bool Connected)
	{
		public bool IsThing => Object.IsThing;

		public bool Hidden => Status.Hide ?? false;

		public bool Gagging => Status.Gagged ?? false;

		/// <summary>
		/// PennMUSH <c>do_channel_who</c> (<c>src/extchat.c:2963</c>): a THING counts as present, a player
		/// only while connected, and either is withheld from a viewer without <c>Priv_Who</c> while hidden.
		///
		/// <para>Penn's <c>fun_cwho</c> (<c>:3068</c>) exempts a THING from the hide test, so its two
		/// listings disagree about a hidden object. This takes <c>do_channel_who</c>'s rule for both:
		/// <c>@channel/hide</c> means the same thing whoever set it.</para>
		/// </summary>
		public bool ListedAsOn(bool privilegedWho)
			=> (IsThing || Connected) && (!Hidden || privilegedWho);

		/// <summary>
		/// PennMUSH <c>fun_cwho</c>'s "off" arm (<c>src/extchat.c:3066</c>), which is the complement of the
		/// "on" arm: every member is in exactly one of the two, or <c>cwho(&lt;chan&gt;,on)</c> and
		/// <c>cwho(&lt;chan&gt;,off)</c> would both name a connected object.
		/// </summary>
		public bool ListedAsOff(bool privilegedWho) => !ListedAsOn(privilegedWho);
	}

	/// <summary>
	/// The channel's owner, or <see langword="null"/> when it cannot be resolved.
	///
	/// <para>PennMUSH never dereferences a channel's creator to draw a listing: <c>do_channel_list</c>
	/// (<c>src/extchat.c:2688</c>) compares <c>ChanCreator(c) == player</c>, a dbref, so a channel whose
	/// creator is gone still lists. Here the owner is an object behind an <c>AsyncLazy</c> that THROWS
	/// when it cannot be found, and the commands that walk every channel at once — <c>@channel/list</c>
	/// and <c>@channel/what</c> — are exactly the ones that must not die because one row's owner is
	/// unresolvable. It is reachable in practice: under <c>surrealdb</c> the owner is an edge rather than
	/// a field, and a channel read while another connection is creating one has been observed with no
	/// <c>owner_of_channel</c> edge yet.</para>
	///
	/// <para>Commands that act on a single named channel do NOT use this — a missing owner there is worth
	/// surfacing, not rendering as a dash.</para>
	/// </summary>
	public static async ValueTask<SharpPlayer?> TryResolveOwner(SharpChannel channel)
	{
		try
		{
			return await channel.Owner.WithCancellation(CancellationToken.None);
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	/// <summary>
	/// Whether <paramref name="viewer"/> sees through <c>@channel/hide</c>: PennMUSH's <c>Priv_Who</c>
	/// (<c>hdrs/mushtype.h</c>), which is privileged status or the <c>Who</c> power.
	/// </summary>
	public static async ValueTask<bool> PrivilegedWho(AnySharpObject viewer)
		=> await viewer.IsPriv() || await viewer.HasPower("Who");

	/// <summary>
	/// The channel's membership with each member's connection state attached, so the callers can apply
	/// PennMUSH's listing rules (<see cref="ChannelMember.ListedAsOn"/> /
	/// <see cref="ChannelMember.ListedAsOff"/>).
	///
	/// </summary>
	public static async ValueTask<List<ChannelMember>> ChannelMembers(IConnectionService connectionService,
		SharpChannel channel)
	{
		var result = new List<ChannelMember>();

		await foreach (var (member, status) in channel.Members.Value)
		{
			var connected = member.IsThing
											|| await connectionService.Get(member.Object().DBRef).AnyAsync();
			result.Add(new ChannelMember(member, status, connected));
		}

		return result;
	}

	/// <summary>
	/// Drops the recall lines that were only ever delivered to privileged members - PennMUSH's
	/// <c>CBTYPE_SEEALL</c> filter, applied identically by <c>do_chan_recall</c> (src/extchat.c:4083)
	/// and <c>fun_crecall</c> (:3559): a See_All viewer sees everything, and everyone else still sees
	/// their own lines. Without it, <c>@channel/recall</c> replays the hidden-connect announcement
	/// that the live broadcast deliberately withheld.
	/// </summary>
	public static async ValueTask<List<SharpChannelMessage>> FilterRecallableAsync(
		IEnumerable<SharpChannelMessage> messages, AnySharpObject viewer)
	{
		var materialized = messages.ToList();
		if (!materialized.Any(x => x.SeeAllOnly))
		{
			return materialized;
		}

		if (await viewer.IsSee_All())
		{
			return materialized;
		}

		var viewerNumber = viewer.Object().DBRef.Number;
		return materialized
			.Where(x => !x.SeeAllOnly || x.Sender.Number == viewerNumber)
			.ToList();
	}
}
