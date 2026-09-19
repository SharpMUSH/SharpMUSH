using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Utilities;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@CPATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
	MinArgs = 2, MaxArgs = int.MaxValue, ParameterNames = ["source/attribute", "destination/attribute"])]
	public async ValueTask<Option<CallState>> CopyAttribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var copyFlags = !parser.CurrentState.Switches.Contains("NOFLAGCOPY");

		if (!args.TryGetValue("0", out var sourceArg) || !args.TryGetValue("1", out _))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidArgumentsToCommandFormat), executor, "@cpattr");
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var sourceText = sourceArg.Message!.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(sourceText) is not { Object: var sourceDbref, Attribute: { } sourceAttr })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidSourceFormat), executor);
			return new CallState(ErrorMessages.Returns.InvalidSource);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, sourceDbref, LocateFlags.All) switch
		{
			AnySharpObject sourceObject => await CopyAttributeFromAsync(parser, executor, sourceObject, args, copyFlags,
				sourceAttr),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> CopyAttributeFromAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject sourceObject, Dictionary<string, CallState> args, bool copyFlags, string sourceAttr)
	{
		if (await AttributeService.GetAttributeAsync(executor, sourceObject, sourceAttr,
				IAttributeService.AttributeMode.Read) is not SharpAttribute[] sourceAttribute)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFoundOnSourceFormat), executor, sourceAttr);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var sourceLeaf = sourceAttribute.Last();
		var attrValue = sourceLeaf.Value;
		var attrFlagNames = sourceLeaf.Flags.Select(flag => flag.Name).ToList();

		// With CB.RSArgs + CB.EqSplit, each comma-separated destination becomes a separate arg
		// starting at index 1. Collect all destination args in order.
		var destinations = args
			.Where(kvp => int.TryParse(kvp.Key, out var k) && k >= 1)
			.OrderBy(kvp => int.Parse(kvp.Key))
			.Select(kvp => kvp.Value.Message!.ToPlainText().Trim())
			.Where(d => !string.IsNullOrEmpty(d));

		int copiedCount = 0;

		foreach (var dest in destinations)
		{
			if (HelperFunctions.SplitDbRefAndOptionalAttr(dest) is not { Object: var destDbref, Attribute: var destAttr })
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidDestinationFormat), executor, dest);
				continue;
			}

			var targetAttrName = string.IsNullOrEmpty(destAttr) ? sourceAttr : destAttr;

			if (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
					executor, executor, destDbref, LocateFlags.All) is not AnySharpObject destObject)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CouldNotFindDestination), executor, destDbref);
				continue;
			}

			var canSet = await PermissionService.CanSet(executor, destObject);
			if (!canSet)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDeniedSetAttribute), executor, destDbref);
				continue;
			}

			var setResult = await AttributeService.SetAttributeAsync(executor, destObject, targetAttrName, attrValue);

			if (setResult is Error<string> error)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToCopyAttributeToFormat), executor, destDbref, error.Value);
				continue;
			}

			if (copyFlags && attrFlagNames.Count > 0)
			{
				// One batch, not one call per flag: applying flags one at a time re-checks permission
				// after each mutation, so a source attribute carrying both SAFE and (say) WIZARD would
				// have WIZARD silently fail to copy once SAFE landed first - Penn's copy_attrib_flags
				// checks once and applies the whole mask.
				await AttributeService.SetAttributeFlagsAsync(executor, destObject, targetAttrName, attrFlagNames);
			}

			copiedCount++;
		}

		if (copiedCount > 0)
		{
			var destWord = copiedCount == 1 ? "destination" : "destinations";
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCopiedToDestinationsFormat), executor, copiedCount, destWord);
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToCopyAttributeAny), executor);
			return new CallState(ErrorMessages.Returns.CopyFailed);
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@MVATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
	MinArgs = 2, MaxArgs = int.MaxValue, ParameterNames = ["source/attribute", "destination/attribute"])]
	public async ValueTask<Option<CallState>> MoveAttribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var copyFlags = !parser.CurrentState.Switches.Contains("NOFLAGCOPY");

		if (!args.TryGetValue("0", out var sourceArg) || !args.TryGetValue("1", out _))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidArgumentsToCommandFormat), executor, "@mvattr");
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var sourceText = sourceArg.Message!.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(sourceText) is not { Object: var sourceDbref, Attribute: { } sourceAttr })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidSourceFormat), executor);
			return new CallState(ErrorMessages.Returns.InvalidSource);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, sourceDbref, LocateFlags.All) switch
		{
			AnySharpObject sourceObject => await MoveAttributeFromAsync(parser, executor, sourceObject, args, copyFlags,
				sourceAttr),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> MoveAttributeFromAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject sourceObject, Dictionary<string, CallState> args, bool copyFlags, string sourceAttr)
	{
		if (await AttributeService.GetAttributeAsync(executor, sourceObject, sourceAttr,
				IAttributeService.AttributeMode.Read) is not SharpAttribute[] sourceAttribute)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFoundOnSourceFormat), executor, sourceAttr);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var sourceLeaf = sourceAttribute.Last();
		var attrValue = sourceLeaf.Value;
		var attrFlagNames = sourceLeaf.Flags.Select(flag => flag.Name).ToList();

		// With CB.RSArgs + CB.EqSplit, each comma-separated destination becomes a separate arg
		// starting at index 1. Collect all destination args in order.
		var destinations = args
			.Where(kvp => int.TryParse(kvp.Key, out var k) && k >= 1)
			.OrderBy(kvp => int.Parse(kvp.Key))
			.Select(kvp => kvp.Value.Message!.ToPlainText().Trim())
			.Where(d => !string.IsNullOrEmpty(d));

		int copiedCount = 0;

		foreach (var dest in destinations)
		{
			if (HelperFunctions.SplitDbRefAndOptionalAttr(dest) is not { Object: var destDbref, Attribute: var destAttr })
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidDestinationFormat), executor, dest);
				continue;
			}

			var targetAttrName = string.IsNullOrEmpty(destAttr) ? sourceAttr : destAttr;

			if (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
					executor, executor, destDbref, LocateFlags.All) is not AnySharpObject destObject)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CouldNotFindDestination), executor, destDbref);
				continue;
			}

			var canSet = await PermissionService.CanSet(executor, destObject);
			if (!canSet)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDeniedSetAttribute), executor, destDbref);
				continue;
			}

			var setResult = await AttributeService.SetAttributeAsync(executor, destObject, targetAttrName, attrValue);

			if (setResult is Error<string> error)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToCopyAttributeToFormat), executor, destDbref, error.Value);
				continue;
			}

			if (copyFlags && attrFlagNames.Count > 0)
			{
				// One batch, not one call per flag: applying flags one at a time re-checks permission
				// after each mutation, so a source attribute carrying both SAFE and (say) WIZARD would
				// have WIZARD silently fail to copy once SAFE landed first - Penn's copy_attrib_flags
				// checks once and applies the whole mask.
				await AttributeService.SetAttributeFlagsAsync(executor, destObject, targetAttrName, attrFlagNames);
			}

			copiedCount++;
		}

		if (copiedCount > 0)
		{
			var clearResult = await AttributeService.ClearAttributeAsync(executor, sourceObject, sourceAttr,
			IAttributeService.AttributePatternMode.Exact);

			var destWord = copiedCount == 1 ? "destination" : "destinations";
			if (clearResult is Error<string> error)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeMovedFailedRemoveFormat), executor, copiedCount, destWord, error.Value);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeMovedToFormat), executor, copiedCount, destWord);
			}
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToMoveAttributeAny), executor);
			return new CallState(ErrorMessages.Returns.MoveFailed);
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@ATRCHOWN", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 2, MaxArgs = 2, ParameterNames = ["object/attribute", "player"])]
	public async ValueTask<Option<CallState>> ChangeAttributeOwner(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		if (!args.TryGetValue("0", out var objAttrArg) || !args.TryGetValue("1", out var ownerArg))
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
			AnySharpObject targetObject => await ChangeAttributeOwnerAsync(parser, executor, targetObject, ownerArg, attrName),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> ChangeAttributeOwnerAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject targetObject, CallState ownerArg, string attrName)
	{
		if (await AttributeService.GetAttributeAsync(executor, targetObject, attrName,
				IAttributeService.AttributeMode.Read) is not SharpAttribute[] attribute)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFound), executor);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var newOwnerText = ownerArg.Message!.ToPlainText();

		// A thing, room or exit named as the new owner stands for its own owner.
		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor, executor, newOwnerText, LocateFlags.All) switch
		{
			AnySharpObject and SharpPlayer newOwnerPlayer => await ChownAttributeToAsync(executor, targetObject, attrName,
				attribute, newOwnerPlayer),
			AnySharpObject newOwnerObject => await ChownAttributeToAsync(executor, targetObject, attrName, attribute,
				await newOwnerObject.Object().Owner.WithCancellation(CancellationToken.None)),
			Error<CallState> error => await NewOwnerNotFoundAsync(executor, error.Value)
		};
	}

	private async ValueTask<Option<CallState>> NewOwnerNotFoundAsync(AnySharpObject executor, CallState error)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantFindThatPlayer), executor);
		return error;
	}

	private async ValueTask<Option<CallState>> ChownAttributeToAsync(AnySharpObject executor,
		AnySharpObject targetObject, string attrName, SharpAttribute[] attribute, SharpPlayer newOwnerPlayer)
	{
		// Mortals can only chown to themselves; wizards can chown to anyone.
		var isWizard = await executor.HasPower("WIZARD") || await executor.HasFlag("WIZARD");
		var canSet = await PermissionService.CanSet(executor, targetObject);

		if (!canSet)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!isWizard)
		{
			if (executor is SharpPlayer executorPlayer)
			{
				if (newOwnerPlayer.Object.DBRef != executorPlayer.Object.DBRef)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CanOnlyChownToYourself), executor);
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}
			}
			else
			{
				var executorOwner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
				if (executorOwner.Object.DBRef != newOwnerPlayer.Object.DBRef)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CanOnlyChownToYourself), executor);
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}
			}
		}

		var currentValue = attribute.Last().Value;
		var setResult = await AttributeService.SetAttributeAsync(executor, targetObject, attrName, currentValue);

		if (setResult is Error<string> error)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToChangeOwnershipFormat), executor, error.Value);
			return new CallState(ErrorMessages.Returns.Failed);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeOwnerChanged), executor);
		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@WIPE", Switches = [], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Wipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WipeWhat), executor);
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		var objAttr = args["0"].Message!.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(objAttr) is not { Object: var dbref, Attribute: var maybeAttribute })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return new CallState(ErrorMessages.Returns.InvalidObject);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
		executor,
		executor,
		dbref,
		LocateFlags.All) switch
		{
			AnySharpObject targetObject => await WipeAsync(executor, targetObject, maybeAttribute),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> WipeAsync(AnySharpObject executor, AnySharpObject targetObject,
		string? maybeAttribute)
	{
		var canModify = await PermissionService.Controls(executor, targetObject);
		if (!canModify)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var isSafe = await targetObject.HasFlag("SAFE");
		if (isSafe)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectIsProtectedSafe), executor);
			return new CallState(ErrorMessages.Returns.Safe);
		}

		// PennMUSH's do_wipe/wipe_helper (set.c:1493-1577) owns its own reporting entirely - a
		// notify per denied/tree-blocked match as they're discovered during iteration, THEN an
		// unconditional final tally ("No/One/N attributes wiped.") regardless of whether anything
		// was blocked. ClearAttributeAsync's wipe branch does exactly that internally, so this
		// command has nothing left to report itself - doing so here too would either duplicate or
		// (worse) silently override one class of outcome with a generic "success" line.
		var attributePattern = string.IsNullOrEmpty(maybeAttribute) ? "**" : maybeAttribute;
		await AttributeService.ClearAttributeAsync(executor, targetObject, attributePattern,
			IAttributeService.AttributePatternMode.Wildcard);

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@EDIT", Switches = ["FIRST", "CHECK", "QUIET", "REGEXP", "NOCASE", "ALL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 1, MaxArgs = 0, ParameterNames = ["object/attribute", "from", "to"])]
	public async ValueTask<Option<CallState>> Edit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		var objAttrArg = args.ElementAtOrDefault(0).Value;
		if (objAttrArg == null || objAttrArg.Message == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditInvalidArguments), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var objAttrText = objAttrArg.Message.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText) is not { Object: var dbref, Attribute: { } attrPattern })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditInvalidFormat), executor);
			return new CallState(ErrorMessages.Returns.InvalidFormat);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor, executor, dbref, LocateFlags.All) switch
		{
			AnySharpObject targetObject => await EditAttributesAsync(parser, executor, targetObject, args, switches, attrPattern),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> EditAttributesAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject targetObject, ImmutableSortedDictionary<string, CallState> args, IEnumerable<string> switches,
		string attrPattern)
	{
		var canModify = await PermissionService.Controls(executor, targetObject);
		if (!canModify)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// With RSArgs, the arguments after = are split by comma
		var searchArg = args.ElementAtOrDefault(1).Value;
		var replaceArg = args.ElementAtOrDefault(2).Value;

		if (searchArg == null || searchArg.Message == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditMustSpecifySearchAndReplace), executor);
			return new CallState(ErrorMessages.Returns.MissingArguments);
		}

		var search = searchArg.Message.ToPlainText();
		var replace = replaceArg?.Message != null ? replaceArg.Message.ToPlainText() : string.Empty;

		return await AttributeService.GetAttributePatternAsync(
			executor, targetObject, attrPattern, false, IAttributeService.AttributePatternMode.Wildcard) switch
		{
			SharpAttribute[] attributes => await EditMatchedAttributesAsync(parser, executor, targetObject, switches,
				attributes.ToList(), search, replace),
			Error<string> error => await NotifyAndReturnAsync(executor, error.Value)
		};
	}

	private async ValueTask<Option<CallState>> NotifyAndReturnAsync(AnySharpObject executor, string message)
	{
		await NotifyService.Notify(executor, message, executor);
		return new CallState(message);
	}

	/// <summary>Applies the edit to each attribute the pattern matched.</summary>
	private async ValueTask<Option<CallState>> EditMatchedAttributesAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject targetObject, IEnumerable<string> switches, List<SharpAttribute> attrList, string search,
		string replace)
	{
		if (attrList.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditNoMatchingAttributesFound), executor);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var hadErrors = false;
		int modifiedCount = 0;
		int unchangedCount = 0;
		var isRegexp = switches.Contains("REGEXP");
		var isFirst = switches.Contains("FIRST");
		var isCheck = switches.Contains("CHECK");
		var isQuiet = switches.Contains("QUIET");
		var isAll = switches.Contains("ALL");
		var isNoCase = switches.Contains("NOCASE");

		foreach (var attr in attrList)
		{
			var attrName = attr.LongName!;
			var attrValue = attr.Value;
			var originalText = attrValue.ToPlainText();
			string newText;

			if (isRegexp)
			{
				var edited = await PerformRegexEdit(parser, originalText, search, replace, isAll, isNoCase);
				newText = edited.Message!.ToPlainText();
				hadErrors |= edited.HadErrors;
			}
			else
			{
				newText = PerformSimpleEdit(originalText, search, replace, isFirst);
			}

			if (newText == originalText)
			{
				unchangedCount++;
				continue;
			}

			modifiedCount++;

			if (!isQuiet && !isCheck)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditAttributeSetFormat), executor, attrName);
			}
			else if (!isQuiet && isCheck)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditWouldChangeToFormat), executor, attrName, newText);
			}

			if (!isCheck)
			{
				await AttributeService.SetAttributeAsync(executor, targetObject, attrName, MarkupText.Plain(newText));
			}
		}

		if (isQuiet || (modifiedCount + unchangedCount > 1))
		{
			var checkPrefix = isCheck ? "Would edit" : "Edited";
			await NotifyService.Notify(executor,
				$"{checkPrefix} {modifiedCount} attribute{(modifiedCount != 1 ? "s" : "")}. {unchangedCount} unchanged.", executor);
		}

		return new CallState(string.Empty) { HadErrors = hadErrors };
	}

	/// <summary>
	/// Split search/replace text by comma, respecting curly brace escaping
	/// </summary>
	private string[] SplitSearchReplace(string text)
	{
		var parts = new List<string>();
		var current = new StringBuilder();
		int braceDepth = 0;

		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];

			if (c == '{')
			{
				braceDepth++;
				current.Append(c);
			}
			else if (c == '}')
			{
				braceDepth--;
				current.Append(c);
			}
			else if (c == ',' && braceDepth == 0)
			{
				parts.Add(current.ToString());
				current.Clear();
			}
			else
			{
				current.Append(c);
			}
		}

		parts.Add(current.ToString());

		for (int i = 0; i < parts.Count; i++)
		{
			var part = parts[i].Trim();
			if (part.StartsWith('{') && part.EndsWith('}'))
			{
				part = part[1..^1];
			}
			parts[i] = part;
		}

		return [.. parts];
	}

	/// <summary>
	/// Perform simple string replacement
	/// </summary>
	private string PerformSimpleEdit(string text, string search, string replace, bool firstOnly)
	{
		if (search == "^")
		{
			return replace + text;
		}
		else if (search == "$")
		{
			return text + replace;
		}
		else if (firstOnly)
		{
			int index = text.IndexOf(search);
			if (index >= 0)
			{
				return text[..index] + replace + text[(index + search.Length)..];
			}
			return text;
		}
		else
		{
			return text.Replace(search, replace);
		}
	}

	/// <summary>
	/// Perform regex replacement with evaluation: PennMUSH's <c>do_edit_regexp</c> (<c>src/set.c</c>).
	/// Each replacement is evaluated inside a regexp capture context holding its match, and the capture
	/// text is never pasted into the replacement.
	/// </summary>
	private async ValueTask<CallState> PerformRegexEdit(IMUSHCodeParser parser, string text,
		string pattern, string replaceTemplate, bool all, bool nocase)
	{
		var hadErrors = false;
		Match[] matches = [];
		var replacements = Array.Empty<string>();
		var firstEvaluated = 0;
		var captures = new RegexpCaptureFrame(parser.CurrentState.CurrentEvaluation);
		parser.CurrentState.RegexRegisters.Push(captures);
		try
		{
			var options = RegexOptions.None;
			if (nocase)
			{
				options |= RegexOptions.IgnoreCase;
			}

			var regex = SoftcodeRegex.Create(pattern, options);

			if (all)
			{
				// Evaluated last match first, as the replacements may have side effects; spliced once.
				matches = regex.Matches(text).ToArray();
				replacements = new string[matches.Length];
				firstEvaluated = matches.Length;
				for (var i = matches.Length - 1; i >= 0; i--)
				{
					var replacement = await EvaluateRegexReplacement(parser, captures, regex, matches[i], replaceTemplate, text);
					hadErrors |= replacement.HadErrors;
					replacements[i] = replacement.Message!.ToPlainText();
					firstEvaluated = i;
				}

				text = SpliceReplacements(text, matches, replacements, firstEvaluated);
			}
			else
			{
				var match = regex.Match(text);
				if (match.Success)
				{
					var replacement = await EvaluateRegexReplacement(parser, captures, regex, match, replaceTemplate, text);
					hadErrors |= replacement.HadErrors;
					text = text[..match.Index] + replacement.Message!.ToPlainText() + text[(match.Index + match.Length)..];
				}
			}

			return new CallState(text) { HadErrors = hadErrors };
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
		{
			// Same answer as an unusable pattern: the text keeps only the replacements evaluated before the failure.
			return new CallState(SpliceReplacements(text, matches, replacements, firstEvaluated)) { HadErrors = hadErrors };
		}
		catch (ArgumentException)
		{
			return new CallState(SpliceReplacements(text, matches, replacements, firstEvaluated)) { HadErrors = hadErrors };
		}
		finally
		{
			parser.CurrentState.RegexRegisters.TryPop(out _);
		}
	}

	/// <summary>
	/// Builds <paramref name="text"/> with <c>matches[from..]</c> replaced by the matching
	/// <paramref name="replacements"/>, copying each unchanged stretch once.
	/// </summary>
	private static string SpliceReplacements(string text, Match[] matches, string[] replacements, int from)
	{
		if (from >= matches.Length)
		{
			return text;
		}

		var builder = new StringBuilder(text.Length);
		var position = 0;
		for (var i = from; i < matches.Length; i++)
		{
			builder.Append(text, position, matches[i].Index - position).Append(replacements[i]);
			position = matches[i].Index + matches[i].Length;
		}

		return builder.Append(text, position, text.Length - position).ToString();
	}

	/// <summary>
	/// The replacement for one match, evaluated with that match as the innermost regexp context.
	/// </summary>
	private static async ValueTask<CallState> EvaluateRegexReplacement(IMUSHCodeParser parser,
		RegexpCaptureFrame captures, Regex regex, Match match, string template, string text)
	{
		captures.Fill(regex, match, MarkupText.Plain(text));

		var evaluatedReplacement = await parser.FunctionParse(MarkupText.Plain(template));
		return new CallState(evaluatedReplacement?.Message?.ToPlainText() ?? string.Empty)
		{ HadErrors = evaluatedReplacement?.HadErrors == true };
	}

}
