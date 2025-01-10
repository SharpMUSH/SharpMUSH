using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SendMailCommand(SharpObject Sender, SharpPlayer Recipient, SharpMail Mail) : ICommand;