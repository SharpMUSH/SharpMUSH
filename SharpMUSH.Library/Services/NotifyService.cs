using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.INotifyService;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Notifies objects and sends telnet data.
/// This is now a wrapper that delegates to an inner INotifyService implementation
/// (typically MessageQueueNotifyService in distributed architecture).
/// </summary>
/// <param name="_innerService">The actual notify service implementation to delegate to</param>
public class NotifyService(INotifyService _innerService) : INotifyService
{
	public ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> _innerService.Notify(who, what, sender, type);

	public ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> _innerService.Notify(who, what, sender, type);

	public ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> _innerService.Notify(handle, what, sender, type);

	public ValueTask Notify(long[] handles, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> _innerService.Notify(handles, what, sender, type);

	public ValueTask Prompt(DBRef who, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> _innerService.Prompt(who, what, sender, type);

	public ValueTask Prompt(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> _innerService.Prompt(who, what, sender, type);

	public ValueTask Prompt(long handle, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> _innerService.Prompt(handle, what, sender, type);

	public ValueTask Prompt(long[] handles, OneOf<MString, string> what, AnySharpObject? sender, NotificationType type = NotificationType.Announce)
		=> _innerService.Prompt(handles, what, sender, type);
}