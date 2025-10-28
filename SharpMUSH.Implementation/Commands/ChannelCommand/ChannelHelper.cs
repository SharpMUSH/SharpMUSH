using System.Collections.ObjectModel;
using Mediator;
using OneOf;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

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
	private static readonly ReadOnlyDictionary<string, char> ChannelPrivileges = new(
		new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
		{
			{ "Disabled", 'D' },
			{ "Player", 'P' },
			{ "Admin", 'A' },
			{ "Wizard", 'W' },
			{ "Thing", 'T' },
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
		=> await channel.Members.Value
			.AnyAsync(x => x.Member.Id() == member.Id());

	public static async ValueTask<(AnySharpObject Member, SharpChannelStatus Status)?> ChannelMemberStatus(
		AnySharpObject member, SharpChannel channel)
		=> await channel.Members.Value
			.FirstOrDefaultAsync(x => x.Member.Id() == member.Id());

	public static PrivilegeOrError StringToChannelPrivileges(MString channelName)
	{
		var plainText = channelName.ToPlainText();
		var list = plainText
			.Split(' ')
			.Where(x => !string.IsNullOrWhiteSpace(x))
			.ToArray();

		var badList = list.Where(x => x.Length == 1
			? !ChannelPrivilegesReverse.ContainsKey(x.ToUpper()[0])
			: !ChannelPrivileges.ContainsKey(x)).ToArray();

		if (badList.Length != 0)
		{
			return new PrivilegeOrError(new Error<string[]>(badList));
		}

		var validatedList = list.Select(x => x.Length == 1
				? ChannelPrivilegesReverse.GetValueOrDefault(x.ToUpper()[0], null)
				: (ChannelPrivileges.ContainsKey(x) ? x : null))
			.Where(x => x != null).ToArray();

		return new PrivilegeOrError(validatedList!);
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
				await NotifyService.Notify(await parser.CurrentState.KnownExecutorObject(Mediator),
					"Channel not found.");
				return new ChannelOrError(new Error<CallState>(new CallState("#-1 Channel not found.")));
			}
			case (null, false):
			{
				return new ChannelOrError(new Error<CallState>(new CallState("#-1 Channel not found.")));
			}
			case ({ } foundChannel, _):
			{
				return new ChannelOrError(foundChannel);
			}
		}
	}
}