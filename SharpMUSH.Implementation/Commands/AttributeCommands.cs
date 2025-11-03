using System.Linq;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@ATRLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> AttributeLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (!args.TryGetValue("0", out var objAttrArg))
		{
			await NotifyService!.Notify(executor, "Invalid arguments to @atrlock.");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		// Parse object/attribute
		var objAttrText = MModule.plainText(objAttrArg.Message!);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText);

		if (!split.TryPickT0(out var details, out _) || string.IsNullOrEmpty(details.Attribute))
		{
			await NotifyService!.Notify(executor, "Invalid format. Use: object/attribute[=on|off]");
			return new CallState("#-1 INVALID FORMAT");
		}

		var (dbref, attrName) = details;

		// Locate object
		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		enactor, executor, dbref, LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var targetObject = locate.AsSharpObject;

		// Check if attribute exists
		var attribute = await AttributeService!.GetAttributeAsync(executor, targetObject, attrName,
		IAttributeService.AttributeMode.Read);

		if (!attribute.IsAttribute)
		{
			await NotifyService!.Notify(executor, $"Attribute {attrName} not found.");
			return new CallState("#-1 NO MATCH");
		}

		// Check if we're querying or setting lock status
		if (!args.TryGetValue("1", out var valueArg))
		{
			// Query mode - show lock status
			var isLocked = attribute.AsAttribute.Last().Flags.Any(f => f.Name.Equals("LOCKED", StringComparison.OrdinalIgnoreCase));
			await NotifyService!.Notify(executor, $"Attribute {attrName} is {(isLocked ? "locked" : "unlocked")}.");
			return new CallState(string.Empty);
		}

		// Set mode
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
			await NotifyService!.Notify(executor, "Invalid lock value. Use: on or off");
			return new CallState("#-1 INVALID VALUE");
		}

		// Check permissions
		var canSet = await PermissionService!.CanSet(executor, targetObject);
		if (!canSet)
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState("#-1 PERMISSION DENIED");
		}

		if (shouldLock)
		{
			// Lock the attribute and change ownership to executor
			await AttributeService!.SetAttributeFlagAsync(executor, targetObject, attrName, "LOCKED");

			// Change ownership to executor (if executor is a player)
			if (executor.IsPlayer)
			{
				// Re-set the attribute with new owner to change ownership
				var currentValue = attribute.AsAttribute.Last().Value;
				await AttributeService!.SetAttributeAsync(executor, targetObject, attrName, currentValue);
			}

			await NotifyService!.Notify(executor, $"Attribute {attrName} locked.");
		}
		else
		{
			// Unlock the attribute
			await AttributeService!.UnsetAttributeFlagAsync(executor, targetObject, attrName, "LOCKED");
			await NotifyService!.Notify(executor, $"Attribute {attrName} unlocked.");
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@CPATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
	MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> CopyAttribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var copyFlags = !parser.CurrentState.Switches.Contains("NOFLAGCOPY");

		if (!args.TryGetValue("0", out var sourceArg) || !args.TryGetValue("1", out var destArg))
		{
			await NotifyService!.Notify(executor, "Invalid arguments to @cpattr.");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		// Parse source object/attribute
		var sourceText = MModule.plainText(sourceArg.Message!);
		var sourceSplit = HelperFunctions.SplitDbRefAndOptionalAttr(sourceText);

		if (!sourceSplit.TryPickT0(out var sourceDetails, out _) || string.IsNullOrEmpty(sourceDetails.Attribute))
		{
			await NotifyService!.Notify(executor, "Invalid source format. Use: object/attribute");
			return new CallState("#-1 INVALID SOURCE");
		}

		var (sourceDbref, sourceAttr) = sourceDetails;

		// Locate source object
		var sourceLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		enactor, executor, sourceDbref, LocateFlags.All);

		if (sourceLocate.IsError)
		{
			return sourceLocate.AsError;
		}

		var sourceObject = sourceLocate.AsSharpObject;

		// Get the source attribute
		var sourceAttribute = await AttributeService!.GetAttributeAsync(executor, sourceObject, sourceAttr,
		IAttributeService.AttributeMode.Read);

		if (!sourceAttribute.IsAttribute)
		{
			await NotifyService!.Notify(executor, $"Attribute {sourceAttr} not found on source object.");
			return new CallState("#-1 NO MATCH");
		}

		var attrValue = sourceAttribute.AsAttribute.Last().Value;
		var attrFlags = sourceAttribute.AsAttribute.Last().Flags.ToList();

		// Parse destination(s) - can be comma-separated
		var destText = MModule.plainText(destArg.Message!);
		var destinations = destText.Split(',').Select(d => d.Trim()).ToList();

		int copiedCount = 0;

		foreach (var dest in destinations)
		{
			var destSplit = HelperFunctions.SplitDbRefAndOptionalAttr(dest);

			if (!destSplit.TryPickT0(out var destDetails, out _))
			{
				await NotifyService!.Notify(executor, $"Invalid destination format: {dest}");
				continue;
			}

			var (destDbref, destAttr) = destDetails;
			// If no destination attribute name specified, use source attribute name
			var targetAttrName = string.IsNullOrEmpty(destAttr) ? sourceAttr : destAttr;

			// Locate destination object
			var destLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			enactor, executor, destDbref, LocateFlags.All);

			if (destLocate.IsError)
			{
				await NotifyService!.Notify(executor, $"Could not find destination: {destDbref}");
				continue;
			}

			var destObject = destLocate.AsSharpObject;

			// Check permissions to set attribute on destination
			var canSet = await PermissionService!.CanSet(executor, destObject);
			if (!canSet)
			{
				await NotifyService!.Notify(executor, $"Permission denied to set attribute on {destDbref}.");
				continue;
			}

			// Set the attribute value
			var setResult = await AttributeService!.SetAttributeAsync(executor, destObject, targetAttrName, attrValue);

			if (setResult.IsT1)
			{
				await NotifyService!.Notify(executor, $"Failed to copy attribute to {destDbref}: {setResult.AsT1.Value}");
				continue;
			}

			// Copy flags if requested
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
			await NotifyService!.Notify(executor, $"Attribute copied to {copiedCount} destination(s).");
		}
		else
		{
			await NotifyService!.Notify(executor, "Failed to copy attribute to any destinations.");
			return new CallState("#-1 COPY FAILED");
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@MVATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
	MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> MoveAttribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var copyFlags = !parser.CurrentState.Switches.Contains("NOFLAGCOPY");

		if (!args.TryGetValue("0", out var sourceArg) || !args.TryGetValue("1", out var destArg))
		{
			await NotifyService!.Notify(executor, "Invalid arguments to @mvattr.");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		// Parse source object/attribute
		var sourceText = MModule.plainText(sourceArg.Message!);
		var sourceSplit = HelperFunctions.SplitDbRefAndOptionalAttr(sourceText);

		if (!sourceSplit.TryPickT0(out var sourceDetails, out _) || string.IsNullOrEmpty(sourceDetails.Attribute))
		{
			await NotifyService!.Notify(executor, "Invalid source format. Use: object/attribute");
			return new CallState("#-1 INVALID SOURCE");
		}

		var (sourceDbref, sourceAttr) = sourceDetails;

		// Locate source object
		var sourceLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		enactor, executor, sourceDbref, LocateFlags.All);

		if (sourceLocate.IsError)
		{
			return sourceLocate.AsError;
		}

		var sourceObject = sourceLocate.AsSharpObject;

		// Get the source attribute
		var sourceAttribute = await AttributeService!.GetAttributeAsync(executor, sourceObject, sourceAttr,
		IAttributeService.AttributeMode.Read);

		if (!sourceAttribute.IsAttribute)
		{
			await NotifyService!.Notify(executor, $"Attribute {sourceAttr} not found on source object.");
			return new CallState("#-1 NO MATCH");
		}

		var attrValue = sourceAttribute.AsAttribute.Last().Value;
		var attrFlags = sourceAttribute.AsAttribute.Last().Flags.ToList();

		// Parse destination(s) - can be comma-separated
		var destText = MModule.plainText(destArg.Message!);
		var destinations = destText.Split(',').Select(d => d.Trim()).ToList();

		int copiedCount = 0;

		foreach (var dest in destinations)
		{
			var destSplit = HelperFunctions.SplitDbRefAndOptionalAttr(dest);

			if (!destSplit.TryPickT0(out var destDetails, out _))
			{
				await NotifyService!.Notify(executor, $"Invalid destination format: {dest}");
				continue;
			}

			var (destDbref, destAttr) = destDetails;
			// If no destination attribute name specified, use source attribute name
			var targetAttrName = string.IsNullOrEmpty(destAttr) ? sourceAttr : destAttr;

			// Locate destination object
			var destLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			enactor, executor, destDbref, LocateFlags.All);

			if (destLocate.IsError)
			{
				await NotifyService!.Notify(executor, $"Could not find destination: {destDbref}");
				continue;
			}

			var destObject = destLocate.AsSharpObject;

			// Check permissions to set attribute on destination
			var canSet = await PermissionService!.CanSet(executor, destObject);
			if (!canSet)
			{
				await NotifyService!.Notify(executor, $"Permission denied to set attribute on {destDbref}.");
				continue;
			}

			// Set the attribute value
			var setResult = await AttributeService!.SetAttributeAsync(executor, destObject, targetAttrName, attrValue);

			if (setResult.IsT1)
			{
				await NotifyService!.Notify(executor, $"Failed to copy attribute to {destDbref}: {setResult.AsT1.Value}");
				continue;
			}

			// Copy flags if requested
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
			// Remove the source attribute after successful copy
			var clearResult = await AttributeService!.ClearAttributeAsync(executor, sourceObject, sourceAttr,
			IAttributeService.AttributePatternMode.Exact,
			IAttributeService.AttributeClearMode.Safe);

			if (clearResult.IsT1)
			{
				await NotifyService!.Notify(executor, $"Attribute moved to {copiedCount} destination(s) but failed to remove source: {clearResult.AsT1.Value}");
			}
			else
			{
				await NotifyService!.Notify(executor, $"Attribute moved to {copiedCount} destination(s).");
			}
		}
		else
		{
			await NotifyService!.Notify(executor, "Failed to move attribute to any destinations.");
			return new CallState("#-1 MOVE FAILED");
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@ATRCHOWN", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> ChangeAttributeOwner(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (!args.TryGetValue("0", out var objAttrArg) || !args.TryGetValue("1", out var ownerArg))
		{
			await NotifyService!.Notify(executor, "Invalid arguments to @atrchown.");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		// Parse object/attribute
		var objAttrText = MModule.plainText(objAttrArg.Message!);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText);

		if (!split.TryPickT0(out var details, out _) || string.IsNullOrEmpty(details.Attribute))
		{
			await NotifyService!.Notify(executor, "Invalid format. Use: object/attribute=new_owner");
			return new CallState("#-1 INVALID FORMAT");
		}

		var (dbref, attrName) = details;

		// Locate object
		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		enactor, executor, dbref, LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var targetObject = locate.AsSharpObject;

		// Check if attribute exists
		var attribute = await AttributeService!.GetAttributeAsync(executor, targetObject, attrName,
		IAttributeService.AttributeMode.Read);

		if (!attribute.IsAttribute)
		{
			await NotifyService!.Notify(executor, $"Attribute {attrName} not found.");
			return new CallState("#-1 NO MATCH");
		}

		// Locate new owner
		var newOwnerText = MModule.plainText(ownerArg.Message!);
		var ownerLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		enactor, executor, newOwnerText, LocateFlags.All);

		if (ownerLocate.IsError)
		{
			await NotifyService!.Notify(executor, "Could not find new owner.");
			return ownerLocate.AsError;
		}

		var newOwnerObject = ownerLocate.AsSharpObject;

		// New owner must be a player (or we use their owner)
		SharpPlayer newOwnerPlayer;
		if (newOwnerObject.IsPlayer)
		{
			newOwnerPlayer = newOwnerObject.AsPlayer;
		}
		else
		{
			// Use the object's owner
			newOwnerPlayer = await newOwnerObject.Object().Owner.WithCancellation(CancellationToken.None);
		}

		// Check permissions
		// Mortals can only chown to themselves
		// Wizards can chown to anyone
		var isWizard = await executor.HasPower("WIZARD") || await executor.HasFlag("WIZARD");
		var canSet = await PermissionService!.CanSet(executor, targetObject);

		if (!canSet)
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState("#-1 PERMISSION DENIED");
		}

		if (!isWizard)
		{
			// Mortals can only chown to themselves
			if (executor.IsPlayer && newOwnerPlayer.Object.DBRef != executor.AsPlayer.Object.DBRef)
			{
				await NotifyService!.Notify(executor, "You can only change attribute ownership to yourself.");
				return new CallState("#-1 PERMISSION DENIED");
			}
			else if (!executor.IsPlayer)
			{
				var executorOwner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
				if (executorOwner.Object.DBRef != newOwnerPlayer.Object.DBRef)
				{
					await NotifyService!.Notify(executor, "You can only change attribute ownership to yourself.");
					return new CallState("#-1 PERMISSION DENIED");
				}
			}
		}

		// Change ownership by re-setting the attribute with new owner
		var currentValue = attribute.AsAttribute.Last().Value;
		var setResult = await AttributeService!.SetAttributeAsync(executor, targetObject, attrName, currentValue);

		if (setResult.IsT1)
		{
			await NotifyService!.Notify(executor, $"Failed to change ownership: {setResult.AsT1.Value}");
			return new CallState("#-1 FAILED");
		}

		await NotifyService!.Notify(executor, $"Attribute {attrName} owner changed.");
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@WIPE", Switches = [], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Wipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "Wipe what?");
			return new CallState("#-1 INVALID ARGUMENT");
		}

		var objAttr = MModule.plainText(args["0"].Message!);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttr);

		if (!split.TryPickT0(out var details, out _))
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return new CallState("#-1 INVALID OBJECT");
		}

		var (dbref, maybeAttribute) = details;

		// Locate the object
		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
		enactor,
		executor,
		dbref,
		LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var targetObject = locate.AsSharpObject;

		// Check if executor can modify the object
		var canModify = await PermissionService!.Controls(executor, targetObject);
		if (!canModify)
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState("#-1 PERMISSION DENIED");
		}

		// Check if object has SAFE flag
		var isSafe = await targetObject.HasFlag("SAFE");
		if (isSafe)
		{
			await NotifyService!.Notify(executor, "That object is protected (SAFE).");
			return new CallState("#-1 SAFE");
		}

		// If no attribute pattern specified, wipe all attributes
		if (string.IsNullOrEmpty(maybeAttribute))
		{
			// Wipe all attributes - use ClearAttributeAsync with wildcard pattern
			await AttributeService!.ClearAttributeAsync(executor, targetObject, "**",
			IAttributeService.AttributePatternMode.Wildcard,
			IAttributeService.AttributeClearMode.Safe);
			await NotifyService!.Notify(executor, "Attributes wiped.");
			return new CallState(string.Empty);
		}
		else
		{
			// Wipe matching attributes
			await AttributeService!.ClearAttributeAsync(executor, targetObject, maybeAttribute,
			IAttributeService.AttributePatternMode.Wildcard,
			IAttributeService.AttributeClearMode.Safe);
			await NotifyService!.Notify(executor, $"Wiped attributes matching {maybeAttribute}.");
			return new CallState(string.Empty);
		}
	}

}
