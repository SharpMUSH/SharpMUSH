using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// "Permission denied." with the reason: the permission the executor lacks, the object it does not
/// control, or that only God may. A command's refusal goes through one of these, so a player learns
/// what would let them, and staff what to grant. Each tells the refused object why and answers
/// <see cref="ErrorMessages.Returns.PermissionDenied"/>, for a command to return.
/// </summary>
public static class PermissionDeniedExtensions
{
	/// <summary>Refused for want of a permission; given several, any one of them would do.</summary>
	public static ValueTask<CallState> PermissionDeniedNeeds(this INotifyService notifyService, AnySharpObject who,
		params string[] scopes)
		=> notifyService.PermissionDenied(who, PermissionRefusal.Needs(scopes));

	/// <summary>
	/// Refused because <paramref name="who"/> does not control <paramref name="target"/>; given
	/// <paramref name="scopes"/>, nor holds any of the permissions that would have done instead.
	/// </summary>
	public static ValueTask<CallState> PermissionDeniedNotControlled(this INotifyService notifyService, AnySharpObject who,
		AnySharpObject target, params string[] scopes)
		=> notifyService.PermissionDenied(who, PermissionRefusal.NotControlled(target, scopes));

	/// <summary>Refused because <paramref name="who"/> neither controls <paramref name="target"/> nor finds it set <paramref name="flag"/>.</summary>
	public static ValueTask<CallState> PermissionDeniedNotControlledNorFlag(this INotifyService notifyService, AnySharpObject who,
		AnySharpObject target, string flag)
		=> notifyService.PermissionDenied(who, PermissionRefusal.NotControlledNorFlag(target, flag));

	/// <summary>Refused because only God may.</summary>
	public static ValueTask<CallState> PermissionDeniedGodOnly(this INotifyService notifyService, AnySharpObject who)
		=> notifyService.PermissionDenied(who, PermissionRefusal.GodOnly());

	/// <summary>Refused for the reason the notification <paramref name="key"/> gives.</summary>
	public static async ValueTask<CallState> PermissionDenied(this INotifyService notifyService, AnySharpObject who,
		string key, params object[] args)
	{
		await notifyService.NotifyLocalized(who, key, who, args);
		return new CallState(ErrorMessages.Returns.PermissionDenied);
	}

	/// <summary>Refused for <paramref name="refusal"/>'s reason.</summary>
	public static ValueTask<CallState> PermissionDenied(this INotifyService notifyService, AnySharpObject who,
		PermissionRefusal refusal)
		=> notifyService.PermissionDenied(who, refusal.Key, refusal.Args);
}

/// <summary>
/// A refusal's reason carried as a value, for code that decides why before it decides how to say it: the
/// notification <see cref="Key"/>, the English <see cref="Format"/> it stands for, and the arguments.
/// </summary>
public sealed record PermissionRefusal(string Key, string Format, params object[] Args)
{
	/// <summary>The reason in English, for a message that is not localised (a fail_lock default, say).</summary>
	public string Text => string.Format(null, Format, Args);

	public static PermissionRefusal GodOnly()
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedGodOnly), ErrorMessages.Notifications.PermissionDeniedGodOnly);

	/// <summary>Wants a permission; given several, any one of them would do.</summary>
	public static PermissionRefusal Needs(params string[] scopes)
		=> scopes is [var scope]
			? new(nameof(ErrorMessages.Notifications.PermissionDeniedNeedsPermission), ErrorMessages.Notifications.PermissionDeniedNeedsPermission, scope)
			: new(nameof(ErrorMessages.Notifications.PermissionDeniedNeedsAnyPermission), ErrorMessages.Notifications.PermissionDeniedNeedsAnyPermission,
				string.Join(", ", scopes));

	/// <summary>Does not control <paramref name="target"/>; given <paramref name="scopes"/>, nor holds any of them.</summary>
	public static PermissionRefusal NotControlled(AnySharpObject target, params string[] scopes)
		=> scopes switch
		{
			[] => new(nameof(ErrorMessages.Notifications.PermissionDeniedNotControlled), ErrorMessages.Notifications.PermissionDeniedNotControlled,
				target.Object().Name),
			[var scope] => new(nameof(ErrorMessages.Notifications.PermissionDeniedNotControlledNeeds),
				ErrorMessages.Notifications.PermissionDeniedNotControlledNeeds, target.Object().Name, scope),
			_ => new(nameof(ErrorMessages.Notifications.PermissionDeniedNotControlledNeedsAny),
				ErrorMessages.Notifications.PermissionDeniedNotControlledNeedsAny, target.Object().Name, string.Join(", ", scopes))
		};

	public static PermissionRefusal NotControlledNorFlag(AnySharpObject target, string flag)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedNotControlledNorFlag),
			ErrorMessages.Notifications.PermissionDeniedNotControlledNorFlag, target.Object().Name, flag);

	public static PermissionRefusal Lock(AnySharpObject target, string lockName)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedLock), ErrorMessages.Notifications.PermissionDeniedLock,
			target.Object().Name, lockName);
}

/// <summary>Allowed, or refused for a reason.</summary>
public union Permission(Success, PermissionRefusal);
