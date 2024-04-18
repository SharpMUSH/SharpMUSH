using MediatR;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;

namespace SharpMUSH.Implementation.Handlers.Telnet
{
	public class TelnetInputRequestHandler(IMUSHCodeParser _parser) : INotificationHandler<TelnetInputRequest>
	{
		public async Task Handle(TelnetInputRequest request, CancellationToken ct)
			=> await _parser.CommandParse(request.Handle, request.Input);
	}
}
