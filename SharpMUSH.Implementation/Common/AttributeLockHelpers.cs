using Mediator;
using MoreLinq.Extensions;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The write half of <c>do_atrlock</c> (<c>src/attrib.c:2466</c>), which is the one routine behind
/// both <c>@ATRLOCK</c> and <c>atrlock()</c> in PennMUSH: the attribute-setting gate, the
/// <c>AF_LOCKED</c> change, the reassignment of the attribute to the locker's owner
/// (<c>src/attrib.c:2532</c>) and the report of what happened.
/// </summary>
/// <remarks>
/// What is deliberately <em>not</em> here, because PennMUSH keeps the two callers apart on it:
/// <list type="bullet">
/// <item>the <c>Controls</c> gate — the command asks it unconditionally, before it even looks the
/// attribute up, while a one-argument <c>atrlock()</c> is a read and only asks when it is about to
/// change something;</item>
/// <item>the lookup failure — a missing or unreadable attribute is a bare <c>#-1</c> and nothing
/// said from the function (<c>src/fundb.c:2452-2456</c>) but <c>AttributeNotFound</c> plus
/// <c>#-1 NO MATCH</c> from the command;</item>
/// <item>the read form — <c>"1"</c>/<c>"0"</c> from the function, a notification from the command.</item>
/// </list>
/// </remarks>
internal static class AttributeLockHelpers
{
	/// <summary>PennMUSH's <c>AF_LOCKED</c>, as SharpMUSH spells its attribute flags.</summary>
	public const string LockedAttributeFlag = "LOCKED";

	/// <summary>Whether the attribute at the end of <paramref name="chain"/> carries the lock.</summary>
	public static bool IsLocked(SharpAttribute[] chain)
		=> chain.Last().Flags.Any(flag => flag.Name.Equals(LockedAttributeFlag, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Sets or clears the lock, having already established that <paramref name="executor"/> controls
	/// <paramref name="target"/> and that <paramref name="chain"/> is the attribute it named.
	/// </summary>
	/// <returns>Empty on success, otherwise the failure's <c>#-1</c> string.</returns>
	public static async ValueTask<CallState> ChangeAsync(
		IPermissionService permissionService,
		IAttributeService attributeService,
		INotifyService notifyService,
		IMediator mediator,
		AnySharpObject executor,
		AnySharpObject target,
		string attributeName,
		SharpAttribute[] chain,
		bool lockIt)
	{
		if (!await permissionService.CanSet(executor, target, chain))
		{
			await notifyService.Notify(executor, "You need to be able to set the attribute to change its lock.", executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var changed = lockIt
			? await attributeService.SetAttributeFlagAsync(executor, target, attributeName, LockedAttributeFlag)
			: await attributeService.UnsetAttributeFlagAsync(executor, target, attributeName, LockedAttributeFlag);

		if (changed is Error<string> error)
		{
			await notifyService.Notify(executor, error.Value, executor);
			return new CallState(error.Value);
		}

		// do_atrlock hands the attribute to the locker's owner, so a locked attribute cannot be
		// re-set by whoever owned it before.
		if (lockIt)
		{
			var owner = await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
			if (!await mediator.Send(new SetAttributeOwnerCommand(target.Object().DBRef,
				chain.Select(item => item.Name).ToArray(), owner), ExecutionBudget.CurrentToken))
			{
				await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFound), executor);
				return new CallState(ErrorMessages.Returns.NoMatch);
			}
		}

		await notifyService.NotifyLocalized(executor, lockIt
			? nameof(ErrorMessages.Notifications.AttributeLocked)
			: nameof(ErrorMessages.Notifications.AttributeUnlocked), executor);

		return CallState.Empty;
	}
}
