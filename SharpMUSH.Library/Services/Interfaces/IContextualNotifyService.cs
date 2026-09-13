using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Optional notification routing metadata, preserving the legacy notifier contract.</summary>
public interface IContextualNotifyService
{
	ValueTask NotifyContextAsync(NotificationContext context, MString body, AnySharpObject? speaker,
		INotifyService.NotificationType type, bool prompt = false);
}

public static class ContextualNotifyExtensions
{
	/// <summary>Legacy implementations receive the complete text but cannot propagate routing metadata.</summary>
	public static ValueTask NotifyWithContextAsync(this INotifyService notify, NotificationContext context,
		MString body, AnySharpObject? speaker, INotifyService.NotificationType type, bool prompt = false,
		AnySharpObject? target = null)
	{
		if (notify is IContextualNotifyService contextual)
			return contextual.NotifyContextAsync(context, body, speaker, type, prompt);
		var message = MString.Concat(context.Prefix, body);
		if (target is not null)
			return prompt ? notify.Prompt(target, message, speaker, type) : notify.Notify(target, message, speaker, type);
		return prompt ? notify.Prompt(context.Target, message, speaker, type)
			: notify.Notify(context.Target, message, speaker, type);
	}
}
