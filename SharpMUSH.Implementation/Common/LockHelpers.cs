using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The lock writes the six lock commands, <c>@lset</c>, <c>lock()</c> and <c>lset()</c> share:
/// PennMUSH's <c>do_lock</c>, <c>do_unlock</c> and <c>do_lset</c> (<c>src/lock.c</c>).
/// </summary>
/// <remarks>
/// <c>fun_lock</c> with a key is one call to <c>do_lock</c> (<c>src/fundb.c:1311-1335</c>), and
/// <c>fun_lset</c> one call to <c>do_lset</c> (<c>:1296-1308</c>), so the function forms say exactly
/// what the commands say. Written out twice, <c>lock()</c> and <c>lset()</c> reported a refusal in
/// the service's raw English and a success not at all, and every refusal the commands reported went
/// out in English too, bypassing the localisation table.
/// <para>
/// The write and its permission check are <see cref="ILockService"/>'s; what is here is the matching
/// and the reporting around it, in PennMUSH's order.
/// </para>
/// </remarks>
public static class LockHelpers
{
	/// <summary>
	/// PennMUSH <c>do_lock</c> (<c>src/lock.c:702-760</c>), from the point the <c>&lt;object&gt;/&lt;attribute&gt;</c>
	/// form has been handed to <c>@atrlock</c>. An empty key is <c>do_unlock</c>.
	/// </summary>
	/// <param name="type">The lock type as typed, or <see langword="null"/> for the Basic lock.</param>
	public static async ValueTask<Result<Success>> LockAsync(IMUSHCodeParser parser, ILocateService locateService,
		INotifyService notifyService, IPermissionService permissionService, ILockService lockService,
		AnySharpObject executor, string name, string key, string? type)
	{
		if (key.Length == 0)
		{
			return await UnlockAsync(parser, locateService, notifyService, lockService, executor, name, type);
		}

		// lock.c:715-731: match_result, which is silent, so do_lock words both failures itself.
		return await locateService.Locate(parser, executor, executor, name, LocateFlags.All) switch
		{
			AnySharpObject target => await LockLocatedAsync(notifyService, permissionService, lockService, executor,
				target, key, type ?? string.Empty),
			Error<string> { Value: ErrorMessages.Returns.AmbiguousMatch }
				=> await RefusedAsync(notifyService, executor, ErrorMessages.Notifications.DontKnowWhichOneToLock),
			_ => await RefusedAsync(notifyService, executor, ErrorMessages.Notifications.DontSeeWhatYouWantToLock)
		};
	}

