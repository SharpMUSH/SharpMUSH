using SharpMUSH.Database;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Implementation.Tools;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Immutable;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using MarkupString;
using MarkupString.Ansi;
using static MarkupString.MStringInterpolation;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// Resolves the display width to wrap a formatted attribute block to, for <c>@examine</c> and
	/// <c>@grep/PRINT</c>'s syntax-flagged attribute rendering. NAWS WIDTH is client-controlled,
	/// unvalidated metadata. Per RFC 1073, 0 means "unspecified" from the client's end, not "wrap
	/// every token" -- and it parses fine, so it must be rejected explicitly rather than relying on
	/// TryParse to catch it. A parsed value &lt;= 0 falls back to 78 exactly like a missing one.
	/// </summary>
	private async ValueTask<int> ExecutorFormatWidthAsync(AnySharpObject executor)
	{
		var executorConnection = await ConnectionService.Get(executor.Object().DBRef).FirstOrDefaultAsync();

		return executorConnection is not null
			&& executorConnection.Metadata.TryGetValue("WIDTH", out var widthStr)
			&& int.TryParse(widthStr, out var parsedWidth)
			&& parsedWidth > 0
				? parsedWidth
				: 78;
	}

	[SharpCommand(Name = "@@", Switches = [], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = ["comment"])]
	public ValueTask<Option<CallState>> At(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ValueTask.FromResult(new Option<CallState>(CallState.Empty));

	// PennMUSH src/command.c: {"THINK", "NOEVAL", cmd_think, CMD_T_ANY | CMD_T_NOGAGGED, 0, 0}.
	[SharpCommand(Name = "THINK", Switches = ["NOEVAL"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1,
		ParameterNames = ["expression"])]
	public async ValueTask<Option<CallState>> Think(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// PennMUSH cmd_think (cmds.c:1769) is an unconditional notify, so a bare `think` prints a
		// blank line rather than nothing at all. A bare `think` has no "0" argument at all, so the
		// return has to come off the same check as the notify.
		if (!parser.CurrentState.Arguments.TryGetValue("0", out var thought))
		{
			await NotifyService.Notify(executor, string.Empty, executor);
			return CallState.Empty;
		}

		// The MString, NOT ToString(): rendering it here bakes the colour into the text as ANSI escape
		// characters and hands a plain string onward, so the markup is gone before the transport sees
		// it. A browser has no ANSI decoder and printed the escapes as literal text.
		await NotifyService.Notify(executor, thought.Message!, executor);
		return thought;
	}

	[SharpCommand(Name = "HUH_COMMAND", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> HuhCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HuhTypeHelp), executor);
		return new CallState(ErrorMessages.Returns.Huh);
	}

	[SharpCommand(Name = "LOOK", Switches = ["OUTSIDE", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Look(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		// cmds.c:1742: the /opaque switch is LOOK_NOCONTENTS.
		var key = switches.Contains("OPAQUE") ? LookKey.NoContents : LookKey.Normal;
		var lookOutside = switches.Contains("OUTSIDE");

		if (executor.IsPlayer)
		{
			var fallbackHome = new DBRef((int)Configuration.CurrentValue.Database.PlayerStart);
			if (await MoveService.RescueFromVoidAsync(executor, fallbackHome))
			{
				executor = await parser.CurrentState.KnownExecutorObject(Mediator);
			}
		}

		AnyOptionalSharpObject viewing = new None();

		if (lookOutside)
		{
			// PennMUSH do_look_at (look.c:611): looking outside shows the location OF your location, so
			// there is nothing to see when you are standing in a room or inside an opaque container.
			var container = executor.IsContent ? await executor.AsContent.Location() : null;

			if (container is null || container.IsRoom || await container.WithExitOption().IsOpaque())
			{
				await NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.CantSeeThroughThat), executor);
				return new CallState(ErrorMessages.Returns.CantSeeThroughThat);
			}

			viewing = (await container.Location()).WithExitOption().WithNoneOption();
		}
		else if (args.Count == 1)
		{
			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				args["0"].Message!.ToPlainText(),
				LocateFlags.All);

			if (locate is AnySharpObject located)
			{
				viewing = located;
			}
		}
		else
		{
			viewing = (await Mediator.Send(new GetCertainLocationQuery(executor.Id()!, executor.Object().Id!))).WithExitOption()
				.WithNoneOption();
		}

		if (viewing.IsNone)
		{
			return new None();
		}

		return await LookService.LookRoom(parser, executor, viewing, key, lookOutside);
	}

	[SharpCommand(Name = "EXAMINE", Switches = ["BRIEF", "DEBUG", "MORTAL", "PARENT", "ALL", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Examine(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		AnyOptionalSharpObject viewing;
		string? attributePattern = null;

		if (args.Count == 1)
		{
			var argText = args["0"].Message!.ToPlainText();
			if (HelperFunctions.SplitDbRefAndOptionalAttr(argText) is { Object: var objectName, Attribute: var maybeAttributePattern })
			{
				attributePattern = maybeAttributePattern;

				var locate = await LocateService.LocateAndNotifyIfInvalid(
					parser,
					executor,
					executor,
					objectName,
					LocateFlags.All);

				if (locate is not AnySharpObject located)
				{
					return new None();
				}

				viewing = located;
			}
			else
			{
				var locate = await LocateService.LocateAndNotifyIfInvalid(
					parser,
					executor,
					executor,
					argText,
					LocateFlags.All);

				if (locate is not AnySharpObject located)
				{
					return new None();
				}

				viewing = located;
			}
		}
		else
		{
			viewing = (await Mediator.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();
		}

		if (viewing is not AnySharpObject viewingKnown)
		{
			return new None();
		}

		var canExamine = await PermissionService.CanExamine(executor, viewingKnown);

		if (switches.Contains("MORTAL") && await executor.IsWizard())
		{
			canExamine = await PermissionService.Controls(executor, viewingKnown);
		}

		if (!canExamine)
		{
			var limitedObj = viewingKnown.Object();
			var limitedOwnerObj = (await limitedObj.Owner.WithCancellation(CancellationToken.None)).Object;
			await NotifyService.Notify(enactor,
				Format($"{limitedObj.Name.Hilight()} is owned by {limitedOwnerObj.Name.Hilight()}."),
				enactor);
			return new CallState(limitedObj.DBRef.ToString());
		}

		var perceive = await ObserveRealityAsync(parser, executor);
		var contents = (switches.Contains("OPAQUE") || viewing.IsExit)
			? []
			: await Mediator.CreateStream(new GetContentsQuery(viewingKnown.AsContainer), ExecutionBudget.CurrentToken)
				.Where((item, ct) => perceive(item.Object().DBRef, ct))
				.ToArrayAsync(ExecutionBudget.CurrentToken);

		var obj = viewingKnown.Object()!;
		var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var description = (await AttributeService.GetAttributeAsync(executor, viewingKnown, "DESCRIBE",
				IAttributeService.AttributeMode.Read, false)) switch
		{
			SharpAttribute[] attr => attr.Last().Value.Length == 0
				? MarkupText.Plain("There is nothing to see here")
				: attr.Last().Value,
			None => MarkupText.Plain("There is nothing to see here"),
			Error<string> => MarkupText.Empty
		};

		var objFlags = await obj.Flags.Value.ToArrayAsync();
		var objParent = await obj.Parent.WithCancellation(CancellationToken.None);
		var objPowers = obj.Powers.Value;
		var objZone = await obj.Zone.WithCancellation(CancellationToken.None);

		var outputSections = new List<MString>();

		var showFlags = Configuration.CurrentValue.Cosmetic.FlagsOnExamine;

		var objFlagStr = showFlags ? MessageFormatting.FlagSymbols(objFlags) : string.Empty;
		var nameRow = Format($"{name.Hilight()}(#{obj.DBRef.Number}{objFlagStr})");
		outputSections.Add(nameRow);

		outputSections.Add(showFlags
			? MarkupText.Plain($"Type: {obj.Type} Flags: {string.Join(" ", objFlags.Select(x => x.Name))}")
			: MarkupText.Plain($"Type: {obj.Type}"));

		if (!switches.Contains("BRIEF"))
		{
			outputSections.Add(description);
		}

		MString zoneSection;
		if (objZone is AnySharpObject zone)
		{
			var zoneLine = await MessageFormatting.FormatObjectWithDbrefMString(zone.Object());
			zoneSection = Format($"  Zone: {zoneLine}");
		}
		else
		{
			zoneSection = MarkupText.Plain("  Zone: *NOTHING*");
		}

		var ownerFlagStr = showFlags ? await MessageFormatting.FlagSymbolsAsync(ownerObj) : string.Empty;
		var ownerRow = Format($"Owner: {ownerName.Hilight()}(#{ownerObj.DBRef.Number}{ownerFlagStr}){zoneSection}");
		outputSections.Add(ownerRow);

		var parentObject = objParent.Object();
		if (parentObject == null)
		{
			outputSections.Add(MarkupText.Plain("Parent: *NOTHING*"));
		}
		else
		{
			var parentLine = await MessageFormatting.FormatObjectWithDbrefMString(parentObject);
			outputSections.Add(Format($"Parent: {parentLine}"));
		}

		foreach (var lockKvp in obj.Locks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
		{
			outputSections.Add(MarkupText.Plain(await FormatLockLineAsync(executor, lockKvp.Key, lockKvp.Value)));
		}

		var powersList = await objPowers.Select(x => x.Name).ToArrayAsync();
		outputSections.Add(MarkupText.Plain($"Powers: {string.Join(" ", powersList)}"));

		var warningsStr = obj.Warnings != WarningType.None
			? WarningTypeHelper.UnparseWarnings(obj.Warnings)
			: string.Empty;
		outputSections.Add(MarkupText.Plain($"Warnings checked: {warningsStr}"));

		if (switches.Contains("DEBUG") && await executor.IsWizard())
		{
			outputSections.Add(MarkupText.Plain($"Created: {obj.CreationTime} ({DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F})"));
		}
		else
		{
			outputSections.Add(MarkupText.Plain($"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):ddd MMM dd HH:mm:ss yyyy}"));
		}

		outputSections.Add(MarkupText.Plain($"Last modified: {DateTimeOffset.FromUnixTimeMilliseconds(obj.ModifiedTime):ddd MMM dd HH:mm:ss yyyy}"));

		if (viewingKnown is SharpPlayer viewedPlayer)
		{
			outputSections.Add(MarkupText.Plain($"Quota: {viewedPlayer.Quota}"));
		}

		await NotifyService.Notify(enactor, MarkupText.Join(MarkupText.Plain("\n"), outputSections), enactor);

		if (!switches.Contains("BRIEF"))
		{
			var checkParents = switches.Contains("PARENT");

			SharpAttributesOrError atrs;
			if (!string.IsNullOrEmpty(attributePattern))
			{
				var patternMode = IAttributeService.AttributePatternMode.Wildcard;

				atrs = await AttributeService.GetAttributePatternAsync(
					executor,
					viewingKnown,
					attributePattern,
					checkParents,
					patternMode);
			}
			else
			{
				atrs = await AttributeService.GetVisibleAttributesAsync(executor, viewingKnown);
			}

			if (atrs is SharpAttribute[] visibleAttributes)
			{
				var showAll = switches.Contains("ALL");

				// Lazily computed: only a flagged attribute needs it, and most @examine calls have none.
				int? width = null;

				foreach (var attr in visibleAttributes)
				{
					const string VeiledFlagName = "VEILED";
					if (!showAll && attr.Flags.Any(f => f.Name.Equals(VeiledFlagName, StringComparison.OrdinalIgnoreCase)))
					{
						continue;
					}

					var attrOwner = await attr.Owner.WithCancellation(CancellationToken.None);
					var attrFlagsStr = attr.Flags.Any() ? $"{string.Join("", attr.Flags.Select(f => f.Symbol))} " : "";

					if (!await PermissionService.CanViewAttribute(executor, viewingKnown, attr))
					{
						continue;
					}

					var header = MarkupText.Plain($"{attr.LongName} [{attrFlagsStr}#{attrOwner!.Object.DBRef.Number}]: ").Hilight();
					var parseType = attr.SyntaxParseType();

					if (parseType is null)
					{
						await NotifyService.Notify(enactor, Format($"{header}{attr.Value}"), enactor);
						continue;
					}

					await NotifyService.Notify(enactor, header, enactor);

					if (attr.Value.Length == 0)
					{
						continue;
					}

					width ??= await ExecutorFormatWidthAsync(executor);

					// Cost per flagged, non-empty attribute: 3 lexes and 2 full parses of `source`.
					// Tokenize lexes once (no parse). GetSemanticTokens and ValidateAndGetErrors each
					// independently construct their own lexer + parser and run the parseType grammar
					// rule again -- one to walk the parse tree for token classification, the other with
					// a custom ParserErrorListener attached. Neither IMUSHCodeParser method exposes a
					// way to reuse the other's lex/parse, and there's no third overload that returns
					// tokens + semantic tokens + errors from one pass. Collapsing this to one lex/parse
					// would need a new combined method on IMUSHCodeParser (e.g. an
					// `Analyze(MString, ParseType) -> (TokenInfo[], SemanticToken[], ParseError[])` that
					// attaches an error listener to the same parser instance AnalyzeSemanticTokens already
					// walks) -- an interface change, deliberately not made here per review instruction;
					// left as the known, documented cost of this path instead.
					var source = attr.Value;
					var tokens = parser.Tokenize(source);
					var semanticTokens = parser.GetSemanticTokens(source, parseType.Value);
					var errors = SoftcodeSource.Validate(parser, source, parseType.Value);
					var formatted = SoftcodeFormatter.Format(source, tokens, semanticTokens, errors, width.Value, parser, parseType.Value);

					await NotifyService.Notify(enactor, formatted, enactor);
				}
			}

			if (!switches.Contains("OPAQUE") && contents.Length > 0)
			{
				var conFormatResult = await AttributeService.GetAttributeAsync(executor, viewingKnown, "CONFORMAT",
					IAttributeService.AttributeMode.Read, false);

				if (conFormatResult.IsAttribute)
				{
					var contentDbrefs = string.Join(" ", contents.Select(x => x.Object().DBRef.ToString()));
					var contentNames = string.Join("|", contents.Select(x => x.Object().Name));

					var formatArgs = new Dictionary<string, CallState>
					{
						["0"] = new CallState(contentDbrefs),
						["1"] = new CallState(contentNames)
					};

					var formattedContents = await AttributeService.EvaluateAttributeFunctionAsync(
						parser, executor, viewingKnown, "CONFORMAT", formatArgs);

					await NotifyService.Notify(enactor, formattedContents, enactor);
				}
				else
				{
					var contentsLabel = viewingKnown.IsRoom ? "Contents:" : "Carrying:";
					var contentItems = await contents
						.ToAsyncEnumerable()
						.Select((AnySharpContent content, CancellationToken _) => MessageFormatting.FormatObjectWithDbrefMString(content.Object()))
						.Prepend(MarkupText.Plain(contentsLabel))
						.ToListAsync();
					await NotifyService.Notify(enactor,
						MarkupText.Join(MarkupText.Plain("\n"), contentItems), enactor);
				}
			}

			if (!switches.Contains("OPAQUE") && !viewingKnown.IsExit)
			{
				var exits = await Mediator.CreateStream(new GetExitsQuery(viewingKnown.AsContainer), ExecutionBudget.CurrentToken)
					.Where((exit, ct) => perceive(exit.Object.DBRef, ct))
					.ToArrayAsync(ExecutionBudget.CurrentToken);

				if (exits.Length > 0)
				{
					var exitLines = await exits
						.ToAsyncEnumerable()
						.Select((SharpExit exit, CancellationToken _) => MessageFormatting.FormatObjectWithDbrefMString(exit.Object))
						.Prepend(MarkupText.Plain("Exits:"))
						.ToListAsync();
					await NotifyService.Notify(enactor,
						MarkupText.Join(MarkupText.Plain("\n"), exitLines), enactor);
				}
			}

			if (!viewingKnown.IsRoom)
			{
				var homeContainer = await viewingKnown.MinusRoom().Home();
				var locationContainer = await viewingKnown.AsContent.Location();

				var locationLine = await MessageFormatting.FormatObjectWithDbrefMString(locationContainer.Object());

				// An unlinked exit has no destination to report; PennMUSH shows #-1 for NOTHING.
				if (homeContainer is AnySharpContainer home)
				{
					var homeLine = await MessageFormatting.FormatObjectWithDbrefMString(home.Object());
					await NotifyService.Notify(enactor, Format($"Home: {homeLine}"), enactor);
				}
				else
				{
					await NotifyService.Notify(enactor, Format($"Home: #-1"), enactor);
				}

				await NotifyService.Notify(enactor, Format($"Location: {locationLine}"), enactor);
			}
		}

		return new CallState(obj.DBRef.ToString());
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


	[SharpCommand(Name = "@CHANNEL",
		Switches =
		[
			"LIST", "ADD", "DELETE", "RENAME", "MOGRIFIER", "NAME", "PRIVS", "QUIET", "DECOMPILE", "DESCRIBE", "CHOWN",
			"WIPE", "MUTE", "UNMUTE", "GAG", "UNGAG", "HIDE", "UNHIDE", "WHAT", "TITLE", "BRIEF", "RECALL", "BUFFER",
			"COMBINE", "UNCOMBINE", "ON", "JOIN", "OFF", "LEAVE", "WHO"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs, MinArgs = 0, MaxArgs = 0, ParameterNames = ["channel", "options..."])]
	public async ValueTask<Option<CallState>> Channel(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// /quiet only pairs with /list or /recall — it is invalid alongside anything else.
		if (switches.Contains("QUIET") && !switches.Contains("LIST") && !switches.Contains("RECALL"))
		{
			await NotifyService.Notify(executor, "CHAT: Incorrect combination of switches.", executor);
			return new CallState("CHAT: INCORRECT COMBINATION OF SWITCHES");
		}

		// sharpchat.md:179-181 documents `@channel/list[/on|/off][/quiet] [<prefix>]` and
		// `@channel/what [<prefix>]` — the argument is OPTIONAL there and required everywhere else.
		// Reading Arguments["0"] unconditionally turned every argument-less switched form into a
		// KeyNotFoundException, so read through the dictionary and let the arms state their own arity.
		var arg0 = args.GetValueOrDefault("0")?.Message;
		var arg1 = args.GetValueOrDefault("1")?.Message;
		var emptyIfMissing0 = arg0 ?? MarkupText.Empty;
		var emptyIfMissing1 = arg1 ?? MarkupText.Empty;

		// Note: Channel visibility checking is handled by PermissionService.ChannelCanSeeAsync in each handler
		return switches switch
		{
			// /list, /recall and /decompile combine with other switches (`@channel/list/on/quiet`), so they
			// match on membership rather than on a positional list pattern that only fires when they are last.
			_ when switches.Contains("LIST") => await ChannelCommand.ChannelList.Handle(parser, LocateService,
				PermissionService, Mediator, NotifyService, ConnectionService, emptyIfMissing0, emptyIfMissing1,
				switches),
			// CB.RSArgs comma-splits the right-hand side, so `@channel/recall <chan>=<lines>,<start>` arrives
			// as two arguments — PennMUSH reads the same pair out of its lineinfo array (src/extchat.c:4008).
			_ when switches.Contains("RECALL") && arg0 is not null => await ChannelRecall.Handle(parser, LocateService,
				PermissionService, Mediator, NotifyService, arg0, emptyIfMissing1,
				args.GetValueOrDefault("2")?.Message ?? MarkupText.Empty, switches),
			_ when switches.Contains("DECOMPILE") && arg0 is not null => await ChannelDecompile.Handle(parser,
				LocateService, PermissionService, Mediator, NotifyService, ConnectionService, arg0, emptyIfMissing1,
				switches),
			["WHAT"] => await ChannelWhat.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
				emptyIfMissing0),
			["WHO"] when arg0 is not null => await ChannelWho.Handle(parser, LocateService, PermissionService, Mediator,
				NotifyService, ConnectionService, arg0),
			(["ON"] or ["JOIN"]) when arg0 is not null => await ChannelOn.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, arg0, arg1),
			(["OFF"] or ["LEAVE"]) when arg0 is not null => await ChannelOff.Handle(parser, LocateService,
				PermissionService, Mediator, NotifyService, arg0, arg1),
			// The eight per-member switches are one operation in PennMUSH (do_chan_user_flags,
			// src/extchat.c:1900), and the un-forms are it with "n" for an answer (cmd_channel, :3628-3640).
			// The channel is OPTIONAL in every one of them: omitted, they act on every channel you are on.
			["GAG"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Gag, forceOff: false),
			["UNGAG"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Gag, forceOff: true),
			["MUTE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Quiet, forceOff: false),
			["UNMUTE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Quiet, forceOff: true),
			["HIDE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Hide, forceOff: false),
			["UNHIDE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Hide, forceOff: true),
			["COMBINE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Combine, forceOff: false),
			["UNCOMBINE"] => await ChannelUserFlags.Handle(parser, PermissionService, Mediator, NotifyService,
				arg0, arg1, ChannelUserFlags.UserFlag.Combine, forceOff: true),
			// arg1 is null when no `=` was typed at all, which is the QUERY form — distinct from an `=` with
			// nothing after it, which clears the title (do_chan_title's rhs_present, src/extchat.c:3145).
			["TITLE"] when arg0 is not null => await ChannelTitle.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, Configuration, arg0, arg1),
			["ADD"] when arg0 is not null && arg1 is not null
				=> await ChannelAdd.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					Configuration, arg0, arg1),
			["PRIVS"] when arg0 is not null && arg1 is not null
				=> await ChannelPrivs.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					arg0, arg1),
			["DESCRIBE"] when arg0 is not null && arg1 is not null
				=> await ChannelDescribe.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					arg0, arg1),
			["BUFFER"] when arg0 is not null && arg1 is not null
				=> await ChannelBuffer.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					Configuration, arg0, arg1),
			["CHOWN"] when arg0 is not null => await ChannelChown.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, arg0, emptyIfMissing1),
			// NAME and RENAME are the same operation, as in PennMUSH (src/extchat.c:3605-3608, where both
			// switches call do_chan_admin with CH_ADMIN_RENAME). NAME was declared in the switch list above
			// but had no arm, so `@channel/name` fell through to the "What do you want to do" usage line.
			(["RENAME"] or ["NAME"]) when arg0 is not null && arg1 is not null
				=> await ChannelRename.Handle(parser, LocateService, PermissionService, Mediator, NotifyService,
					Configuration, arg0, arg1),
			["WIPE"] when arg0 is not null => await ChannelWipe.Handle(parser, LocateService, PermissionService, Mediator,
				NotifyService, arg0, emptyIfMissing1),
			["DELETE"] when arg0 is not null => await ChannelDelete.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, arg0, emptyIfMissing1),
			["MOGRIFIER"] when arg0 is not null => await ChannelMogrifier.Handle(parser, LocateService, PermissionService,
				Mediator, NotifyService, arg0, arg1),
			_ => await NotifyAndReturnChannelUsage(executor)
		};
	}

	private async ValueTask<CallState> NotifyAndReturnChannelUsage(AnySharpObject executor)
	{
		await NotifyService.Notify(executor, "What do you want to do with the channel?", executor);
		return new CallState("What do you want to do with the channel?");
	}

	private async ValueTask<string> FormatLockLineAsync(AnySharpObject viewer, string name, SharpLockData data)
	{
		var expression = await BooleanExpressionParser.RenderAsync(data.LockString, viewer, LockRenderMode.Examine, ExecutionBudget.CurrentToken);
		var creator = data.Creator is { } identity ? $"#{identity.Number}" : "#-1";
		return $"{LockNames.Display(name)} Lock [{creator}{LockService.FormatLockFlags(data.Flags)}]: {expression}";
	}

	[SharpCommand(Name = "@LISTMOTD", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> ListMessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var isWizard = await executor.IsWizard();

		var motdFile = Configuration.CurrentValue.Message.MessageOfTheDayFile;
		var motdHtmlFile = Configuration.CurrentValue.Message.MessageOfTheDayHtmlFile;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdCurrentSettingsHeader), executor);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdConnectFileFormat), executor, motdFile ?? "(not set)");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdConnectHtmlFormat), executor, motdHtmlFile ?? "(not set)");

		if (isWizard)
		{
			var wizmotdFile = Configuration.CurrentValue.Message.WizMessageOfTheDayFile;
			var wizmotdHtmlFile = Configuration.CurrentValue.Message.WizMessageOfTheDayHtmlFile;

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdWizardFileFormat), executor, wizmotdFile ?? "(not set)");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdWizardHtmlFormat), executor, wizmotdHtmlFile ?? "(not set)");
		}

		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>();
		if (motdData != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EmptyLine), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdTemporaryHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdConnectMotdFormat), executor, string.IsNullOrEmpty(motdData.ConnectMotd) ? "(not set)" : motdData.ConnectMotd);

			if (isWizard)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdWizardMotdFormat), executor, string.IsNullOrEmpty(motdData.WizardMotd) ? "(not set)" : motdData.WizardMotd);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdDownMotdFormat), executor, string.IsNullOrEmpty(motdData.DownMotd) ? "(not set)" : motdData.DownMotd);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdFullMotdFormat), executor, string.IsNullOrEmpty(motdData.FullMotd) ? "(not set)" : motdData.FullMotd);
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@MAIL",
		Switches =
		[
			"NOEVAL", "NOSIG", "STATS", "CSTATS", "DSTATS", "FSTATS", "DEBUG", "NUKE", "FOLDER", "UNFOLDER", "LIST", "READ",
			"UNREAD", "CLEAR", "UNCLEAR", "STATUS", "PURGE", "FILE", "TAG", "UNTAG", "FWD", "FORWARD", "SEND", "SILENT",
			"URGENT", "REVIEW", "RETRACT"
		], Behavior = CB.Default | CB.EqSplit | CB.NoParse, MinArgs = 0, MaxArgs = 2, ParameterNames = ["player", "subject"])]
	public async ValueTask<Option<CallState>> Mail(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		MString? arg0, arg1;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var caller = await parser.CurrentState.KnownCallerObject(Mediator);
		string[] sendSwitches = ["SEND", "URGENT", "NOSIG", "SILENT", "NOEVAL"];

		if (switches.Except(sendSwitches).Any() && switches.Length > 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MailTooManySwitches), executor);
			return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		if (!switches.Contains("NOEVAL"))
		{
			arg0 = await (arg0CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
			arg1 = await (arg1CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
		}
		else
		{
			arg0 = arg0CallState?.Message;
			arg1 = arg1CallState?.Message;
		}

		var response = switches.AsSpan() switch
		{
			[.., "FOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, switches),
			[.., "UNFOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, switches),
			[.., "FILE"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, switches),
			[.., "CLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "CLEAR"),
			[.., "UNCLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "UNCLEAR"),
			[.., "TAG"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "TAG"),
			[.., "UNTAG"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "UNTAG"),
			[.., "UNREAD"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "UNREAD"),
			[.., "STATUS"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "STATUS"),
			[.., "CSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService, LocateService,
				Mediator, NotifyService, arg0, switches),
			[.., "STATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService, LocateService,
				Mediator, NotifyService, arg0, switches),
			[.., "DSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService, LocateService,
				Mediator, NotifyService, arg0, switches),
			[.., "FSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService, LocateService,
				Mediator, NotifyService, arg0, switches),
			[.., "DEBUG"] => await AdminMail.Handle(parser, Mediator, NotifyService, switches),
			[.., "NUKE"] => await AdminMail.Handle(parser, Mediator, NotifyService, switches),
			[.., "REVIEW"] => await ReviewMail.Handle(parser, LocateService, Mediator, NotifyService, arg0, arg1, switches),
			[.., "RETRACT"] when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await RetractMail.Handle(parser, ObjectDataService, LocateService, Mediator, NotifyService,
					arg0!.ToPlainText(), arg1!.ToPlainText()),
			[.., "FWD"] when executor.IsPlayer && int.TryParse(arg0?.ToPlainText(), out var number) &&
											 (arg1?.Length ?? 0) != 0
				=> await ForwardMail.Handle(parser, ObjectDataService, LocateService, PermissionService, Mediator, number,
					arg1!.ToPlainText()),
			[.., "SEND"] or [.., "URGENT"] or [.., "SILENT"] or [.., "NOSIG"] or []
				when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await SendMail.Handle(parser, PermissionService, LocateService, ObjectDataService, Mediator, NotifyService,
					AttributeService, Configuration, arg0!, arg1!, switches),
			[.., "READ"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0 &&
															int.TryParse(arg0?.ToPlainText(), out var number)
				=> await ReadMail.Handle(parser, ObjectDataService, Mediator, NotifyService, Math.Max(0, number - 1),
					switches),
			[.., "LIST"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0
				=> await ListMail.Handle(parser, ObjectDataService, Mediator, NotifyService, arg0, arg1, switches),
			_ => await NotifyAndReturnBadMailArguments(executor)
		};

		return new CallState(response);
	}

	private async ValueTask<MString> NotifyAndReturnBadMailArguments(AnySharpObject executor)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MailBadArguments), executor);
		return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToMailCommand);
	}


	[SharpCommand(Name = "@PASSWORD", Switches = [],
		Behavior = CB.Player | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGuest, MinArgs = 2, MaxArgs = 0, ParameterNames = ["old", "new"])]
	public async ValueTask<Option<CallState>> Password(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var oldPassword = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var newPassword = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (executor is not SharpPlayer player)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PasswordOnlyPlayersHavePasswords), executor);
			return new CallState(ErrorMessages.Returns.InvalidObjectType);
		}

		var isValidPassword = PasswordService.PasswordIsValid(executor.Object().DBRef.ToString(), oldPassword,
			player.PasswordHash);
		if (!isValidPassword)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PasswordInvalid), executor);
			return new CallState(ErrorMessages.Returns.InvalidPassword);
		}

		var hashedPassword = PasswordService.HashPassword(executor.Object().DBRef.ToString(), newPassword);
		await PasswordService.SetPassword(player, hashedPassword);

		return new CallState(string.Empty);
	}
}
