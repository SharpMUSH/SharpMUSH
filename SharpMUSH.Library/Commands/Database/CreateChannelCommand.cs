using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateChannelCommand(
	MString Channel,
	string[] Privs,
	SharpPlayer Owner) : ICommand, ICacheInvalidating
{
	// Add a $ at the end to make sure it cannot conflict with object DBRefs.
	public string[] CacheKeys => [$"channels:{Channel.ToPlainText()}$"];
	public string[] CacheTags => [Definitions.CacheTags.ChannelList];
}

public record UpdateChannelOwnerCommand(SharpChannel Channel, SharpPlayer Player) : ICommand;

public record UpdateChannelCommand(
	SharpChannel Channel,
	MString? Name,
	MString? Description,
	string[]? Privs,
	string? JoinLock,
	string? SpeakLock,
	string? SeeLock,
	string? HideLock,
	string? ModLock, 
	string? Mogrifier,
	int? Buffer) : ICommand;

public record DeleteChannelCommand(SharpChannel Channel) : ICommand;

public record AddUserToChannelCommand(
	SharpChannel Channel, 
	AnySharpObject Object) : ICommand;

public record RemoveUserFromChannelCommand(
	SharpChannel Channel, 
	AnySharpObject Object) : ICommand;

public record UpdateChannelUserStatusCommand(
	SharpChannel Channel,
	AnySharpObject Object,
	SharpChannelStatus Status) : ICommand;