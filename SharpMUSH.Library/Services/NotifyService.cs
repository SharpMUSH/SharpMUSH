using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

// TODO: Convert to Mediator Notification Handler.
public class NotifyService(IConnectionService _connectionService) : INotifyService
{
	public async Task Notify(DBRef who, MString what)
	{
		var list = _connectionService.Get(who);

		try
		{
			foreach (var item in list)
			{
				await item.OutputFunction(item.Encoding().GetBytes(what.ToString()));
			}
		}
		catch { }
	}

	public Task Notify(AnySharpObject who, MString what) => Notify(who.Object().DBRef, what);


	public async Task Notify(string handle, MString what)
	{
		var item = _connectionService.Get(handle);

		try
		{
			await item!.OutputFunction(item.Encoding().GetBytes(what.ToString()));
		}
		catch { }
	}

	public async Task Notify(string[] handles, MString what)
	{
		var list = handles.Select(_connectionService.Get);

		try
		{
			foreach (var item in list)
			{
				await item!.OutputFunction(item!.Encoding().GetBytes(what.ToString()));
			}
		}
		catch { }
	}
	
	public async Task Notify(DBRef who, string what)
	{
		var list = _connectionService.Get(who);

		try
		{
			foreach (var item in list)
			{
				await item.OutputFunction(item.Encoding().GetBytes(what));
			}
		}
		catch { }
	}

	public Task Notify(AnySharpObject who, string what) => Notify(who.Object().DBRef, what);


	public async Task Notify(string handle, string what)
	{
		var item = _connectionService.Get(handle);

		try
		{
			await item!.OutputFunction(item.Encoding().GetBytes(what));
		}
		catch { }
	}

	public async Task Notify(string[] handles, string what)
	{
		var list = handles.Select(_connectionService.Get);

		try
		{
			foreach (var item in list)
			{
				await item!.OutputFunction(item!.Encoding().GetBytes(what));
			}
		}
		catch { }
	}
}