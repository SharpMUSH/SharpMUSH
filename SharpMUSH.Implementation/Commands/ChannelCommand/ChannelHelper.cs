using Mediator;
using OneOf;
using OneOf.Types;
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

public class ChannelOrError : OneOfBase<SharpChannel, Error<CallState>>
{
	public ChannelOrError(SharpChannel channel) : base(channel)
	{
	}

	public ChannelOrError(Error<CallState> error) : base(error)
	{
	}

	public bool IsError => IsT1;
	public SharpChannel AsChannel => AsT0;
	public Error<CallState> AsError => AsT1;
}

public class PrivilegeOrError : OneOfBase<string[], Error<string[]>>
{
	public PrivilegeOrError(string[] channel) : base(channel)
	{
	}

	public PrivilegeOrError(Error<string[]> error) : base(error)
	{
	}

	public bool IsError => IsT1;
	public string[] AsPrivileges => AsT0;
	public Error<string[]> AsError => AsT1;
}

public static class ChannelHelper
{
	/// <summary>
	/// Channel privilege names and their single-character abbreviations, matching PennMUSH's
	/// <c>chan_privs</c> table (<c>extchat.c</c>). The characters are case-sensitive: 'O' is Object
	/// and 'o' is Open.
	/// </summary>
	private static readonly ReadOnlyDictionary<string, char> ChannelPrivileges = new(
		new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
		{
			{ "Disabled", 'D' },
			{ "Player", 'P' },
			{ "Admin", 'A' },
			{ "Wizard", 'W' },
			{ "Object", 'O' },
			{ "Quiet", 'Q' },
			{ "Open", 'o' },
			{ "Hide_Ok", 'H' },
			{ "NoTitles", 'T' },
			{ "NoNames", 'N' },
			{ "NoCemit", 'C' },
			{ "Interact", 'I' }
		});

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
	/// <para>Names are returned in the canonical casing of the <c>chan_privs</c> table, never in whatever
	/// casing the player typed. <c>@channel/add Foo=wizard</c> used to persist the literal <c>"wizard"</c>,
	/// which every ordinal <c>Privs.Contains("Wizard")</c> permission check then failed to see.</para>
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

	public static bool IsValidChannelName(IOptionsWrapper<SharpMUSHOptions> Configuration, string channelName)
		=> Configuration.CurrentValue.Chat.ChannelTitleLength >= channelName.Length
			 && channelName.Length > 3
			 && !channelName.Contains(' ');

	/// <summary>
	/// Looks a channel up by name. This is PennMUSH's <c>find_channel()</c> and nothing more: it does not
	/// decide who may see, join or speak on what it returns.
	///
	/// <para>It used to take an <see cref="IPermissionService"/> and never touch it, which made every one
	/// of its twenty-odd call sites read as though a permission check had already happened. The parameter
	/// is gone; callers that need a gate ask for one by name — <see cref="GetVisibleChannelOrError"/>,
	/// <see cref="JoinRefusal"/>, <see cref="SpeechRefusal"/>, <see cref="CemitRefusal"/>.</para>
	/// </summary>
	public static async ValueTask<ChannelOrError> GetChannelOrError(
		IMUSHCodeParser parser,
		IMediator Mediator,
		INotifyService NotifyService,
		MString channelName,
		bool notify = false)
	{
		var channel = await Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));

		switch (channel, notify)
		{
			case (null, true):
				{
					var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
					await NotifyService.Notify(executor,
						"Channel not found.", executor);
					return new ChannelOrError(new Error<CallState>(new CallState(ErrorMessages.Returns.ChannelNotFound)));
				}
			case (null, false):
				{
					return new ChannelOrError(new Error<CallState>(new CallState(ErrorMessages.Returns.ChannelNotFound)));
				}
			case ({ } foundChannel, _):
				{
					return new ChannelOrError(foundChannel);
				}
		}
	}

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
		IMUSHCodeParser parser,
		IPermissionService permissionService,
		IMediator mediator,
		INotifyService notifyService,
		AnySharpObject viewer,
		MString channelName,
		bool notify = false)
	{
		// notify: false — the caller must not learn which of the two failures it hit, so the one refusal
		// below is the only thing either path is allowed to emit.
		var maybeChannel = await GetChannelOrError(parser, mediator, notifyService, channelName, notify: false);

		if (!maybeChannel.IsError && await CanSeeChannel(permissionService, viewer, maybeChannel.AsChannel))
		{
			return maybeChannel;
		}

		if (notify)
		{
			await notifyService.Notify(viewer, ErrorMessages.Notifications.DontRecognizeThatChannel, viewer);
		}

		return new ChannelOrError(new Error<CallState>(new CallState(ErrorMessages.Returns.NoSuchChannel)));
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
		// Materialise the channel list BEFORE testing visibility. The test reads channel.Members, which
		// opens its own database stream, and Core.Arango 3.12.x races when one ExecuteStreamAsync is
		// enumerated inside another — it faults on a thread pool thread and takes the process with it.
		// The same driver race is already worked around in HelperFunctions.HasPower.
		var all = await channels.ToArrayAsync();
		var visible = new List<SharpChannel>(all.Length);

		foreach (var channel in all)
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
	/// PennMUSH <c>do_cemit</c> (<c>src/extchat.c:1622-1640</c>): See_All + Pemit_All skips the checks
	/// entirely, since such a player could enumerate the channel's members and <c>@pemit</c> them anyway.
	/// Otherwise the type gate and <c>Chan_Can_Cemit</c> apply. Note <c>LOUD</c> does NOT bypass this —
	/// Penn only consults it in <c>do_chat</c>.
	/// </summary>
	public static async ValueTask<string?> CemitRefusal(IPermissionService permissionService, AnySharpObject who,
		SharpChannel channel)
	{
		if (await who.IsSee_All() && await who.HasPower("Pemit_All"))
		{
			return null;
		}

		if (WrongTypeRefusal(permissionService, who, channel) is { } wrongType)
		{
			return wrongType;
		}

		return await permissionService.ChannelCanCemit(who, channel)
			? null
			: string.Format(ErrorMessages.Notifications.ChatNotAllowedToCemit, channel.Name.ToPlainText());
	}
}