using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using static SharpMUSH.Library.Services.INotifyService;

namespace SharpMUSH.Library.Services;

public class NotifyService(IConnectionService _connectionService) : INotifyService
{
	public async Task Notify(DBRef who, MString what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
	{
		if(MModule.getLength(what) == 0)
		{
			return;
		}

		var list = _connectionService.Get(who);

		try
		{
			foreach (var item in list)
			{
				await (item?.OutputFunction(item.Encoding().GetBytes(what.ToString())) ?? ValueTask.CompletedTask);
			}
		}
		catch { }
	}

	public Task Notify(AnySharpObject who, MString what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	public async Task Notify(string handle, MString what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
	{
		if (MModule.getLength(what) == 0)
		{
			return;
		}

		var item = _connectionService.Get(handle);

		try
		{
			await (item?.OutputFunction(item.Encoding().GetBytes(what.ToString())) ?? ValueTask.CompletedTask);
		}
		catch { }
	}

	public async Task Notify(string[] handles, MString what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
	{
		if (MModule.getLength(what) == 0)
		{
			return;
		}

		var list = handles.Select(_connectionService.Get);

		try
		{
			foreach (var item in list)
			{
				await (item?.OutputFunction(item!.Encoding().GetBytes(what.ToString())) ?? ValueTask.CompletedTask);
			}
		}
		catch { }
	}

	public async Task Notify(DBRef who, string what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
	{
		if (what.Length == 0)
		{
			return;
		}

		var list = _connectionService.Get(who);

		try
		{
			foreach (var item in list)
			{
				await (item?.OutputFunction(item.Encoding().GetBytes(what)) ?? ValueTask.CompletedTask);
			}
		}
		catch { }
	}

	public Task Notify(AnySharpObject who, string what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	public async Task Notify(string handle, string what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
	{
		if (what.Length == 0)
		{
			return;
		}

		var item = _connectionService.Get(handle);

		try
		{
			await (item?.OutputFunction(item.Encoding().GetBytes(what)) ?? ValueTask.CompletedTask);
		}
		catch { }
	}

	public async Task Notify(string[] handles, string what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
	{
		if (what.Length == 0)
		{
			return;
		}

		var list = handles.Select(_connectionService.Get);

		try
		{
			foreach (var item in list)
			{
				await (item?.OutputFunction(item!.Encoding().GetBytes(what)) ?? ValueTask.CompletedTask);
			}
		}
		catch { }
	}
}