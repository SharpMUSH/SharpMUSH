using System.Globalization;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// TinyMUX's comsys functions that PennMUSH lacks: <c>chanfind()</c>, <c>chaninfo()</c>, <c>chanuser()</c> and
/// <c>chanusers()</c>. A channel is found as every channel function finds one, and one the caller may not see is
/// <c>#-1 CHANNEL NOT FOUND</c>, as a missing one is.
/// </summary>
/// <remarks>
/// TinyMUX's per-member settings map as its import maps them: a member who turned the channel off is a gagged
/// member here, and one who gagged join and leave messages is a muted (Quiet) one. SharpMUSH shows every member's
/// title, so comtitles are always on.
/// </remarks>
public partial class Functions
{
	/// <summary>TinyMUX's channel type bits (<c>chaninfo(&lt;channel&gt;, type)</c>).</summary>
	private const int MuxPlayerJoinTransmitReceive = 0x7;
	private const int MuxObjectJoinTransmitReceive = 0x70;
	private const int MuxLoud = 0x100;
	private const int MuxPublic = 0x200;

	/// <summary>The channel <paramref name="name"/> names, if the caller may see it, handed to <paramref name="answer"/>.</summary>
	private async ValueTask<CallState> WithMuxChannel(AnySharpObject executor, string name,
		Func<SharpChannel, ValueTask<CallState>> answer)
		=> await ChannelHelper.GetVisibleChannelOrError(ChannelPermissions, Mediator, NotifyService, executor,
				MarkupText.Plain(name), false) switch
		{
			SharpChannel channel => await answer(channel),
			_ => new CallState(ErrorMessages.Returns.ChannelNotFound)
		};

	/// <summary>What a channel's messages begin with: its name in angle brackets.</summary>
	private static string ChannelHeader(SharpChannel channel) => $"<{channel.Name.ToPlainText()}>";

	/// <summary>
	/// <c>chanfind(&lt;name or header&gt;)</c>: the name of the channel called <c>&lt;name&gt;</c>, or whose header
	/// (<c>&lt;Name&gt;</c>) or description is the text given, ignoring case.
	/// </summary>
	[SharpFunction(Name = "chanfind", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["name"])]
	public async ValueTask<CallState> ChannelFind(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var target = OptionalArgument(parser, "0").Trim();

		await foreach (var channel in Mediator.CreateStream(new GetChannelListQuery()))
		{
			var named = channel.Name.ToPlainText().Equals(target, StringComparison.OrdinalIgnoreCase)
				|| ChannelHeader(channel).Equals(target, StringComparison.OrdinalIgnoreCase)
				|| (channel.Description.Length > 0
					&& channel.Description.ToPlainText().Equals(target, StringComparison.OrdinalIgnoreCase));
			if (named && await ChannelHelper.CanSeeChannel(ChannelPermissions, Mediator, executor, channel))
			{
				return new CallState(channel.Name);
			}
		}

		return new CallState(ErrorMessages.Returns.ChannelNotFound);
	}

