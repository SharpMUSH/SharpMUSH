using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateChannelCommandHandler(IChannelStore database) : ICommandHandler<CreateChannelCommand, ChannelCreationResult>
{
	public async ValueTask<ChannelCreationResult> Handle(CreateChannelCommand request, CancellationToken cancellationToken)
		=> await database.CreateChannelAsync(request.Channel, request.Privs, request.Owner, cancellationToken);
}

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

public class DeleteChannelCommandHandler(IChannelStore database) : ICommandHandler<DeleteChannelCommand>
{
	public async ValueTask<Unit> Handle(DeleteChannelCommand request, CancellationToken cancellationToken)
	{
		await database.DeleteChannelAsync(request.Channel, cancellationToken);
		return Unit.Value;
	}
}

public class AddUserToChannelCommandHandler(IChannelStore database) : ICommandHandler<AddUserToChannelCommand>
{
	public async ValueTask<Unit> Handle(AddUserToChannelCommand request, CancellationToken cancellationToken)
	{
		await database.AddUserToChannelAsync(request.Channel, request.Object, cancellationToken);
		return Unit.Value;
	}
}

public class RemoveUserFromChannelCommandHandler(IChannelStore database) : ICommandHandler<RemoveUserFromChannelCommand>
{
	public async ValueTask<Unit> Handle(RemoveUserFromChannelCommand request, CancellationToken cancellationToken)
	{
		await database.RemoveUserFromChannelAsync(request.Channel, request.Object, cancellationToken);
		return Unit.Value;
	}
}

public class UpdateChannelUserStatusCommandHandler(IChannelStore database) : ICommandHandler<UpdateChannelUserStatusCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelUserStatusCommand request, CancellationToken cancellationToken)
	{
		await database.UpdateChannelUserStatusAsync(request.Channel, request.Object, request.Status, cancellationToken);
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