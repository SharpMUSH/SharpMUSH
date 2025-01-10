using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SendMailCommandHandler(ISharpDatabase database)  : ICommandHandler<SendMailCommand>
{
	public async ValueTask<Unit> Handle(SendMailCommand command, CancellationToken cancellationToken)
	{
		await database.SendMailAsync(command.Sender, command.Recipient, command.Mail);
		throw new NotImplementedException();
	}
}