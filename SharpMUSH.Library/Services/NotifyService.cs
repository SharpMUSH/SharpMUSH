using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using static SharpMUSH.Library.Services.INotifyService;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Notifies objects and sends telnet data.
/// </summary>
/// <remarks>
/// Intentionally not awaiting Telnet ValueTasks here, as we don't need to wait for the output to complete.
/// </remarks>
/// <param name="_connectionService">Connection Service</param>
public class NotifyService(IConnectionService _connectionService) : INotifyService
{
	public ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
			))
		{
			return ValueTask.CompletedTask;
		}

		var list = _connectionService.Get(who);

		foreach (var item in list)
		{
			_ = item?.OutputFunction(what.Match(
				markupString => item.Encoding().GetBytes(markupString.ToString()),
				str => item.Encoding().GetBytes(str)));
		}

		return ValueTask.CompletedTask;
	}

	public ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> Notify(who.Object().DBRef, what, sender, type);

	public async ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> await Notify([handle], what, sender, type);

	public ValueTask Notify(long[] handles, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
	{
		if (what.Match(
			markupString => MModule.getLength(markupString) == 0,
			str => str.Length == 0
			))
		{
			return ValueTask.CompletedTask;
		}

		var list = handles.Select(_connectionService.Get);

		foreach (var item in list)
		{
			_ = item?.OutputFunction(what.Match(
				markupString => item.Encoding().GetBytes(markupString.ToString()),
				str => item.Encoding().GetBytes(str)));
		}

		return ValueTask.CompletedTask;
	}
}