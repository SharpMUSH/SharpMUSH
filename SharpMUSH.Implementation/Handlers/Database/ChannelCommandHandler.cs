using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateChannelCommandHandler(ISharpDatabase database) : ICommandHandler<CreateChannelCommand>
{
	public async ValueTask<Unit> Handle(CreateChannelCommand request, CancellationToken cancellationToken)
	{
		await database.CreateChannelAsync(request.Channel, request.Privs, request.Owner);
		return Unit.Value;
	}
}

public class UpdateChannelCommandHandler(ISharpDatabase database) : ICommandHandler<UpdateChannelCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelCommand request, CancellationToken cancellationToken)
	{
		await database.UpdateChannelAsync(request.Channel, request.Name, request.Description, request.Privs, request.JoinLock, request.SpeakLock, request.SeeLock, request.HideLock, request.ModLock);
		return Unit.Value;
	}
}

public class DeleteChannelCommandHandler(ISharpDatabase database) : ICommandHandler<DeleteChannelCommand>
{
	public async ValueTask<Unit> Handle(DeleteChannelCommand request, CancellationToken cancellationToken)
	{
		await database.DeleteChannelAsync(request.Channel);
		return Unit.Value;
	}
}

public class AddUserToChannelCommandHandler(ISharpDatabase database) : ICommandHandler<AddUserToChannelCommand>
{
	public async ValueTask<Unit> Handle(AddUserToChannelCommand request, CancellationToken cancellationToken)
	{
		await database.AddUserToChannelAsync(request.Channel, request.Object);
		return Unit.Value;
	}
}

public class RemoveUserFromChannelCommandHandler(ISharpDatabase database) : ICommandHandler<RemoveUserFromChannelCommand>
{
	public async ValueTask<Unit> Handle(RemoveUserFromChannelCommand request, CancellationToken cancellationToken)
	{
		await database.RemoveUserFromChannelAsync(request.Channel, request.Object);
		return Unit.Value;
	}
}

public class UpdateChannelUserStatusCommandHandler(ISharpDatabase database) : ICommandHandler<UpdateChannelUserStatusCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelUserStatusCommand request, CancellationToken cancellationToken)
	{
		await database.UpdateChannelUserStatusAsync(request.Channel, request.Object, request.Status);
		return Unit.Value;
	}
}

public class UpdateChannelOwnerCommandHandler(ISharpDatabase database) : ICommandHandler<UpdateChannelOwnerCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelOwnerCommand request, CancellationToken cancellationToken)
	{
		await database.UpdateChannelOwnerAsync(request.Channel, request.Player);
		return Unit.Value;
	}
}