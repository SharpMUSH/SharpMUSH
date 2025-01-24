using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateChannelCommand(
	SharpChannel Channel, 
	SharpPlayer Owner) : ICommand;

public record UpdateChannelCommand(
	SharpChannel Channel,
	string? Name,
	string? Description,
	string[]? Privs,
	string? JoinLock,
	string? SpeakLock,
	string? SeeLock,
	string? HideLock,
	string? ModLock) : ICommand;

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