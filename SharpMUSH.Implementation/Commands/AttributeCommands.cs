using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@ATRLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object/attribute", "on-off"])]
	public static async ValueTask<Option<CallState>> AttributeLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (!args.TryGetValue("0", out var objAttrArg))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedObjectAttributePair), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var objAttrText = MModule.plainText(objAttrArg.Message!);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText);

		if (!split.TryPickT0(out var details, out _) || string.IsNullOrEmpty(details.Attribute))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedObjectAttributePair), executor);
			return new CallState(ErrorMessages.Returns.InvalidFormat);
		}

		var (dbref, attrName) = details;

		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, dbref, LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var targetObject = locate.AsSharpObject;

		var attribute = await AttributeService!.GetAttributeAsync(executor, targetObject, attrName,
		IAttributeService.AttributeMode.Read);

		if (!attribute.IsAttribute)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFound), executor);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		if (!args.TryGetValue("1", out var valueArg))
		{
			var isLocked = attribute.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
			await NotifyService!.NotifyLocalized(executor,
				isLocked
					? nameof(ErrorMessages.Notifications.AttributeIsLocked)
					: nameof(ErrorMessages.Notifications.AttributeIsUnlocked),
				executor);
			return new CallState(string.Empty);
		}

		var lockValue = MModule.plainText(valueArg.Message!).ToLowerInvariant();
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
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidArgument), executor);
			return new CallState(ErrorMessages.Returns.InvalidValue);
		}

		var canSet = await PermissionService!.CanSet(executor, targetObject);
		if (!canSet)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (shouldLock)
		{
			await AttributeService!.SetAttributeFlagAsync(executor, targetObject, attrName, "LOCKED");

			if (executor.IsPlayer)
			{
				var currentValue = attribute.AsAttribute.Last().Value;
				await AttributeService!.SetAttributeAsync(executor, targetObject, attrName, currentValue);
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeLocked), executor);
		}
		else
		{
			await AttributeService!.UnsetAttributeFlagAsync(executor, targetObject, attrName, "LOCKED");
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeUnlocked), executor);
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@CPATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
	MinArgs = 2, MaxArgs = int.MaxValue, ParameterNames = ["source/attribute", "destination/attribute"])]
	public static async ValueTask<Option<CallState>> CopyAttribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var copyFlags = !parser.CurrentState.Switches.Contains("NOFLAGCOPY");

		if (!args.TryGetValue("0", out var sourceArg) || !args.TryGetValue("1", out _))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidArgumentsToCommandFormat), executor, "@cpattr");
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var sourceText = MModule.plainText(sourceArg.Message!);
		var sourceSplit = HelperFunctions.SplitDbRefAndOptionalAttr(sourceText);

		if (!sourceSplit.TryPickT0(out var sourceDetails, out _) || string.IsNullOrEmpty(sourceDetails.Attribute))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidSourceFormat), executor);
			return new CallState(ErrorMessages.Returns.InvalidSource);
		}

		var (sourceDbref, sourceAttr) = sourceDetails;

		var sourceLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, sourceDbref, LocateFlags.All);

		if (sourceLocate.IsError)
		{
			return sourceLocate.AsError;
		}

		var sourceObject = sourceLocate.AsSharpObject;

		var sourceAttribute = await AttributeService!.GetAttributeAsync(executor, sourceObject, sourceAttr,
		IAttributeService.AttributeMode.Read);

		if (!sourceAttribute.IsAttribute)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFoundOnSourceFormat), executor, sourceAttr);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var attrValue = sourceAttribute.AsAttribute.Last().Value;
		var attrFlags = sourceAttribute.AsAttribute.Last().Flags.ToList();

		// With CB.RSArgs + CB.EqSplit, each comma-separated destination becomes a separate arg
		// starting at index 1. Collect all destination args in order.
		var destinations = args
			.Where(kvp => int.TryParse(kvp.Key, out var k) && k >= 1)
			.OrderBy(kvp => int.Parse(kvp.Key))
			.Select(kvp => MModule.plainText(kvp.Value.Message!).Trim())
			.Where(d => !string.IsNullOrEmpty(d));

		int copiedCount = 0;

		foreach (var dest in destinations)
		{
			var destSplit = HelperFunctions.SplitDbRefAndOptionalAttr(dest);

			if (!destSplit.TryPickT0(out var destDetails, out _))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidDestinationFormat), executor, dest);
				continue;
			}

			var (destDbref, destAttr) = destDetails;
			var targetAttrName = string.IsNullOrEmpty(destAttr) ? sourceAttr : destAttr;

			var destLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			executor, executor, destDbref, LocateFlags.All);

			if (destLocate.IsError)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CouldNotFindDestination), executor, destDbref);
				continue;
			}

			var destObject = destLocate.AsSharpObject;

			var canSet = await PermissionService!.CanSet(executor, destObject);
			if (!canSet)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDeniedSetAttribute), executor, destDbref);
				continue;
			}

			var setResult = await AttributeService!.SetAttributeAsync(executor, destObject, targetAttrName, attrValue);

			if (setResult.IsT1)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToCopyAttributeToFormat), executor, destDbref, setResult.AsT1.Value);
				continue;
			}

			if (copyFlags)
			{
				foreach (var flag in attrFlags)
				{
					await AttributeService!.SetAttributeFlagAsync(executor, destObject, targetAttrName, flag.Name);
				}
			}

			copiedCount++;
		}

		if (copiedCount > 0)
		{
			var destWord = copiedCount == 1 ? "destination" : "destinations";
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCopiedToDestinationsFormat), executor, copiedCount, destWord);
		}
		else
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToCopyAttributeAny), executor);
			return new CallState(ErrorMessages.Returns.CopyFailed);
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@MVATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
	MinArgs = 2, MaxArgs = int.MaxValue, ParameterNames = ["source/attribute", "destination/attribute"])]
	public static async ValueTask<Option<CallState>> MoveAttribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var copyFlags = !parser.CurrentState.Switches.Contains("NOFLAGCOPY");

		if (!args.TryGetValue("0", out var sourceArg) || !args.TryGetValue("1", out _))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidArgumentsToCommandFormat), executor, "@mvattr");
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var sourceText = MModule.plainText(sourceArg.Message!);
		var sourceSplit = HelperFunctions.SplitDbRefAndOptionalAttr(sourceText);

		if (!sourceSplit.TryPickT0(out var sourceDetails, out _) || string.IsNullOrEmpty(sourceDetails.Attribute))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidSourceFormat), executor);
			return new CallState(ErrorMessages.Returns.InvalidSource);
		}

		var (sourceDbref, sourceAttr) = sourceDetails;

		var sourceLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, sourceDbref, LocateFlags.All);

		if (sourceLocate.IsError)
		{
			return sourceLocate.AsError;
		}

		var sourceObject = sourceLocate.AsSharpObject;

		var sourceAttribute = await AttributeService!.GetAttributeAsync(executor, sourceObject, sourceAttr,
		IAttributeService.AttributeMode.Read);

		if (!sourceAttribute.IsAttribute)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFoundOnSourceFormat), executor, sourceAttr);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var attrValue = sourceAttribute.AsAttribute.Last().Value;
		var attrFlags = sourceAttribute.AsAttribute.Last().Flags.ToList();

		// With CB.RSArgs + CB.EqSplit, each comma-separated destination becomes a separate arg
		// starting at index 1. Collect all destination args in order.
		var destinations = args
			.Where(kvp => int.TryParse(kvp.Key, out var k) && k >= 1)
			.OrderBy(kvp => int.Parse(kvp.Key))
			.Select(kvp => MModule.plainText(kvp.Value.Message!).Trim())
			.Where(d => !string.IsNullOrEmpty(d));

		int copiedCount = 0;

		foreach (var dest in destinations)
		{
			var destSplit = HelperFunctions.SplitDbRefAndOptionalAttr(dest);

			if (!destSplit.TryPickT0(out var destDetails, out _))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidDestinationFormat), executor, dest);
				continue;
			}

			var (destDbref, destAttr) = destDetails;
			var targetAttrName = string.IsNullOrEmpty(destAttr) ? sourceAttr : destAttr;

			var destLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			executor, executor, destDbref, LocateFlags.All);

			if (destLocate.IsError)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CouldNotFindDestination), executor, destDbref);
				continue;
			}

			var destObject = destLocate.AsSharpObject;

			var canSet = await PermissionService!.CanSet(executor, destObject);
			if (!canSet)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDeniedSetAttribute), executor, destDbref);
				continue;
			}

			var setResult = await AttributeService!.SetAttributeAsync(executor, destObject, targetAttrName, attrValue);

			if (setResult.IsT1)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToCopyAttributeToFormat), executor, destDbref, setResult.AsT1.Value);
				continue;
			}

			if (copyFlags)
			{
				foreach (var flag in attrFlags)
				{
					await AttributeService!.SetAttributeFlagAsync(executor, destObject, targetAttrName, flag.Name);
				}
			}

			copiedCount++;
		}

		if (copiedCount > 0)
		{
			var clearResult = await AttributeService!.ClearAttributeAsync(executor, sourceObject, sourceAttr,
			IAttributeService.AttributePatternMode.Exact,
			IAttributeService.AttributeClearMode.Safe);

			var destWord = copiedCount == 1 ? "destination" : "destinations";
			if (clearResult.IsT1)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeMovedFailedRemoveFormat), executor, copiedCount, destWord, clearResult.AsT1.Value);
			}
			else
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeMovedToFormat), executor, copiedCount, destWord);
			}
		}
		else
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToMoveAttributeAny), executor);
			return new CallState(ErrorMessages.Returns.MoveFailed);
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@ATRCHOWN", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 2, MaxArgs = 2, ParameterNames = ["object/attribute", "player"])]
	public static async ValueTask<Option<CallState>> ChangeAttributeOwner(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (!args.TryGetValue("0", out var objAttrArg) || !args.TryGetValue("1", out var ownerArg))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedObjectAttributePair), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var objAttrText = MModule.plainText(objAttrArg.Message!);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText);

		if (!split.TryPickT0(out var details, out _) || string.IsNullOrEmpty(details.Attribute))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedObjectAttributePair), executor);
			return new CallState(ErrorMessages.Returns.InvalidFormat);
		}

		var (dbref, attrName) = details;

		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, dbref, LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var targetObject = locate.AsSharpObject;

		var attribute = await AttributeService!.GetAttributeAsync(executor, targetObject, attrName,
		IAttributeService.AttributeMode.Read);

		if (!attribute.IsAttribute)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFound), executor);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var newOwnerText = MModule.plainText(ownerArg.Message!);
		var ownerLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, newOwnerText, LocateFlags.All);

		if (ownerLocate.IsError)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantFindThatPlayer), executor);
			return ownerLocate.AsError;
		}

		var newOwnerObject = ownerLocate.AsSharpObject;

		SharpPlayer newOwnerPlayer;
		if (newOwnerObject.IsPlayer)
		{
			newOwnerPlayer = newOwnerObject.AsPlayer;
		}
		else
		{
			newOwnerPlayer = await newOwnerObject.Object().Owner.WithCancellation(CancellationToken.None);
		}

		// Mortals can only chown to themselves; wizards can chown to anyone.
		var isWizard = await executor.HasPower("WIZARD") || await executor.HasFlag("WIZARD");
		var canSet = await PermissionService!.CanSet(executor, targetObject);

		if (!canSet)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!isWizard)
		{
			if (executor.IsPlayer && newOwnerPlayer.Object.DBRef != executor.AsPlayer.Object.DBRef)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CanOnlyChownToYourself), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}
			else if (!executor.IsPlayer)
			{
				var executorOwner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
				if (executorOwner.Object.DBRef != newOwnerPlayer.Object.DBRef)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CanOnlyChownToYourself), executor);
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}
			}
		}

		var currentValue = attribute.AsAttribute.Last().Value;
		var setResult = await AttributeService!.SetAttributeAsync(executor, targetObject, attrName, currentValue);

		if (setResult.IsT1)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToChangeOwnershipFormat), executor, setResult.AsT1.Value);
			return new CallState(ErrorMessages.Returns.Failed);
		}

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeOwnerChanged), executor);
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@WIPE", Switches = [], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> Wipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (args.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WipeWhat), executor);
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		var objAttr = MModule.plainText(args["0"].Message!);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttr);

		if (!split.TryPickT0(out var details, out _))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return new CallState(ErrorMessages.Returns.InvalidObject);
		}

		var (dbref, maybeAttribute) = details;

		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		executor,
		executor,
		dbref,
		LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var targetObject = locate.AsSharpObject;

		var canModify = await PermissionService!.Controls(executor, targetObject);
		if (!canModify)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var isSafe = await targetObject.HasFlag("SAFE");
		if (isSafe)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectIsProtectedSafe), executor);
			return new CallState(ErrorMessages.Returns.Safe);
		}

		if (string.IsNullOrEmpty(maybeAttribute))
		{
			await AttributeService!.ClearAttributeAsync(executor, targetObject, "**",
			IAttributeService.AttributePatternMode.Wildcard,
			IAttributeService.AttributeClearMode.Safe);
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributesWiped), executor);
			return new CallState(string.Empty);
		}
		else
		{
			await AttributeService!.ClearAttributeAsync(executor, targetObject, maybeAttribute,
			IAttributeService.AttributePatternMode.Wildcard,
			IAttributeService.AttributeClearMode.Safe);
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WipedAttributes), executor, maybeAttribute);
			return new CallState(string.Empty);
		}
	}

}
