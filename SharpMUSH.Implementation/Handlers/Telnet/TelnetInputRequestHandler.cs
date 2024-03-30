using MediatR;
using SharpMUSH.Library.Requests;

namespace SharpMUSH.Implementation.Handlers.Telnet
{
	public class TelnetInputRequestHandler(MUSHCodeParser _parser) : INotificationHandler<TelnetInputRequest>
	{
		public async Task Handle(TelnetInputRequest request, CancellationToken ct)
			=> await _parser.CommandParse(request.Handle, request.Input);
	}
}
