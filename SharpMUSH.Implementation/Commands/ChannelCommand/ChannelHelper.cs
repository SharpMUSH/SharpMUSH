using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.ObjectModel;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public union ChannelOrError(SharpChannel, SharpErrorCallState)
{
	public bool IsError    => Value is SharpErrorCallState;
	public SharpChannel        AsChannel => (SharpChannel)Value!;
	public SharpErrorCallState AsError   => (SharpErrorCallState)Value!;
}

/// <summary>Thin wrappers so PrivilegeOrError can have two distinct string[] cases.</summary>
public readonly record struct GrantedPrivileges(string[] Values);
public readonly record struct DeniedPrivileges(string[] Values);

public union PrivilegeOrError(GrantedPrivileges, DeniedPrivileges)
{
	public bool    IsError      => Value is DeniedPrivileges;
	public string[] AsPrivileges => ((GrantedPrivileges)Value!).Values;
	public string[] AsError      => ((DeniedPrivileges)Value!).Values;
}

public static class ChannelHelper
{
	private static readonly ReadOnlyDictionary<string, char> ChannelPrivileges = new(
		new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
		{
			{ "Disabled", 'D' },
			{ "Player",   'P' },
			{ "Admin",    'A' },
			{ "Wizard",   'W' },
			{ "Thing",    'T' },
			{ "Object",   'O' },
			{ "Quiet",    'Q' },
			{ "Open",     'o' },
			{ "Hide_Ok",  'H' },
			{ "NoTitles", 'T' },
			{ "NoNames",  'N' },
			{ "NoCemit",  'C' },
			{ "Interact", 'I' }
		});

	private static readonly ReadOnlyDictionary<char, string?> ChannelPrivilegesReverse =
		new(ChannelPrivileges.ToDictionary(x => x.Value, string? (x) => x.Key));

	public static async ValueTask<bool> IsMemberOfChannel(AnySharpObject member, SharpChannel channel)
		=> await channel.Members.Value.AnyAsync(x => x.Member.Id == member.Id);

	public static async ValueTask<SharpChannel.MemberAndStatus?> ChannelMemberStatus(
		AnySharpObject member, SharpChannel channel) =>
		await channel.Members.Value.FirstOrDefaultAsync(x => x.Member.Id == member.Id);

	public static PrivilegeOrError StringToChannelPrivileges(MString channelName)
	{
		var plainText = channelName.ToPlainText();
		var list = plainText.Split(' ').Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

		var badList = list.Where(x => x.Length == 1
			? !ChannelPrivilegesReverse.ContainsKey(x.ToUpper()[0])
			: !ChannelPrivileges.ContainsKey(x)).ToArray();

		if (badList.Length != 0)
			return new DeniedPrivileges(badList);

		var validatedList = list.Select(x => x.Length == 1
				? ChannelPrivilegesReverse.GetValueOrDefault(x.ToUpper()[0], null)
				: (ChannelPrivileges.ContainsKey(x) ? x : null))
			.Where(x => x != null).ToArray();

		return new GrantedPrivileges(validatedList!);
	}

	public static bool IsValidChannelName(IOptionsWrapper<SharpMUSHOptions> Configuration, MString channelName)
		=> IsValidChannelName(Configuration, channelName.ToPlainText());

	public static bool IsValidChannelName(IOptionsWrapper<SharpMUSHOptions> Configuration, string channelName)
		=> Configuration.CurrentValue.Chat.ChannelTitleLength >= channelName.Length
			 && channelName.Length > 3
			 && !channelName.Contains(' ');

	public static async ValueTask<ChannelOrError> GetChannelOrError(
		IMUSHCodeParser parser,
		ILocateService LocateService,
		IPermissionService PermissionService,
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
				await NotifyService.Notify(executor, "Channel not found.", executor);
				return new SharpErrorCallState(new CallState("#-1 Channel not found."));
			}
			case (null, false):
				return new SharpErrorCallState(new CallState("#-1 Channel not found."));
			case ({ } foundChannel, _):
				return foundChannel;
		}
	}
}
