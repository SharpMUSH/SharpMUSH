using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SendMailCommandHandler(IMailStore database) : ICommandHandler<SendMailCommand>
{
	public async ValueTask<Unit> Handle(SendMailCommand command, CancellationToken cancellationToken)
	{
		await database.SendMailAsync(command.Sender, command.Recipient, command.Mail, cancellationToken);
		return Unit.Value;
	}
}

public class UpdateMailCommandHandler(IMailStore database) : ICommandHandler<UpdateMailCommand>
{
	public async ValueTask<Unit> Handle(UpdateMailCommand command, CancellationToken cancellationToken)
	{
		await database.UpdateMailAsync(command.Mail.Id!, command.Update, cancellationToken);
		return Unit.Value;
	}
}

public class DeleteMailHandler(IMailStore database) : ICommandHandler<DeleteMailCommand>
{
	public async ValueTask<Unit> Handle(DeleteMailCommand command, CancellationToken cancellationToken)
	{
		await database.DeleteMailAsync(command.Mail.Id!, cancellationToken);
		return Unit.Value;
	}
}

public class RenameMailFolderHandler(IMailStore database) : ICommandHandler<RenameMailFolderCommand>
{
	public async ValueTask<Unit> Handle(RenameMailFolderCommand command, CancellationToken cancellationToken)
	{
		await database.RenameMailFolderAsync(command.Owner, command.FolderName, command.NewFolderName, cancellationToken);
		return Unit.Value;
	}
}

public class MoveMailFolderHandler(IMailStore database) : ICommandHandler<MoveMailFolderCommand>
{
	public async ValueTask<Unit> Handle(MoveMailFolderCommand command, CancellationToken cancellationToken)
	{
		await database.MoveMailFolderAsync(command.Mail.Id!, command.NewFolderName, cancellationToken);
		return Unit.Value;
	}
}