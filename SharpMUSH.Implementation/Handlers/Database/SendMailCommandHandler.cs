using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SendMailCommandHandler(ISharpDatabase database)  : ICommandHandler<SendMailCommand>
{
	public async ValueTask<Unit> Handle(SendMailCommand command, CancellationToken cancellationToken)
	{
		await database.SendMailAsync(command.Sender, command.Recipient, command.Mail);
		return Unit.Value;
	}
}

public class UpdateMailCommandHandler(ISharpDatabase database) : ICommandHandler<UpdateMailCommand>
{
	public async ValueTask<Unit> Handle(UpdateMailCommand command, CancellationToken cancellationToken)
	{
		await database.UpdateMailAsync(command.Mail.Id!, command.Update);
		return Unit.Value;
	}
}