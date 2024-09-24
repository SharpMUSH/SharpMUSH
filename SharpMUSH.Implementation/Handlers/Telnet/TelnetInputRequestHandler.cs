using MediatR;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Handlers.Telnet;

public class TelnetInputRequestHandler(ITaskScheduler scheduler, IMUSHCodeParser parser) : INotificationHandler<TelnetInputRequest>
{
	public async Task Handle(TelnetInputRequest request, CancellationToken ct)
		=> await scheduler.Write(request.Handle, MModule.single(request.Input), parser);
}