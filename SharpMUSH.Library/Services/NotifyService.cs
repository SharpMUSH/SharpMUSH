using MediatR;
using SharpMUSH.Library.Models;
using System.Text;

namespace SharpMUSH.Library.Services
{
	// TODO: Convert to MediatR Notification Handler.
	public class NotifyService(IConnectionService _connectionService) : INotifyService
	{
		public async Task Notify(DBRef who, string what)
		{
			var list = _connectionService.Get(who).Select(x => x.Item4).ToArray();

			try
			{
				foreach (var fun in list)
				{
					await fun(Encoding.UTF8.GetBytes(what));
				}
			}
			catch
			{

			}
		}


		public async Task Notify(string handle, string what)
		{
			var fun = _connectionService.Get(handle)!.Value.Item4;

			try
			{
					await fun(Encoding.UTF8.GetBytes(what));
			}
			catch
			{

			}
		}

		public async Task Notify(string[] handles, string what)
		{
			var list = handles.Select(_connectionService.Get).Select(x => x!.Value.Item4).ToArray();
			
			try
			{
				foreach (var fun in list)
				{
					await fun(Encoding.UTF8.GetBytes(what));
				}
			}
			catch
			{

			}
		}
		}
}
