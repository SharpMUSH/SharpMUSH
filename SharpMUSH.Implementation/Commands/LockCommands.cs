using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using System.Buffers;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@LOCK", Switches = ["*"], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> Lock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, null, false);

	[SharpCommand(Name = "@UNLOCK", Switches = ["*"], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> Unlock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, null, true);

	[SharpCommand(Name = "@ELOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> ELock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, "Enter", false);

	[SharpCommand(Name = "@EUNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> EUnlock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, "Enter", true);

	[SharpCommand(Name = "@ULOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> ULock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, "Use", false);

	[SharpCommand(Name = "@UUNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> UUnlock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, "Use", true);

	private async ValueTask<Option<CallState>> ChangeLockAsync(IMUSHCodeParser parser, string? fixedType, bool unlock)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		if (!args.TryGetValue("0", out var targetArg)) return new CallState(ErrorMessages.Returns.InvalidArguments);
		var targetText = targetArg.Message!.ToPlainText();
		var slash = targetText.IndexOf('/');
		var attributeName = slash < 0 ? null : targetText[(slash + 1)..];
		if (slash >= 0) targetText = targetText[..slash];
		var expression = args.TryGetValue("1", out var key) ? key.Message!.ToPlainText() : string.Empty;
		var type = fixedType ?? parser.CurrentState.Switches.FirstOrDefault() ?? "Basic";
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetText, LocateFlags.All,
			async target =>
			{
				if (attributeName is not null)
				{
					var attributeArgs = new Dictionary<string, CallState> { ["1"] = new CallState(unlock ? "off" : "on") };
					var result = await AttributeLockAsync(executor, target, attributeArgs, attributeName);
					return result is CallState state ? state : CallState.Empty;
				}
				var removing = unlock || expression.Length == 0;
				var nameResult = await LockService.ResolveWriteNameAsync(target, type, ExecutionBudget.CurrentToken);
				if (nameResult is Error<string> nameError)
				{
					await NotifyService.Notify(executor, nameError.Value, executor);
					return CallState.Empty;
				}
				var name = nameResult is string value ? value : type;
				var existed = target.Object().Locks.ContainsKey(name);
				var changed = removing
					? await LockService.UnsetAsync(executor, target, type)
					: await LockService.SetAsync(executor, target, type, expression);
				if (changed is Error<string> error)
				{
					await NotifyService.Notify(executor, error.Value, executor);
					return CallState.Empty;
				}
				if (!await target.Object().AreQuietAsync(executor))
				{
					if (removing && !existed)
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectAlreadyUnlocked), executor,
							target.Object().Name, target.Object().DBRef.Number, LockNames.Display(name));
					else
						await NotifyService.NotifyLocalized(executor, removing
							? nameof(ErrorMessages.Notifications.ObjectUnlocked)
							: nameof(ErrorMessages.Notifications.ObjectLocked), executor,
							target.Object().Name, target.Object().DBRef.Number, LockNames.Display(name));
				}
				return CallState.Empty;
			});
	}

	[SharpCommand(Name = "@LSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object/lock", "flags"])]
	public async ValueTask<Option<CallState>> LockSet(IMUSHCodeParser parser, SharpCommandAttribute attribute)
	{
		if (await RejectIfTooFewArguments(parser, attribute) is { } error) return error;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var target = args["0"].Message!.ToPlainText();
		var slash = target.IndexOf('/');
		if (slash < 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoLockNameGiven), executor);
			return CallState.Empty;
		}
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, target[..slash], LocateFlags.All,
			async obj =>
			{
				var flags = args["1"].Message!.ToPlainText();
				var result = await LockService.SetFlagsAsync(executor, obj, target[(slash + 1)..], flags);
				if (result is Error<string> failure) await NotifyService.Notify(executor, failure.Value, executor);
				else if (!await obj.Object().AreQuietAsync(executor))
					await NotifyService.NotifyLocalized(executor, flags.StartsWith('!')
						? nameof(ErrorMessages.Notifications.LockFlagsUnset)
						: nameof(ErrorMessages.Notifications.LockFlagsSet), executor,
						obj.Object().Name, LockNames.Display(target[(slash + 1)..]));
				return CallState.Empty;
			});
	}

	[SharpCommand(Name = "@ATRLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object/attribute", "on-off"])]
	public async ValueTask<Option<CallState>> AttributeLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		if (!args.TryGetValue("0", out var objAttrArg))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedObjectAttributePair), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var objAttrText = objAttrArg.Message!.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText) is not { Object: var dbref, Attribute: { } attrName })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedObjectAttributePair), executor);
			return new CallState(ErrorMessages.Returns.InvalidFormat);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, dbref, LocateFlags.All) switch
		{
			AnySharpObject targetObject => await AttributeLockAsync(executor, targetObject, args, attrName),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> AttributeLockAsync(AnySharpObject executor, AnySharpObject targetObject,
		Dictionary<string, CallState> args, string attrName)
	{
		if (!await PermissionService.Controls(executor, targetObject))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (await AttributeService.GetAttributeAsync(executor, targetObject, attrName,
				IAttributeService.AttributeMode.Read, false) is not SharpAttribute[] attribute)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFound), executor);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		if (!args.TryGetValue("1", out var valueArg) || string.IsNullOrEmpty(valueArg.Message?.ToPlainText()))
		{
			var isLocked = attribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
			await NotifyService.NotifyLocalized(executor,
				isLocked
					? nameof(ErrorMessages.Notifications.AttributeIsLocked)
					: nameof(ErrorMessages.Notifications.AttributeIsUnlocked),
				executor);
			return new CallState(string.Empty);
		}

		var lockValue = valueArg.Message!.ToPlainText().ToLowerInvariant();
		bool shouldLock;

		if (lockValue == "on" || lockValue == "1" || lockValue == "yes")
		{
			shouldLock = true;
		}
		else if (lockValue == "off" || lockValue == "0" || lockValue == "no")
		{
			shouldLock = false;
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidArgument), executor);
			return new CallState(ErrorMessages.Returns.InvalidValue);
		}

		if (!await PermissionService.CanSet(executor, targetObject, attribute))
		{
			await NotifyService.Notify(executor, "You need to be able to set the attribute to change its lock.", executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var changed = shouldLock
			? await AttributeService.SetAttributeFlagAsync(executor, targetObject, attrName, "LOCKED")
			: await AttributeService.UnsetAttributeFlagAsync(executor, targetObject, attrName, "LOCKED");
		if (changed is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return new CallState(error.Value);
		}
		if (shouldLock)
		{
			var owner = await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
			if (!await Mediator.Send(new SetAttributeOwnerCommand(targetObject.Object().DBRef,
				attribute.Select(item => item.Name).ToArray(), owner), ExecutionBudget.CurrentToken))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFound), executor);
				return new CallState(ErrorMessages.Returns.NoMatch);
			}
		}
		await NotifyService.NotifyLocalized(executor, shouldLock
			? nameof(ErrorMessages.Notifications.AttributeLocked)
			: nameof(ErrorMessages.Notifications.AttributeUnlocked), executor);

		return new CallState(string.Empty);
	}
}