	/// <inheritdoc cref="LockAsync"/>
	private static async ValueTask<Result<Success>> LockLocatedAsync(INotifyService notifyService,
		IPermissionService permissionService, ILockService lockService, AnySharpObject executor, AnySharpObject target,
		string key, string type)
	{
		// lock.c:725-727.
		if (!await permissionService.Controls(executor, target))
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Notifications.CantLockThat);
		}

		// lock.c:738-742: the key is parsed before the lock type is checked.
		if (await lockService.BindAsync(key, executor) is not string)
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Notifications.DontUnderstandThatKey);
		}

		return await lockService.ResolveWriteNameAsync(target, type) switch
		{
			string lockName => await ReportedAsync(notifyService, executor, target, lockName,
				await lockService.SetAsync(executor, target, type, key), nameof(ErrorMessages.Notifications.ObjectLocked)),
			Error<string> unresolved => await RefusedAsync(notifyService, executor, unresolved.Value)
		};
	}

	/// <summary>
	/// PennMUSH <c>do_unlock</c> (<c>src/lock.c:662-691</c>), from the point the
	/// <c>&lt;object&gt;/&lt;attribute&gt;</c> form has been handed to <c>@atrlock</c>.
	/// </summary>
	/// <remarks>
	/// <c>match_controlled</c> is <c>noisy_match_result</c> with <c>MAT_CONTROL</c>
	/// (<c>src/match.c:104-107</c>), so an object the unlocker does not control is "Permission denied."
	/// from the matcher. A lock the object only inherits counts as set, as <c>getlock</c> walks the
	/// parent chain; <c>delete_lock</c> then finds nothing of the object's own to remove and succeeds.
	/// </remarks>
	public static async ValueTask<Result<Success>> UnlockAsync(IMUSHCodeParser parser, ILocateService locateService,
		INotifyService notifyService, ILockService lockService, AnySharpObject executor, string name, string? type)
		=> await locateService.LocateAndNotifyIfInvalid(parser, executor, executor, name, ControlledMatch) switch
		{
			AnySharpObject target => await UnlockLocatedAsync(notifyService, lockService, executor, target,
				type ?? string.Empty),
			Error<string> reported => reported,
			_ => new Error<string>(ErrorMessages.Returns.NoMatch)
		};

	/// <inheritdoc cref="UnlockAsync"/>
	private static async ValueTask<Result<Success>> UnlockLocatedAsync(INotifyService notifyService,
		ILockService lockService, AnySharpObject executor, AnySharpObject target, string type)
		=> await lockService.ResolveWriteNameAsync(target, type) switch
		{
			string lockName => await UnlockNamedAsync(notifyService, lockService, executor, target, type, lockName),
			Error<string> unresolved => await RefusedAsync(notifyService, executor, unresolved.Value)
		};

	/// <inheritdoc cref="UnlockAsync"/>
	private static async ValueTask<Result<Success>> UnlockNamedAsync(INotifyService notifyService,
		ILockService lockService, AnySharpObject executor, AnySharpObject target, string type, string lockName)
	{
		// lock.c:672-677.
		if (await lockService.LookupAsync(target, lockName) is not ResolvedLock)
		{
			if (!await target.Object().AreQuietAsync(executor))
			{
				await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectAlreadyUnlocked),
					executor, target.Object().Name, target.Object().DBRef.Number, LockNames.Display(lockName));
			}

			return new Success();
		}

		return await ReportedAsync(notifyService, executor, target, lockName,
			await lockService.UnsetAsync(executor, target, type), nameof(ErrorMessages.Notifications.ObjectUnlocked));
	}

	/// <summary>
	/// PennMUSH <c>do_lset</c> (<c>src/lock.c:905-955</c>): <c>&lt;object&gt;/&lt;lock&gt;</c> and a flag list.
	/// </summary>
	public static async ValueTask<Result<Success>> SetFlagsAsync(IMUSHCodeParser parser,
		ILocateService locateService, INotifyService notifyService, ILockService lockService,
		AnySharpObject executor, string what, string flags)
	{
		// lock.c:912-915.
		var slash = what.IndexOf('/');
		if (slash < 0)
		{
			return await RefusedAsync(notifyService, executor, ErrorMessages.Notifications.NoLockNameGiven);
		}

		var lockName = what[(slash + 1)..];
		return await locateService.LocateAndNotifyIfInvalid(parser, executor, executor, what[..slash], ControlledMatch)
			switch
		{
			AnySharpObject target => await FlagsSetAsync(notifyService, lockService, executor, target, lockName, flags),
			Error<string> reported => reported,
			_ => new Error<string>(ErrorMessages.Returns.NoMatch)
		};
	}

	/// <inheritdoc cref="SetFlagsAsync"/>
	private static async ValueTask<Result<Success>> FlagsSetAsync(INotifyService notifyService,
		ILockService lockService, AnySharpObject executor, AnySharpObject target, string lockName, string flags)
	{
		if (await lockService.SetFlagsAsync(executor, target, lockName, flags) is Error<string> refused)
		{
			return await RefusedAsync(notifyService, executor, refused.Value);
		}

		// lock.c:947-950.
		if (!await target.Object().AreQuietAsync(executor))
		{
			await notifyService.NotifyLocalized(executor, flags.StartsWith('!')
					? nameof(ErrorMessages.Notifications.LockFlagsUnset)
					: nameof(ErrorMessages.Notifications.LockFlagsSet), executor,
				target.Object().Name, LockNames.Display(LockNames.Canonical(lockName)));
		}

		return new Success();
	}

	/// <summary><c>match_controlled</c>: <c>MAT_EVERYTHING | MAT_CONTROL</c>, reported by the matcher.</summary>
	private const LocateFlags ControlledMatch = LocateFlags.All | LocateFlags.OnlyMatchLookerControlledObjects;

	/// <summary>A write's outcome: its refusal, or the success report <paramref name="successKey"/> unless quiet.</summary>
	private static async ValueTask<Result<Success>> ReportedAsync(INotifyService notifyService, AnySharpObject executor,
		AnySharpObject target, string lockName, Result<Success> result, string successKey)
	{
		if (result is Error<string> refused)
		{
			return await RefusedAsync(notifyService, executor, refused.Value);
		}

		if (!await target.Object().AreQuietAsync(executor))
		{
			await notifyService.NotifyLocalized(executor, successKey, executor,
				target.Object().Name, target.Object().DBRef.Number, LockNames.Display(lockName));
		}

		return result;
	}

	/// <summary>
	/// Tells the executor why, through the localisation table when the refusal is one of the lock
	/// service's own sentences, and returns it.
	/// </summary>
	private static async ValueTask<Result<Success>> RefusedAsync(INotifyService notifyService, AnySharpObject executor,
		string message)
	{
		if (RefusalKeys.TryGetValue(message, out var key))
		{
			await notifyService.NotifyLocalized(executor, key, executor);
		}
		else
		{
			await notifyService.Notify(executor, message, executor);
		}

		return new Error<string>(message);
	}

	/// <summary>
	/// The sentences <see cref="ILockService"/> refuses a write with, each to its localisation key. The
	/// service answers in the neutral text so its other callers — a snapshot restore, a package
	/// revert — can carry the reason on; only a player is told it in their own language.
	/// </summary>
	private static readonly Dictionary<string, string> RefusalKeys = new[]
	{
		(ErrorMessages.Notifications.PermissionDenied, nameof(ErrorMessages.Notifications.PermissionDenied)),
		(ErrorMessages.Notifications.UnknownLockType, nameof(ErrorMessages.Notifications.UnknownLockType)),
		(ErrorMessages.Notifications.InvalidLockName, nameof(ErrorMessages.Notifications.InvalidLockName)),
		(ErrorMessages.Notifications.LockNameHasPipe, nameof(ErrorMessages.Notifications.LockNameHasPipe)),
		(ErrorMessages.Notifications.DontUnderstandThatKey, nameof(ErrorMessages.Notifications.DontUnderstandThatKey)),
		(ErrorMessages.Notifications.UnrecognizedLockFlag, nameof(ErrorMessages.Notifications.UnrecognizedLockFlag)),
		(ErrorMessages.Notifications.NoSuchLock, nameof(ErrorMessages.Notifications.NoSuchLock)),
		(ErrorMessages.Notifications.NoLockNameGiven, nameof(ErrorMessages.Notifications.NoLockNameGiven)),
		(ErrorMessages.Notifications.CantLockThat, nameof(ErrorMessages.Notifications.CantLockThat)),
		(ErrorMessages.Notifications.DontSeeWhatYouWantToLock, nameof(ErrorMessages.Notifications.DontSeeWhatYouWantToLock)),
		(ErrorMessages.Notifications.DontKnowWhichOneToLock, nameof(ErrorMessages.Notifications.DontKnowWhichOneToLock)),
	}.ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal);
}
