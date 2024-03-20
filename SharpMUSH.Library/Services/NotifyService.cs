using MediatR;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Requests;

namespace SharpMUSH.Library.Services
{
	public class NotifyService(IPublisher _publisher, IConnectionService _connectionService) : INotifyService
	{
		public async Task Notify(DBRef who, string what)
			=> await _publisher.Publish(
				new TelnetOutputRequest(
					_connectionService.Get(who).Select(x => x.Item1).ToArray(),
					what
				));

		public async Task Notify(string handle, string what)
			=> await _publisher.Publish(new TelnetOutputRequest([handle], what));

		public async Task Notify(string[] handles, string what)
			=> await _publisher.Publish(new TelnetOutputRequest(handles, what));
	}
}
