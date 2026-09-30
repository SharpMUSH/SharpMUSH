using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateChannelCommandHandler(IChannelStore database) : ICommandHandler<CreateChannelCommand, ChannelCreationResult>
{
	public async ValueTask<ChannelCreationResult> Handle(CreateChannelCommand request, CancellationToken cancellationToken)
		=> await database.CreateChannelAsync(request.Channel, request.Privs, request.Owner, cancellationToken);
}

/// <summary>
/// A rename changes every member's channel list too, but it is announced by <c>ChannelRename</c>, not
/// here: this write invalidates the cached channel list only once it returns, so a handler of the
/// announcement run from inside it could read the list from before the rename.
/// </summary>
public class UpdateChannelCommandHandler(IChannelStore database) : ICommandHandler<UpdateChannelCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelCommand request, CancellationToken cancellationToken)
	{
		await database.UpdateChannelAsync(request.Channel,
			request.Name,
			request.Description,
			request.Privs,
			request.JoinLock,
			request.SpeakLock,
			request.SeeLock,
			request.HideLock,
			request.ModLock,
			request.Mogrifier,
			request.Buffer, cancellationToken);
		return Unit.Value;
	}
}

/// <summary>The members are read before the channel goes, since afterwards there is nothing to read them from.</summary>
public class DeleteChannelCommandHandler(IChannelStore database, IPublisher publisher) : ICommandHandler<DeleteChannelCommand>
{
	public async ValueTask<Unit> Handle(DeleteChannelCommand request, CancellationToken cancellationToken)
	{
		var members = await request.Channel.Members.Value
			.Select(member => member.Member)
			.ToListAsync(cancellationToken);

		await database.DeleteChannelAsync(request.Channel, cancellationToken);

		foreach (var member in members)
		{
			await publisher.Publish(
				new ChannelMembershipChangedNotification(member, request.Channel.Name.ToPlainText(), "delete"),
				cancellationToken);
		}

		return Unit.Value;
	}
}

public class AddUserToChannelCommandHandler(IChannelStore database, IPublisher publisher) : ICommandHandler<AddUserToChannelCommand>
{
	public async ValueTask<Unit> Handle(AddUserToChannelCommand request, CancellationToken cancellationToken)
	{
		await database.AddUserToChannelAsync(request.Channel, request.Object, cancellationToken);
		await publisher.Publish(
			new ChannelMembershipChangedNotification(request.Object, request.Channel.Name.ToPlainText(), "join"),
			cancellationToken);
		return Unit.Value;
	}
}

public class RemoveUserFromChannelCommandHandler(IChannelStore database, IPublisher publisher) : ICommandHandler<RemoveUserFromChannelCommand>
{
	public async ValueTask<Unit> Handle(RemoveUserFromChannelCommand request, CancellationToken cancellationToken)
	{
		await database.RemoveUserFromChannelAsync(request.Channel, request.Object, cancellationToken);
		await publisher.Publish(
			new ChannelMembershipChangedNotification(request.Object, request.Channel.Name.ToPlainText(), "leave"),
			cancellationToken);
		return Unit.Value;
	}
}

public class UpdateChannelUserStatusCommandHandler(IChannelStore database, IPublisher publisher) : ICommandHandler<UpdateChannelUserStatusCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelUserStatusCommand request, CancellationToken cancellationToken)
	{
		await database.UpdateChannelUserStatusAsync(request.Channel, request.Object, request.Status, cancellationToken);
		await publisher.Publish(
			new ChannelMembershipChangedNotification(request.Object, request.Channel.Name.ToPlainText(), "status"),
			cancellationToken);
		return Unit.Value;
	}
}

public class UpdateChannelOwnerCommandHandler(IChannelStore database) : ICommandHandler<UpdateChannelOwnerCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelOwnerCommand request, CancellationToken cancellationToken)
	{
		await database.UpdateChannelOwnerAsync(request.Channel, request.Player, cancellationToken);
		return Unit.Value;
	}
}