	/// <summary>
	/// <c>chaninfo(&lt;channel&gt;, &lt;field&gt;)</c>: one fact about a channel: <c>name</c>, <c>header</c>,
	/// <c>owner</c>, <c>object</c>, <c>type</c>, <c>flags</c>, <c>charge</c>, <c>users</c>, <c>msgs</c>,
	/// <c>desc</c>, <c>buffer</c>, <c>canjoin</c>, <c>cantransmit</c> or <c>canreceive</c>.
	/// </summary>
	[SharpFunction(Name = "chaninfo", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel", "field"])]
	public async ValueTask<CallState> ChannelInformation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var field = OptionalArgument(parser, "1").Trim().ToLowerInvariant();

		return await WithMuxChannel(executor, OptionalArgument(parser, "0"), async channel => field switch
		{
			"name" => new CallState(channel.Name),
			"header" => new CallState(ChannelHeader(channel)),
			"owner" => new CallState($"#{channel.OwnerDBRef.Number}"),
			"object" => await executor.IsWizard()
				? new CallState(await ChannelHelper.Mogrifier(Mediator, channel) is AnySharpObject mogrifier
					? $"#{mogrifier.Object().DBRef.Number}"
					: ErrorMessages.Returns.Nothing)
				: new CallState(ErrorMessages.Returns.PermissionDenied),
			"type" => new CallState(MuxChannelType(channel).ToString(CultureInfo.InvariantCulture)),
			"flags" => new CallState(MuxChannelFlags(channel)),
			// Speaking on a channel costs nothing here.
			"charge" => new CallState("0"),
			"users" => new CallState((await Mediator.Send(new GetChannelMemberCountQuery(channel)))
				.ToString(CultureInfo.InvariantCulture)),
			"msgs" => new CallState((await Mediator.Send(new CountChannelMessagesQuery(channel.Id ?? string.Empty)))
				.ToString(CultureInfo.InvariantCulture)),
			"desc" => new CallState(channel.Description),
			"buffer" => new CallState(channel.Buffer.ToString(CultureInfo.InvariantCulture)),
			"canjoin" => new CallState(await ChannelPermissions.ChannelCanJoin(executor, channel) ? "1" : "0"),
			"cantransmit" => new CallState(await ChannelPermissions.ChannelCanSpeak(executor, channel) ? "1" : "0"),
			"canreceive" => new CallState(await ChannelPermissions.ChannelCanSeeAsync(executor, channel) ? "1" : "0"),
			_ => new CallState(ErrorMessages.Returns.InvalidField)
		});
	}

	private static bool HasPrivilege(SharpChannel channel, string privilege)
		=> channel.Privs.Contains(privilege, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// TinyMUX's type bits for a channel: players and objects may join, speak and listen when the channel admits
	/// them; loud when not Quiet; public when it has no see lock.
	/// </summary>
	private static int MuxChannelType(SharpChannel channel)
		=> (HasPrivilege(channel, "Player") ? MuxPlayerJoinTransmitReceive : 0)
			| (HasPrivilege(channel, "Object") ? MuxObjectJoinTransmitReceive : 0)
			| (HasPrivilege(channel, "Quiet") ? 0 : MuxLoud)
			| (string.IsNullOrEmpty(channel.SeeLock) ? MuxPublic : 0);

	/// <summary>TinyMUX's flag letters: <c>P</c>ublic and <c>L</c>oud. SharpMUSH has no spoofing channels.</summary>
	private static string MuxChannelFlags(SharpChannel channel)
		=> string.Concat(
			string.IsNullOrEmpty(channel.SeeLock) ? "P" : string.Empty,
			HasPrivilege(channel, "Quiet") ? string.Empty : "L");

	/// <summary>A member's TinyMUX flags: <c>O</c> for off (gagged) and <c>G</c> for join and leave messages gagged (muted).</summary>
	private static string MuxMemberFlags(SharpChannelStatus status)
		=> string.Concat(status.Gagged is true ? "O" : string.Empty, status.Mute is true ? "G" : string.Empty);

	/// <summary>One per-member field, or null when <paramref name="field"/> is not one.</summary>
	private static CallState? MuxMemberField(SharpChannelStatus status, string field)
		=> field switch
		{
			"title" => new CallState(status.Title ?? MarkupText.Empty),
			"status" => new CallState(status.Gagged is true ? "Off" : "On"),
			"flags" => new CallState(MuxMemberFlags(status)),
			"gagjoin" => new CallState(status.Mute is true ? "1" : "0"),
			"comtitles" => new CallState("1"),
			_ => null
		};

	/// <summary>
	/// <c>chanusers(&lt;channel&gt;[, &lt;delimiter&gt;[, &lt;field&gt;[, &lt;offset&gt;[, &lt;limit&gt;]]]])</c>: the
	/// channel's members by dbref, or one field of each: <c>name</c>, <c>title</c>, <c>status</c>, <c>flags</c>,
	/// <c>gagjoin</c>, <c>comtitles</c>, or, for wizards, <c>alias</c>. It skips the first <c>&lt;offset&gt;</c> and
	/// lists at most <c>&lt;limit&gt;</c> when that is above 0.
	/// </summary>
	[SharpFunction(Name = "chanusers", MinArgs = 1, MaxArgs = 5, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel", "delimiter", "field", "offset", "limit"])]
	public async ValueTask<CallState> ChannelUsersMux(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var delimiter = OptionalArgument(parser, "1");
		var separator = delimiter.Length == 0 ? " " : delimiter;
		var field = OptionalArgument(parser, "2").Trim().ToLowerInvariant();
		var offset = Math.Max(0, ParsedOrZero(OptionalArgument(parser, "3")));
		var limit = Math.Max(0, ParsedOrZero(OptionalArgument(parser, "4")));

		if (field is not ("" or "name" or "title" or "status" or "flags" or "gagjoin" or "comtitles" or "alias"))
		{
			return new CallState(ErrorMessages.Returns.InvalidField);
		}

		if (field == "alias" && !await executor.IsWizard())
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		return await WithMuxChannel(executor, OptionalArgument(parser, "0"), async channel =>
		{
			var members = new List<SharpChannel.MemberAndStatus>();
			await foreach (var member in channel.Members.Value)
			{
				members.Add(member);
			}

			var page = members
				.OrderBy(member => member.Member.Object().DBRef.Number)
				.Skip(offset)
				.Take(limit > 0 ? limit : int.MaxValue);

			var values = new List<MString>();
			foreach (var (member, status) in page)
			{
				values.Add(field switch
				{
					"" => MarkupText.Plain($"#{member.Object().DBRef.Number}"),
					"name" => MarkupText.Plain(member.Object().Name),
					"alias" => MarkupText.Plain(await FirstAliasFor(member, channel.Name.ToPlainText())),
					_ => MuxMemberField(status, field)!.Message
				});
			}

			return new CallState(MarkupText.Join(MarkupText.Plain(separator), values));
		});

		static int ParsedOrZero(string text)
			=> int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
	}

	/// <summary>
	/// <c>chanuser(&lt;channel&gt;, &lt;object&gt;, &lt;field&gt;)</c>: one of the object's settings on the channel:
	/// <c>title</c>, <c>status</c>, <c>flags</c>, <c>gagjoin</c>, <c>comtitles</c> or <c>alias</c>. The alias is
	/// for objects the caller controls; the rest for the caller itself, anyone on the channel with the caller, or
	/// a wizard.
	/// </summary>
	[SharpFunction(Name = "chanuser", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["channel", "object", "field"])]
	public async ValueTask<CallState> ChannelUser(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var field = OptionalArgument(parser, "2").Trim().ToLowerInvariant();

		return await WithMuxChannel(executor, OptionalArgument(parser, "0"), async channel =>
		{
			if (await LocateService.Locate(parser, executor, executor, OptionalArgument(parser, "1").Trim(),
					LocateFlags.All) is not AnySharpObject target)
			{
				return new CallState(ErrorMessages.Returns.PlayerNotFound);
			}

			if (field == "alias")
			{
				if (!await PermissionService.Controls(executor, target))
				{
					return PermissionRefusal.NotControlled(target).Return;
				}

				var alias = await FirstAliasFor(target, channel.Name.ToPlainText());
				return new CallState(alias.Length > 0 ? alias : ErrorMessages.Returns.NotOnChannel);
			}

			if (!target.Object().DBRef.Equals(executor.Object().DBRef)
					&& !await executor.IsWizard()
					&& !await ChannelHelper.IsMemberOfChannel(Mediator, executor, channel))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (await ChannelHelper.ChannelMemberStatus(Mediator, target, channel) is not { } membership)
			{
				return new CallState(ErrorMessages.Returns.NotOnChannel);
			}

			return MuxMemberField(membership.Status, field) ?? new CallState(ErrorMessages.Returns.InvalidField);
		});
	}
}
