using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// "Permission denied." with the reason: the permission the executor lacks, the object it does not
/// control, or that only God may. A command's refusal goes through one of these, so a player learns
/// what would let them, and staff what to grant. Each tells the refused object why and answers
/// <see cref="PermissionRefusal.Return"/>, for a command to return.
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
		return new PermissionRefusal(key, string.Empty, args).Return;
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

	/// <summary>
	/// What a function refused this way returns: <see cref="ErrorMessages.Returns.PermissionDenied"/> and, after a colon,
	/// the reason in capitals without any object's name, so <c>#-1 PERMISSION DENIED*</c> still matches it:
	/// <c>#-1 PERMISSION DENIED: NEEDS GAME.WIZARD OR GAME.HALT</c>.
	/// </summary>
	public CallState Return => new(ReturnText);

	/// <summary><see cref="Return"/>'s text, for a caller that answers with an <c>Error&lt;string&gt;</c>.</summary>
	public string ReturnText => Code is { } code ? $"{ErrorMessages.Returns.PermissionDenied}: {code}" : ErrorMessages.Returns.PermissionDenied;

	/// <summary>The reason as <see cref="Return"/> carries it, or null for a refusal with no short form.</summary>
	public string? Code => Key switch
	{
		nameof(ErrorMessages.Notifications.PermissionDeniedNeedsPermission) => $"NEEDS {Arg(0)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedNeedsAnyPermission) => $"NEEDS {Alternatives(0)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedGodOnly) => "GOD ONLY",
		nameof(ErrorMessages.Notifications.PermissionDeniedNotControlled)
			or nameof(ErrorMessages.Notifications.PermissionDeniedNotControlledMatch)
			or nameof(ErrorMessages.Notifications.PermissionDeniedLockNotControlled) => "NOT CONTROLLED",
		nameof(ErrorMessages.Notifications.PermissionDeniedNotControlledNeeds) => $"NOT CONTROLLED, NEEDS {Arg(1)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedNotControlledNeedsAny)
			or nameof(ErrorMessages.Notifications.PermissionDeniedQueueEntry) => $"NOT CONTROLLED, NEEDS {Alternatives(1)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedNotControlledNorFlag) => $"NOT CONTROLLED, NOT {Arg(1)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedNotOwnerNeeds) => $"NOT OWNED, NEEDS {Arg(1)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedNotOwnerNeedsAny) => $"NOT OWNED, NEEDS {Alternatives(1)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedLock) => $"{Arg(1)} LOCK",
		nameof(ErrorMessages.Notifications.PermissionDeniedCommandLock) => "COMMAND LOCK",
		nameof(ErrorMessages.Notifications.PermissionDeniedGuest)
			or nameof(ErrorMessages.Notifications.PermissionDeniedCommandGuest) => "GUEST",
		nameof(ErrorMessages.Notifications.PermissionDeniedCommandGagged) => "GAGGED",
		nameof(ErrorMessages.Notifications.PermissionDeniedCommandApp) => "APPLICATION",
		nameof(ErrorMessages.Notifications.PermissionDeniedNeedsAccount) => "NEEDS AN ACCOUNT",
		nameof(ErrorMessages.Notifications.PermissionDeniedProtectedWizard) => "NEEDS GAME.WIZARD",
		nameof(ErrorMessages.Notifications.PermissionDeniedLockOfGod) => "GOD ONLY",
		nameof(ErrorMessages.Notifications.PermissionDeniedLockWizard) => "WIZARD LOCK",
		nameof(ErrorMessages.Notifications.PermissionDeniedLockOwner) => "OWNER LOCK",
		nameof(ErrorMessages.Notifications.PermissionDeniedLockLocked) => "LOCKED",
		nameof(ErrorMessages.Notifications.PermissionDeniedLockSetWizard) => "NEEDS GAME.WIZARD OR GAME.ROYALTY OR GAME.SEE_ALL",
		nameof(ErrorMessages.Notifications.PermissionDeniedNotARoom) => "NOT A ROOM",
		nameof(ErrorMessages.Notifications.PermissionDeniedTeleportDestination) => "NOT CONTROLLED, NOT JUMP_OK",
		nameof(ErrorMessages.Notifications.PermissionDeniedTeleportFixed) => "FIXED",
		nameof(ErrorMessages.Notifications.PermissionDeniedChownCarry) => "NOT CARRIED",
		nameof(ErrorMessages.Notifications.PermissionDeniedFlagType) => $"{Arg(0)} NOT FOR {Arg(1)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedFlagLocked) => $"{Arg(0)} LOCKED",
		nameof(ErrorMessages.Notifications.PermissionDeniedFlagRestricted) => $"{Arg(0)} NEEDS {Alternatives(1)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedGagWizard) => "WIZARD",
		nameof(ErrorMessages.Notifications.PermissionDeniedLinkedExit) => "NOT CONTROLLED, ALREADY LINKED",
		nameof(ErrorMessages.Notifications.PermissionDeniedExamine) => "CANNOT EXAMINE",
		nameof(ErrorMessages.Notifications.PermissionDeniedLocate) => "CANNOT LOCATE",
		nameof(ErrorMessages.Notifications.PermissionDeniedCannotUse) => $"CANNOT USE {Arg(0)}",
		nameof(ErrorMessages.Notifications.PermissionDeniedChannelRead) => "CANNOT READ CHANNEL",
		nameof(ErrorMessages.Notifications.PermissionDeniedChannelModify) => "CANNOT CHANGE CHANNEL",
		nameof(ErrorMessages.Notifications.PermissionDeniedReadAttribute) => "CANNOT READ ATTRIBUTE",
		nameof(ErrorMessages.Notifications.PermissionDeniedReadLock) => $"CANNOT READ {Arg(1)} LOCK",
		nameof(ErrorMessages.Notifications.PermissionDeniedTag) => "TAG NOT ALLOWED",
		_ => null
	};

	private string Arg(int index) => (Args.ElementAtOrDefault(index)?.ToString() ?? string.Empty).ToUpperInvariant();

	/// <summary>A comma-joined list of scopes as "A OR B".</summary>
	private string Alternatives(int index) => Arg(index).Replace(", ", " OR ", StringComparison.Ordinal);

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

	/// <summary>Does not control what queued task <paramref name="pid"/>, nor holds any of <paramref name="scopes"/>.</summary>
	public static PermissionRefusal QueueEntry(long pid, params string[] scopes)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedQueueEntry), ErrorMessages.Notifications.PermissionDeniedQueueEntry,
			pid, string.Join(", ", scopes));

	public static PermissionRefusal Examine(AnySharpObject target)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedExamine), ErrorMessages.Notifications.PermissionDeniedExamine,
			target.Object().Name);

	/// <summary>Cannot locate what was asked for; <paramref name="name"/> is the text the caller gave.</summary>
	public static PermissionRefusal Locate(string name)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedLocate), ErrorMessages.Notifications.PermissionDeniedLocate, name);

	/// <summary>Cannot run <paramref name="command"/>, which the function stands in for.</summary>
	public static PermissionRefusal CannotUse(string command)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedCannotUse), ErrorMessages.Notifications.PermissionDeniedCannotUse, command);

	public static PermissionRefusal ChannelRead(string channel)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedChannelRead), ErrorMessages.Notifications.PermissionDeniedChannelRead, channel);

	public static PermissionRefusal ChannelModify(string channel)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedChannelModify), ErrorMessages.Notifications.PermissionDeniedChannelModify, channel);

	public static PermissionRefusal ReadAttribute(AnySharpObject target, string attribute)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedReadAttribute), ErrorMessages.Notifications.PermissionDeniedReadAttribute,
			attribute, target.Object().Name);

	public static PermissionRefusal ReadLock(AnySharpObject target, string lockName)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedReadLock), ErrorMessages.Notifications.PermissionDeniedReadLock,
			target.Object().Name, lockName);

	public static PermissionRefusal Tag(string tag)
		=> new(nameof(ErrorMessages.Notifications.PermissionDeniedTag), ErrorMessages.Notifications.PermissionDeniedTag, tag);
}

/// <summary>Allowed, or refused for a reason.</summary>
public union Permission(Success, PermissionRefusal);
