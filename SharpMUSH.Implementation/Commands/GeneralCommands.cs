using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration;
using SharpMUSH.Database;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Tools;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Concurrent;
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
using ConfigGenerated = SharpMUSH.Configuration.Generated;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private const string DefaultSemaphoreAttribute = "SEMAPHORE";
	private static readonly string[] DefaultSemaphoreAttributeArray = [DefaultSemaphoreAttribute];

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

	/// <summary>
	/// Helper method to execute attribute content with recursion tracking.
	/// This ensures commands like @INCLUDE, @TRIGGER, etc. track recursion the same way u/ufun/ulocal do.
	/// </summary>
	private async ValueTask<CallState> ExecuteAttributeWithTracking(
		IMUSHCodeParser parser,
		string attributeLongName,
		Func<Task<CallState>> executeFunc)
	{
		var callDepth = parser.CurrentState.CallDepth;
		var recursionDepths = parser.CurrentState.FunctionRecursionDepths;
		var limitExceeded = parser.CurrentState.LimitExceeded;

		if (callDepth == null || recursionDepths == null || limitExceeded == null)
		{
			return await executeFunc();
		}

		callDepth.Increment();
		if (!recursionDepths.TryGetValue(attributeLongName, out var depth))
		{
			depth = 0;
		}
		recursionDepths[attributeLongName] = ++depth;

		if (depth > Configuration.CurrentValue.Limit.FunctionRecursionLimit)
		{
			limitExceeded.IsExceeded = true;
			limitExceeded.ErrorMessage ??= ErrorMessages.Returns.Recursion;
			callDepth.Decrement();
			recursionDepths[attributeLongName] = depth - 1;
			return new CallState(ErrorMessages.Returns.Recursion);
		}

		try
		{
			return await executeFunc();
		}
		finally
		{
			callDepth.Decrement();
			if (recursionDepths.TryGetValue(attributeLongName, out var currentDepth) && currentDepth > 0)
			{
				recursionDepths[attributeLongName] = currentDepth - 1;
			}
		}
	}

	/// <summary>
	/// Validates that a custom semaphore attribute follows the required rules:
	/// 1. If already set, must have same owner (God) and flags as SEMAPHORE (no_inherit, no_clone, locked)
	/// 2. If already set, must have numeric or empty value
	/// 3. If not set, cannot be a built-in attribute (unless it is SEMAPHORE)
	/// </summary>
	private async ValueTask<Result<Success>> ValidateSemaphoreAttribute(
		AnySharpObject targetObject,
		string[] attributePath)
	{
		if (attributePath.Length == 1 && attributePath[0].Equals(DefaultSemaphoreAttribute, StringComparison.OrdinalIgnoreCase))
		{
			return new Success();
		}

		var allStandardAttributes = Mediator.CreateStream(new GetAllAttributeEntriesQuery(), ExecutionBudget.CurrentToken);
		var isStandardAttribute = await allStandardAttributes
			.AnyAsync(stdAttr => stdAttr.Name.Equals(attributePath[0], StringComparison.OrdinalIgnoreCase), ExecutionBudget.CurrentToken);

		if (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken) is not AnySharpObject god)
		{
			throw new InvalidOperationException("God (#1) must exist.");
		}

		return await AttributeService.GetAttributeAsync(
			god, targetObject, string.Join("`", attributePath), IAttributeService.AttributeMode.Read, false) switch
		{
			SharpAttribute[] chain => await ValidateExistingSemaphoreAttribute(chain.Last()),
			None when isStandardAttribute => new Error<string>($"Cannot use built-in attribute '{attributePath[0]}' as semaphore."),
			None => new Success(),
			Error<string> error => error
		};
	}

	/// <summary>The rules an attribute already on the object must meet to be used as a semaphore.</summary>
	private static async ValueTask<Result<Success>> ValidateExistingSemaphoreAttribute(SharpAttribute attribute)
	{

		// Note: Owner is guaranteed to exist for attributes
		var owner = await attribute.Owner.WithCancellation(ExecutionBudget.CurrentToken);
		if (owner!.Object.Key != 1)
		{
			return new Error<string>($"Semaphore attribute must be owned by God (#1). Current owner: #{owner.Object.Key}");
		}

		var value = attribute.Value.ToPlainText();
		if (!string.IsNullOrEmpty(value) && !int.TryParse(value, out _))
		{
			return new Error<string>($"Semaphore attribute must have a numeric or empty value. Current value: {value}");
		}

		var flagNames = attribute.Flags.Select(f => f.Name.ToLowerInvariant()).ToHashSet();
		var requiredFlags = new[] { "no_inherit", "no_clone", "locked" };
		var missingFlags = requiredFlags.Except(flagNames).ToList();

		if (missingFlags.Any())
		{
			return new Error<string>($"Semaphore attribute must have flags: {string.Join(", ", requiredFlags)}. Missing: {string.Join(", ", missingFlags)}");
		}

		return new Success();
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


	/// <summary>
	/// How the exit was linked. PennMUSH stores HOME and AMBIGUOUS directly in Destination(); SharpMUSH
	/// records them in a <c>_LINKTYPE</c> attribute instead, which is the convention <c>loc()</c> already
	/// reads to answer <c>#-3</c> and <c>#-2</c>.
	/// </summary>
	private async ValueTask<string?> LinkTypeOf(AnySharpObject executor, AnySharpObject exitObject)
	{
		var linkTypeAttr = await AttributeService.GetAttributeAsync(
			executor, exitObject, AttrLinkType, IAttributeService.AttributeMode.Read, false);

		if (linkTypeAttr is not SharpAttribute[] { Length: > 0 } linkTypeChain)
		{
			return null;
		}

		var linkType = linkTypeChain[0].Value.ToPlainText().Trim();

		return string.IsNullOrEmpty(linkType) ? null : linkType.ToLowerInvariant();
	}

	/// <summary>
	/// Why an exit could not say where it leads.
	/// </summary>
	private enum ExitDestinationFailure
	{
		/// <summary>No destination at all — never linked, or <c>@unlink</c>ed.</summary>
		Unlinked,

		/// <summary>A variable exit failed to resolve one, and has already told the executor why.</summary>
		AlreadyReported
	}

	/// <summary>
	/// Where an exit leads for a particular mover. <c>goto</c> and <c>@teleport</c> both need this and
	/// must not drift apart: a variable exit computes its destination from <c>@DESTINATION</c>, a
	/// home-linked exit sends the mover to <em>their own</em> home — which is why the mover is a separate
	/// parameter from the executor — and otherwise it is the stored destination edge.
	/// </summary>
	private async ValueTask<ExitDestination> ResolveExitDestination(
		IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject mover, SharpExit exitObj, string typedName)
	{
		var exitObject = new AnySharpObject(exitObj);
		var linkType = await LinkTypeOf(executor, exitObject);

		if (linkType == LinkTypeVariable)
		{
			var variableDestination = await FindVariableDestination(parser, executor, exitObject, typedName);

			return variableDestination is null
				? ExitDestinationFailure.AlreadyReported
				: variableDestination;
		}

		if (linkType == LinkTypeHome)
		{
			// PennMUSH do_move (move.c:451): an exit linked to HOME sends the mover to their own home.
			if (!mover.IsContent)
			{
				return ExitDestinationFailure.Unlinked;
			}

			return await mover.AsContent.Home() switch
			{
				AnySharpContainer moverHome => moverHome,
				None => ExitDestinationFailure.Unlinked
			};
		}

		return await exitObj.Home.WithCancellation(CancellationToken.None) switch
		{
			AnySharpContainer destination => destination,
			None => ExitDestinationFailure.Unlinked
		};
	}

	/// <summary>
	/// PennMUSH <c>find_var_dest</c> (<c>move.c:360</c>): a variable exit works out where it leads at move
	/// time by evaluating its <c>DESTINATION</c> attribute — with <c>%0</c> set to the exit name or alias
	/// the mover typed — falling back to <c>EXITTO</c>. The result is parsed as an objid, so it must name
	/// an object rather than merely matching something nearby.
	/// <para>Returns <c>null</c> after notifying the mover when no usable destination comes back.</para>
	/// </summary>
	private async ValueTask<AnySharpContainer?> FindVariableDestination(
		IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject exitObject, string typedName)
	{
		var attributeArgs = new Dictionary<string, CallState> { { "0", new CallState(typedName) } };

		var resolved = await AttributeHelpers.EvaluateFormatAttribute(
			AttributeService, parser, executor, exitObject, "DESTINATION", attributeArgs, MarkupText.Empty);

		if (resolved.Length == 0)
		{
			resolved = await AttributeHelpers.EvaluateFormatAttribute(
				AttributeService, parser, executor, exitObject, "EXITTO", attributeArgs, MarkupText.Empty);
		}

		var destinationText = resolved.ToPlainText().Trim();
		var located = DBRef.TryParse(destinationText, out var destinationRef)
			? await Mediator.Send(new GetObjectNodeQuery(destinationRef!.Value))
			: new AnyOptionalSharpObject(new None());

		// PennMUSH only permits a variable destination the exit itself could have been linked to
		// (move.c:457), and an exit is not somewhere you can end up.
		if (located is not AnySharpObject destination || !destination.IsContainer
				|| !await ExitCanLinkTo(exitObject, destination))
		{
			await NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.VariableExitDestinationInvalidFormat), executor,
				located switch
				{
					AnySharpObject found => found.Object().DBRef.Number.ToString(),
					None => "#-1"
				});

			return null;
		}

		return destination.AsContainer;
	}

	/// <summary>
	/// PennMUSH <c>can_link_to</c> (<c>mushdb.h:87</c>), asked of the exit rather than of the player: the
	/// exit controls the destination, is allowed to link anywhere, or the destination is LINK_OK and the
	/// exit passes its link lock.
	/// </summary>
	private async ValueTask<bool> ExitCanLinkTo(AnySharpObject exitObject, AnySharpObject destination)
	{
		if (await PermissionService.Controls(exitObject, destination))
		{
			return true;
		}

		if (await exitObject.HasPower("Link_Anywhere"))
		{
			return true;
		}

		return await destination.HasFlag("LINK_OK")
					 && await LockService.Evaluate(LockType.Link, destination, exitObject);
	}

	[SharpCommand(Name = "GOTO", Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["destination"])]
	public async ValueTask<Option<CallState>> GoTo(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (parser.CurrentState.Arguments.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay), executor);
			return CallState.Empty;
		}

		var exit = await LocateService.Locate(
			parser,
			executor,
			executor,
			args["0"].Message!.ToPlainText(),
			LocateFlags.ExitsInTheRoomOfLooker
			| LocateFlags.EnglishStyleMatching
			| LocateFlags.ExitsPreference
			| LocateFlags.OnlyMatchTypePreference);

		// PennMUSH do_move (move.c:432) answers a failed exit match with "You can't go that way.",
		// not with the generic locate failure.
		if (exit is not (AnySharpObject and SharpExit exitObj))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay), executor);
			return CallState.Empty;
		}

		// enter_room only moves a Mobile (move.c:243). A room cannot be content, and asking it where
		// it is would throw rather than refuse.
		if (!executor.IsContent)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay), executor);
			return CallState.Empty;
		}

		var exitObject = new AnySharpObject(exitObj);

		// The leave lock on the room the mover is standing in is evaluated before the exit's own
		// lock (move.c:441).
		var currentLocation = await executor.Where();

		if (!await PermissionService.PassesLock(executor, currentLocation.WithExitOption(), LockType.Leave))
		{
			await DidItService.FailLock(parser, executor, currentLocation.WithExitOption(), LockType.Leave,
				MarkupText.Plain(ErrorMessages.Notifications.CantGoThatWay));
			return CallState.Empty;
		}

		// The exit name or alias actually typed: args["1"] when the visitor routed a bare exit command
		// here, otherwise the argument to an explicit `goto`.
		var typedName = args.TryGetValue("1", out var typedArg)
			? typedArg.Message!.ToPlainText()
			: args["0"].Message!.ToPlainText();

		var resolved = await ResolveExitDestination(parser, executor, executor, exitObj, typedName);

		if (resolved is not AnySharpContainer destination)
		{
			// PennMUSH could_doit() (predicat.c:77) refuses an exit with no destination before the basic
			// lock is even evaluated, so do_move falls through to fail_lock. A variable exit that could
			// not work out where it leads has already reported that itself.
			return resolved is ExitDestinationFailure.Unlinked
				? await FailBasicLock(parser, executor, exitObject)
				: CallState.Empty;
		}

		if (!await PermissionService.CanGoto(executor, exitObj, destination))
		{
			return await FailBasicLock(parser, executor, exitObject);
		}

		if (await MoveService.WouldCreateLoop(executor.AsContent, destination))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWayContainmentLoop), executor);
			return CallState.Empty;
		}

		// did_it_with(..., NOTHING, Location(player), NOTHING, …) (move.c:480-482): the success triad
		// runs with the room being LEFT in %0. did_it's loc of NOTHING resolves to the mover's
		// location, which is still that room (predicat.c:230).
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: exitObject,
			What: "SUCCESS", OWhat: "OSUCCESS", AWhat: "ASUCCESS",
			Loc: currentLocation, Env0: currentLocation.Object().DBRef.ToString()));

		// @drop / @odrop / @adrop on an exit are shown where the mover ARRIVES: did_it's loc argument
		// is var_dest, not the room being left (move.c:483).
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: exitObject,
			What: "DROP", OWhat: "ODROP", AWhat: "ADROP",
			Loc: destination));

		// A room destination goes through enter_room, anything else through safe_tel (move.c:486-508).
		var result = destination.WithExitOption().IsRoom
			? await MoveService.EnterRoom(parser, executor.AsContent, destination,
				noMoveMsgs: false, executor.Object().DBRef, "move")
			: await MoveService.SafeTel(parser, executor.AsContent, destination,
				noMoveMsgs: false, executor.Object().DBRef, "move");

		if (result is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return CallState.Empty;
		}

		// Followers trail the leader only if the leader actually went somewhere (move.c:493).
		var newLocation = await executor.Where();

		if (!newLocation.Object().DBRef.Equals(currentLocation.Object().DBRef))
		{
			await FollowerCommand(parser, executor, currentLocation, "GOTO", exitObj.Object.DBRef);
		}

		return new CallState(destination.Object().DBRef.ToString());
	}

	/// <summary>
	/// PennMUSH <c>fail_lock(player, exit, Basic_Lock, "You can't go that way.", NOTHING)</c>
	/// (<c>src/move.c:516</c>).
	/// </summary>
	private async ValueTask<Option<CallState>> FailBasicLock(
		IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject exitObject)
	{
		await DidItService.FailLock(parser, executor, exitObject, LockType.Basic,
			MarkupText.Plain(ErrorMessages.Notifications.CantGoThatWay));

		return CallState.Empty;
	}

	/// <summary>
	/// PennMUSH <c>Puppet(victim) &amp;&amp; (Owner(victim) == Owner(player))</c> (<c>src/wiz.c:585</c>):
	/// a puppet relays everything it is told to its owner, so an owner acting on their own puppet does
	/// not need a second confirmation.
	/// </summary>
	private static async ValueTask<bool> IsOwnPuppet(AnySharpObject thing, AnySharpObject player)
	{
		if (!await thing.HasFlag("PUPPET"))
		{
			return false;
		}

		var thingOwner = (await thing.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;
		var playerOwner = (await player.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;

		return thingOwner.Equals(playerOwner);
	}


	[SharpCommand(Name = "@TELEPORT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2,
		Switches = ["LIST", "INSIDE", "SILENT"], ParameterNames = ["object", "destination"])]
	public async ValueTask<Option<CallState>> Teleport(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var destinationString = (args.Count == 1 ? args["0"].Message : args["1"].Message)?.ToPlainText() ?? string.Empty;
		var toTeleport = (args.Count == 1 ? MarkupText.Plain(executor.Object().DBRef.ToString()) : args["0"].Message)?.ToPlainText() ?? string.Empty;

		var isList = parser.CurrentState.Switches.Contains("LIST");

		IEnumerable<DbRefOrName> toTeleportList;
		if (isList)
		{
			toTeleportList = ArgHelpers.NameList(toTeleport);
		}
		else
		{
			var isDbRef = DBRef.TryParse(toTeleport, out var objToTeleport);
			toTeleportList = [isDbRef ? objToTeleport!.Value : toTeleport];
		}

		var toTeleportStringList = toTeleportList.Select(x => x switch
		{
			DBRef dbref => dbref.ToString(),
			string str => str
		});

		var destination = await LocateService.LocateAndNotifyIfInvalid(parser,
			executor,
			executor,
			destinationString,
			LocateFlags.All);

		if (destination is not AnySharpObject validDestination)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay), executor);
			return CallState.Empty;
		}

		// Teleporting to an exit means going where it leads. That is resolved per target inside the loop,
		// because a home-linked exit leads somewhere different for each mover.
		var destinationExit = validDestination is SharpExit exitDestination ? exitDestination : null;
		var fixedDestination = destinationExit is null ? validDestination.AsContainer : null;

		foreach (var obj in toTeleportStringList)
		{
			var locateTarget = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, obj,
				LocateFlags.All);
			if (locateTarget is not AnySharpObject target || target.IsRoom)
			{
				// Rooms cannot be teleported (PennMUSH src/wiz.c).
				if (locateTarget.IsRoom)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantTeleportRooms), executor);
				}
				else
				{
					await NotifyService.Notify(executor, ErrorMessages.Returns.NotVisible, executor);
				}
				continue;
			}
			var targetContent = target.AsContent;
			if (!await PermissionService.Controls(executor, target))
			{
				await NotifyService.Notify(executor, ErrorMessages.Returns.CannotTeleport, executor);
				continue;
			}

			AnySharpContainer destinationContainer;

			if (destinationExit is null)
			{
				destinationContainer = fixedDestination!;
			}
			else
			{
				var resolvedExit = await ResolveExitDestination(
					parser, executor, target, destinationExit, destinationString);

				if (resolvedExit is not AnySharpContainer resolvedContainer)
				{
					if (resolvedExit is ExitDestinationFailure.Unlinked)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ExitGoesNowhere), executor);
					}

					continue;
				}

				destinationContainer = resolvedContainer;
			}

			// recursive_member(destination, victim, 0) || victim == destination (wiz.c:440). This is a
			// refusal, so it has to be decided before anything announces the departure — safe_tel
			// declining the move afterwards would leave the room told about a move that never happened.
			if (targetContent.Object().DBRef.Equals(destinationContainer.Object().DBRef)
					|| await MoveService.WouldCreateLoop(targetContent, destinationContainer))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BadDestination), executor);
				continue;
			}

			// Every check from here to the end of the command is gated on Tel_Anywhere in PennMUSH
			// (wiz.c:442, 487, 541, 549, 563): Hasprivs(x) || has_power_by_name(x, "TPORT_ANYWHERE")
			// (hdrs/mushdb.h:17-18), where Hasprivs is Wizard or Royalty. SharpMUSH seeds the matching
			// power as Tport_Anywhere (SharpMUSH.Database/Seed/PowerSeed.cs:48).
			var telAnywhere = await executor.IsWizard()
				|| await executor.IsRoyalty()
				|| await executor.HasPower("Tport_Anywhere");

			// wiz.c:442: without Tel_Anywhere, another player is not a destination at all — the
			// /INSIDE question below only arises for someone who could have gone there.
			if (!telAnywhere && target.IsPlayer && destinationContainer.IsPlayer)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BadDestination), executor);
				continue;
			}

			// wiz.c:487: a Tel_Anywhere teleporter sending a player TO a player lands them beside that
			// player rather than inside them. /INSIDE is what asks for the containment instead.
			// DEVIATION: Penn's branch (wiz.c:487-497) does its own OXTPORT/safe_tel/TPORT and returns
			// before wiz.c:585, so it never prints "Teleported." here. This falls through to the shared
			// path below instead, which does print it.
			if (telAnywhere
					&& target.IsPlayer
					&& destinationContainer.IsPlayer
					&& !parser.CurrentState.Switches.Contains("INSIDE"))
			{
				destinationContainer = await destinationContainer.Location();
			}

			// Zone teleport restriction: check if the source room blocks teleporting out.
			// PennMUSH src/wiz.c: NO_TEL flag prevents all non-wizard teleports from the room.
			// Zone mismatch with Zone lock failure prevents teleporting out of the zone.
			if (!telAnywhere)
			{
				AnySharpContainer? sourceLocation = null;
				try
				{
					sourceLocation = target.IsContent
						? await target.AsContent.Location()
						: null;
				}
				catch
				{
				}

				if (sourceLocation is not null)
				{
					var sourceObj = sourceLocation.WithExitOption();

					if (await sourceObj.HasFlag("NO_TEL"))
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TeleportsNotAllowed), executor);
						continue;
					}

					// Zone mismatch check: if source room has a zone that differs from destination's zone,
					// evaluate the Zone lock on the source room. Failure blocks teleport.
					var sourceZone = await sourceObj.Object().Zone.WithCancellation(CancellationToken.None);
					if (sourceZone is AnySharpObject sourceZoneObject)
					{
						var destObj = destinationContainer.WithExitOption();
						var destZone = await destObj.Object().Zone.WithCancellation(CancellationToken.None);
						var sourceZoneDbRef = sourceZoneObject.Object().DBRef;
						var destZoneDbRef = destZone is AnySharpObject destZoneObject
							? destZoneObject.Object().DBRef
							: new DBRef(-1);

						if (!sourceZoneDbRef.Equals(destZoneDbRef))
						{
							if (!await LockService.Evaluate(LockType.Zone, sourceObj, executor))
							{
								await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoZoneTeleport), executor);
								continue;
							}
						}
					}
				}
			}

			// Check TPort lock on the destination (PennMUSH src/wiz.c).
			// Wizards bypass the TPort lock check.
			if (!telAnywhere)
			{
				var destObj = destinationContainer.WithExitOption();
				if (!await LockService.Evaluate(LockType.Teleport, destObj, executor))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TeleportsNotAllowed), executor);
					continue;
				}
			}

			// PennMUSH do_teleport_one (wiz.c:568-579). /SILENT suppresses the OXTPORT and TPORT
			// triads and, through safe_tel's nomovemsgs, the MOVE triad. It does not reach ENTER or
			// LEAVE, and it does not reach the automatic look enter_room ends with.
			var isSilent = parser.CurrentState.Switches.Contains("SILENT");
			var currentLocation = await targetContent.Location();
			var changesRoom = !currentLocation.Object().DBRef.Equals(destinationContainer.Object().DBRef);

			if (!isSilent && changesRoom)
			{
				await DidItService.DidIt(parser, new DidItRequest(
					Player: target, Thing: target, OWhat: "OXTPORT",
					Loc: currentLocation, Env0: executor.Object().DBRef.ToString()));
			}

			var moveResult = await MoveService.SafeTel(
				parser, targetContent, destinationContainer, isSilent, executor.Object().DBRef, "teleport");

			if (moveResult is Error<string> error)
			{
				await NotifyService.Notify(executor, error.Value, executor);
				continue;
			}

			if (!isSilent && changesRoom)
			{
				await DidItService.DidIt(parser, new DidItRequest(
					Player: target, Thing: target,
					What: "TPORT", OWhat: "OTPORT", AWhat: "ATPORT",
					Loc: destinationContainer,
					Env0: executor.Object().DBRef.ToString(), Env1: currentLocation.Object().DBRef.ToString()));
			}

			// wiz.c:585-588: the teleporter is told the move happened, unless they were the one moved,
			// unless the victim is their own puppet (which reports for itself), and unless AreQuiet.
			if (!target.Object().DBRef.Equals(executor.Object().DBRef)
					&& !await IsOwnPuppet(target, executor)
					&& !await target.Object().AreQuietAsync(executor))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Teleported), executor);
			}
		}

		return new CallState(validDestination.Object().DBRef.ToString());
	}

	[SharpCommand(Name = "@FIND", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 0, MaxArgs = 3, ParameterNames = ["name", "flags"])]
	public async ValueTask<Option<CallState>> Find(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		string? searchName = null;
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			searchName = args["0"].Message?.ToPlainText();
		}

		int? beginDbref = null;
		int? endDbref = null;

		if (args.Count >= 2 && args.ContainsKey("1"))
		{
			var beginStr = args["1"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(beginStr) && int.TryParse(beginStr, out var begin))
			{
				beginDbref = begin;
			}
		}

		if (args.Count >= 3 && args.ContainsKey("2"))
		{
			var endStr = args["2"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out var end))
			{
				endDbref = end;
			}
		}

		var matchCount = 0;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindSearchingFormat), executor, searchName != null ? string.Format(ErrorMessages.Notifications.FindSearchMatchingFormat, searchName) : "");

		var filter = new ObjectSearchFilter
		{
			NamePattern = searchName,
			MinDbRef = beginDbref,
			MaxDbRef = endDbref
		};

		var controlledResults = await Mediator.CreateStream(new GetFilteredObjectsQuery(filter))
			.Where(async (obj, ct) =>
			{
				return await Mediator.Send(new GetObjectNodeQuery(obj.DBRef), ct) is AnySharpObject objNode
					&& await PermissionService.Controls(executor, objNode);
			})
			.ToListAsync();

		matchCount = controlledResults.Count;

		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindRangeFormat), executor, beginDbref ?? 0, endDbref?.ToString() ?? "end");
		}

		foreach (var obj in controlledResults)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindObjectResultFormat), executor, obj.Key, obj.Name);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindFoundMatchingFormat), executor, matchCount);

		return new CallState(matchCount.ToString());
	}

	[SharpCommand(Name = "@HALT", Switches = ["ALL", "NOEVAL", "PID"], Behavior = CB.Default | CB.EqSplit | CB.RSBrace,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Halt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();

		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			await foreach (var obj in Mediator.CreateStream(new GetAllObjectsQuery()))
			{
				await Mediator.Send(new HaltObjectQueueRequest(obj.DBRef));
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllObjectsHalted), executor);
			return CallState.Empty;
		}

		if (switches.Contains("PID"))
		{
			var pidStr = args.GetValueOrDefault("0")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(pidStr))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltMustSpecifyPid), executor);
				return new CallState(ErrorMessages.Returns.NoPidSpecified);
			}

			if (!long.TryParse(pidStr, out var pid))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltInvalidPidFormat), executor);
				return new CallState(ErrorMessages.Returns.InvalidPid);
			}

			if (!TryGetQueueEntry(scheduler, pid, out var entry)) return await QueueInspectionUnsupported(executor);
			if (entry is null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltNoTaskWithPidFormat), executor, pid);
				return new CallState(ErrorMessages.Returns.NotFound);
			}
			if (!await parser.ServiceProvider.GetRequiredService<IQueueControlService>()
				.CanAccessLegacyAsync(executor, pid, mutate: true, ExecutionBudget.CurrentToken))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var halted = await Mediator.Send(new HaltByPidRequest(pid), ExecutionBudget.CurrentToken);
			if (halted)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltTaskHaltedFormat), executor, pid);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltNoTaskWithPidFormat), executor, pid);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			return CallState.Empty;
		}

		// @halt with no arguments - clear executor's queue without setting HALT flag
		if (args.Count == 0)
		{
			await Mediator.Send(new HaltObjectQueueRequest(executor.Object().DBRef));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Halted), executor);
			return CallState.Empty;
		}

		var targetName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(targetName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltMustSpecifyTarget), executor);
			return new CallState(ErrorMessages.Returns.NoTargetSpecified);
		}

		var maybeTarget = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (maybeTarget is not AnySharpObject target)
		{
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		var hasHaltPower = await executor.HasPower("HALT");
		var canHalt = await PermissionService.Controls(executor, target) ||
									await executor.IsWizard() || hasHaltPower;

		if (!canHalt)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var targetObject = target.Object();
		var hasReplacementActions = args.Count >= 2;
		var replacementActions = hasReplacementActions ? args["1"].Message : null;

		// RSBrace preserves outer braces during argument parsing (PennMUSH CS_BRACES).
		// Strip them here before execution (PennMUSH PE_COMMAND_BRACES equivalent).
		if (replacementActions is not null)
			replacementActions = HelperFunctions.StripOuterBraces(replacementActions);

		if (target.IsPlayer)
		{
			await Mediator.Send(new HaltObjectQueueRequest(targetObject.DBRef));

			await foreach (var obj in Mediator.CreateStream(new GetAllObjectsQuery()))
			{
				var owner = await obj.Owner.WithCancellation(CancellationToken.None);
				if (owner.Object.DBRef == targetObject.DBRef)
				{
					await Mediator.Send(new HaltObjectQueueRequest(obj.DBRef));
				}
			}

			if (hasReplacementActions)
			{
				await Mediator.Send(new AdmitCommandListRequest(
					replacementActions!,
					parser.CurrentState,
					new DbRefAttribute(targetObject.DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltedPlayerAndObjectsFormat), executor, targetObject.Name);
		}
		else
		{
			await Mediator.Send(new HaltObjectQueueRequest(targetObject.DBRef));

			if (hasReplacementActions)
			{
				await Mediator.Send(new AdmitCommandListRequest(
					replacementActions!,
					parser.CurrentState,
					new DbRefAttribute(targetObject.DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltedObjectWithActionsFormat), executor, targetObject.Name);
			}
			else
			{
				var haltFlag = await Mediator.Send(new GetObjectFlagQuery("HALT"));
				if (haltFlag != null)
				{
					await Mediator.Send(new SetObjectFlagCommand(target, haltFlag));
				}
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltedObjectFormat), executor, targetObject.Name);
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NOTIFY", Switches = ["ALL", "ANY", "SETQ", "QUIET"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
		MinArgs = 1, MaxArgs = 0, ParameterNames = ["target", "message"])]
	public async ValueTask<Option<CallState>> Notify(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.Except(["QUIET"]).ToArray();
		var notifyType = "ANY";
		var args = parser.CurrentState.Arguments;

		if ((parser.CurrentState.Arguments.Count == 0) || string.IsNullOrEmpty(args["0"].Message?.ToPlainText()))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyMustSpecifySemaphoreObject), executor);
			return new None();
		}

		switch (switches)
		{
			case ["ALL"]:
				notifyType = "ALL";
				break;
			case ["ANY"]:
				notifyType = "ANY";
				break;
			case ["SETQ"]:
				notifyType = "SETQ";
				break;
			case []:
				break;
			default:
				return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		if (HelperFunctions.SplitDbRefAndOptionalAttr(args["0"].Message!.ToPlainText()) is not { Object: var db, Attribute: var maybeAttributeString })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyMustSpecifyValidObjectAttribute), executor);
			return new None();
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
			db, LocateFlags.All) switch
		{
			AnySharpObject objectToNotify => await NotifySemaphoreHolderAsync(parser, executor, objectToNotify, notifyType,
				args, maybeAttributeString),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> NotifySemaphoreHolderAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject objectToNotify, string notifyType, Dictionary<string, CallState> args, string? maybeAttributeString)
	{
		if (!await PermissionService.Controls(executor, objectToNotify) &&
			!await objectToNotify.Object().Flags.Value.AnyAsync(flag => flag.Name.Equals("LINK_OK", StringComparison.OrdinalIgnoreCase), ExecutionBudget.CurrentToken))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var attribute = string.IsNullOrEmpty(maybeAttributeString) ? DefaultSemaphoreAttribute : maybeAttributeString;

		using var semaphoreMutation = await parser.ServiceProvider.GetRequiredService<ITaskScheduler>().EnterSemaphoreMutationAsync();
		var attributeContents = await AttributeService.GetAttributeAsync(executor, objectToNotify, attribute,
			IAttributeService.AttributeMode.Execute, false);

		if (attributeContents is Error<string> attributeError)
		{
			return new CallState(attributeError.Value);
		}

		int notifyCount = 1;
		Dictionary<string, MString>? qRegisters = null;

		if (notifyType == "SETQ")
		{
			// With CB.RSArgs, each comma-separated value becomes a separate argument
			// So @notify/setq obj=0,val1,1,val2 becomes: args[0]=obj, args[1]=0, args[2]=val1, args[3]=1, args[4]=val2
			if (args.Count < 3)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyMustSpecifyQregAssignments), executor);
				return new CallState(ErrorMessages.Returns.MissingQregAssignments);
			}

			var qregArgCount = args.Count - 1;
			if (qregArgCount % 2 != 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyQregAssignmentsMustBePairs), executor);
				return new CallState(ErrorMessages.Returns.InvalidQregPairs);
			}

			qRegisters = new Dictionary<string, MString>();
			for (var i = 1; i < args.Count; i += 2)
			{
				var qregName = args[i.ToString()].Message!.ToPlainText().Trim();
				var qregValue = args[(i + 1).ToString()].Message!.ToPlainText();
				qRegisters[qregName] = MarkupText.Plain(qregValue);
			}
		}
		else if (args.Count > 1 && args.TryGetValue("1", out var arg1))
		{
			var countArg = arg1.Message?.ToPlainText();
			if (!string.IsNullOrEmpty(countArg) &&
					(!int.TryParse(countArg, out notifyCount) || notifyCount < 1))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyInvalidNumber), executor);
				return new CallState(ErrorMessages.Returns.InvalidNumber);
			}
		}

		var dbRefAttribute = new DbRefAttribute(objectToNotify.Object().DBRef, attribute.Split("`"));
		var validation = await ValidateSemaphoreAttribute(objectToNotify, dbRefAttribute.Attribute);
		if (validation is Error<string> validationError) return await ReportSemaphoreCommandError(executor, validationError.Value);
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();
		return await SemaphoreCommandAccounting(objectToNotify, dbRefAttribute.Attribute,
			(old, selected) => notifyType == "ALL" ? Math.Max(0, (long)old - selected) : (long)old - (notifyType == "SETQ" ? 1 : notifyCount), false) switch
		{
			SemaphoreAccounting counted => await NotifySemaphoreAsync(parser, executor, scheduler, dbRefAttribute, notifyType,
				notifyCount, qRegisters, counted),
			Error<string> accountingError => await ReportSemaphoreCommandError(executor, accountingError.Value),
		};
	}

	/// <summary>
	/// The half of <c>@notify</c> that releases the waiting tasks, once the semaphore's count has been accounted for.
	/// </summary>
	private async ValueTask<Option<CallState>> NotifySemaphoreAsync(IMUSHCodeParser parser, AnySharpObject executor,
		ITaskScheduler scheduler, DbRefAttribute dbRefAttribute, string notifyType, int notifyCount,
		Dictionary<string, MString>? qRegisters, SemaphoreAccounting counted)
	{
		var changed = await scheduler.ApplySemaphoreCommandAsync(dbRefAttribute,
			notifyType == "ALL" ? null : notifyType == "SETQ" ? 1 : notifyCount, false,
			counted.Persist, counted.Reconcile, qRegisters);
		if (notifyType == "SETQ")
		{
			if (changed == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyNoTaskWaitingOnSemaphore), executor);
				return new CallState(ErrorMessages.Returns.NoWaitingTask);
			}
			return new None();
		}

		if (!parser.CurrentState.Switches.Contains("QUIET"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Notified), executor);
		}

		return new None();
	}


	[SharpCommand(Name = "@SCAN", Switches = ["ROOM", "SELF", "ZONE", "GLOBALS"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 1, MaxArgs = 0, ParameterNames = ["object", "code"])]
	public async ValueTask<Option<CallState>> Scan(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.Any()
			? parser.CurrentState.Switches.ToArray()
			: ["ROOM", "SELF", "ZONE", "GLOBALS"];

		var perceive = await ObserveRealityAsync(parser, executor);
		List<string> runningOutput = [];

		async Task<bool> CanScan(AnySharpObject obj)
		{
			var controls = await PermissionService.Controls(executor, obj);
			if (controls) return true;

			var isVisual = await obj.HasFlag("VISUAL");
			return isVisual;
		}

		async ValueTask ReportMatches(IAsyncEnumerable<AnySharpObject> candidates)
		{
			var matched = await CommandDiscoveryService.MatchUserDefinedCommand(parser,
				candidates.Where((item, ct) => perceive(item.Object().DBRef, ct)), arg0);
			if (!matched.TryGetValue(out var matches))
			{
				return;
			}

			foreach (var (i, (obj, attr, _)) in matches.Index())
			{
				if (!await CanScan(obj))
				{
					continue;
				}

				runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
				await NotifyService.Notify(executor,
					$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]", executor);
			}
		}

		static IAsyncEnumerable<AnySharpObject> Just(AnySharpObject obj) => new[] { obj }.ToAsyncEnumerable();

		var here = executor.IsContent ? await executor.AsContent.Location() : null;

		// Both zones are wanted by the ZONE branch and by the master room's already-scanned guard, and
		// resolving one costs a fetch, so only pay for them when a branch that reads them will run.
		var needsZones = switches.Contains("ZONE") || switches.Contains("GLOBALS");
		var hereZone = !needsZones || here is null
			? null
			: await here.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject locationZone
				? locationZone
				: null;
		var personalZone = !needsZones
			? null
			: await executor.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject ownZone
				? ownZone
				: null;

		var scannedNeighbors = false;

		if (here is not null && switches.Contains("ROOM"))
		{
			// Penn splits this into two flags and @scan with no switches sets both: CHECK_NEIGHBORS for
			// the contents of the location, CHECK_HERE for the location object itself
			// (src/game.c:1892-1911). Only the first was implemented, so a $-command living on the room
			// - the ordinary place to put one - was never reported.
			await ReportMatches(here.Content(Mediator).Select(x => x.WithRoomOption()));
			await ReportMatches(Just(here.WithExitOption()));
			scannedNeighbors = true;
		}

		if (switches.Contains("SELF"))
		{
			// CHECK_INVENTORY, then CHECK_SELF (src/game.c:1911-1927). The self check is not gated on
			// being a container: Penn scans the executor whether or not it can hold anything, and this
			// whole branch used to be skipped for an executor that could not.
			if (executor.IsContainer)
			{
				await ReportMatches(executor.AsContainer.Content(Mediator).Select(x => x.WithRoomOption()));
			}

			// An executor standing in the room is already in its contents, so the neighbours pass above
			// has reported it. do_scan lets that duplicate through because it prints the two passes under
			// separate headings; this returns one flat list, so follow scan_list instead, which drops
			// CHECK_SELF the moment CHECK_NEIGHBORS is set for exactly this reason (src/game.c:1763-1764).
			if (!scannedNeighbors)
			{
				await ReportMatches(Just(executor));
			}
		}

		if (switches.Contains("ZONE"))
		{
			// A zone that is a room is a Zone Master Room and its CONTENTS carry the commands; a zone
			// that is anything else carries them itself (src/game.c:1931-1978). Scanning the contents in
			// both cases - which is what this did - looks in the wrong place for every non-room zone,
			// and the executor's own zone was not consulted at all.
			if (hereZone is not null)
			{
				await ScanZone(hereZone);
			}

			if (personalZone is not null
					&& (hereZone is null || personalZone.Object().DBRef != hereZone.Object().DBRef))
			{
				await ScanZone(personalZone);
			}
		}

		if (switches.Contains("GLOBALS"))
		{
			var masterRoom = new DBRef(Convert.ToInt32(Configuration.CurrentValue.Database.MasterRoom));

			// Penn's own guard, verbatim: skip when the executor stands in the master room, or the master
			// room is either zone (src/game.c:1984). Note it tests only those three dbrefs - it does NOT
			// ask whether the ROOM or ZONE branch actually ran, so `@scan/globals` from inside the master
			// room reports nothing in PennMUSH too. Kept as-is for parity rather than "improved".
			var alreadyScanned = here?.Object().DBRef == masterRoom
				|| hereZone?.Object().DBRef == masterRoom
				|| personalZone?.Object().DBRef == masterRoom;

			if (!alreadyScanned)
			{
				await ReportMatches(Mediator.CreateStream(new GetContentsQuery(masterRoom))
					?.Select(x => x.WithRoomOption()) ?? AsyncEnumerable.Empty<AnySharpObject>());
			}
		}

		return new CallState(string.Join(" ", runningOutput));

		async ValueTask ScanZone(AnySharpObject zone)
		{
			if (zone.IsRoom)
			{
				// Penn guards both zone blocks with the same expression - Location(player) != Zone(player)
				// (src/game.c:1937, 1971) - which compares the location to the PERSONAL zone even while
				// scanning the location's zone. Reads like a slip, but it is what Penn does, and it is
				// materially different from comparing against the zone being scanned: with no personal
				// zone set, Zone(player) is NOTHING and the location's Zone Master Room is always scanned.
				if (here?.Object().DBRef != personalZone?.Object().DBRef)
				{
					await ReportMatches(Mediator.CreateStream(new GetContentsQuery(zone.Object().DBRef))
						?.Select(x => x.WithRoomOption()) ?? AsyncEnumerable.Empty<AnySharpObject>());
				}

				return;
			}

			await ReportMatches(Just(zone));
		}
	}

	[SharpCommand(Name = "@WAIT", Switches = ["PID", "UNTIL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 1, MaxArgs = 2, ParameterNames = ["seconds", "command"])]
	public async ValueTask<Option<CallState>> Wait(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? string.Empty;
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// RSBrace preserves outer braces during argument parsing (PennMUSH CS_BRACES).
		// Strip them here before execution (PennMUSH PE_COMMAND_BRACES equivalent).
		if (arg1 is not null)
			arg1 = HelperFunctions.StripOuterBraces(arg1);

		// Restore caller's pattern-match args (%0-%9) for the queued callback state.
		// Without this, %0 inside @wait callbacks would resolve to @wait's own arg (the delay time)
		// instead of the enclosing $command pattern match. Equivalent to PennMUSH wenv preservation.
		var callbackState = parser.CurrentState.CallerArguments is not null
			? parser.CurrentState with { Arguments = new Dictionary<string, CallState>(parser.CurrentState.CallerArguments) }
			: parser.CurrentState;

		if (switches.Contains("PID"))
		{
			return await AtWaitForPid(parser, arg0, executor, arg1?.ToPlainText(), switches);
		}

		if (arg1 is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitCommandListMissing), executor);
			return new CallState(ErrorMessages.Returns.MissingCommandListArgument);
		}

		if (double.TryParse(arg0, out var time))
		{
			TimeSpan convertedTime;
			if (switches.Contains("UNTIL"))
			{
				convertedTime = DateTimeOffset.FromUnixTimeSeconds((long)time) - DateTimeOffset.UtcNow;
			}
			else
			{
				convertedTime = TimeSpan.FromSeconds(time);
			}

			await Mediator.Send(new AdmitDelayedCommandListRequest(arg1, callbackState, convertedTime), ExecutionBudget.CurrentToken);
			return CallState.Empty;
		}

		var splitBySlashes = arg0.Split('/');

		if (splitBySlashes.Length == 1)
		{
			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, arg0,
				LocateFlags.All, async located =>
				{
					if (!await PermissionService.Controls(executor, located))
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
						return new CallState(ErrorMessages.Returns.PermissionDenied);
					}

					await QueueSemaphore(parser, located, DefaultSemaphoreAttributeArray, arg1, callbackState);
					return CallState.Empty;
				});
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, splitBySlashes[0],
				LocateFlags.All) switch
		{
			AnySharpObject foundObject => await WaitOnObjectAsync(parser, executor, foundObject, arg1, switches,
				callbackState, splitBySlashes),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> WaitOnObjectAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject foundObject, MString arg1, string[] switches, ParserState callbackState, string[] splitBySlashes)
	{
		var untilTime = 0.0d;

		switch (splitBySlashes.Length)
		{
			case 2 when switches.Contains("UNTIL"):
				{
					if (!double.TryParse(splitBySlashes[1], out untilTime))
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeArgumentFormat), executor);
						return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "TIME ARGUMENT"));
					}

					var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;

					await QueueSemaphoreWithDelay(parser, foundObject, DefaultSemaphoreAttributeArray, newUntilTime, arg1, callbackState);
					return CallState.Empty;
				}

			case 2 when double.TryParse(splitBySlashes[1], out untilTime):
				await QueueSemaphoreWithDelay(parser, foundObject, DefaultSemaphoreAttributeArray, TimeSpan.FromSeconds(untilTime), arg1, callbackState);
				return CallState.Empty;

			case 2:
				{
					var customSemaphoreAttr = splitBySlashes[1].Split('`');
					var validation = await ValidateSemaphoreAttribute(foundObject, customSemaphoreAttr);

					if (validation is Error<string> error)
					{
						await NotifyService.Notify(executor, error.Value, executor);
						return new CallState(ErrorMessages.Returns.InvalidSemaphoreAttribute);
					}

					await QueueSemaphore(parser, foundObject, customSemaphoreAttr, arg1, callbackState);
					return CallState.Empty;
				}

			case 3 when !double.TryParse(splitBySlashes[2], out untilTime):
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeArgumentFormat), executor);
				return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "TIME ARGUMENT"));

			// Note: Attribute value validation for semaphore usage is handled in QueueSemaphore/QueueSemaphoreWithDelay
			// methods. If the attribute value is not a valid integer, an error is returned.
			case 3 when switches.Contains("UNTIL"):
				{
					var customSemaphoreAttr = splitBySlashes[1].Split('`');
					var validation = await ValidateSemaphoreAttribute(foundObject, customSemaphoreAttr);

					if (validation is Error<string> error)
					{
						await NotifyService.Notify(executor, error.Value, executor);
						return new CallState(ErrorMessages.Returns.InvalidSemaphoreAttribute);
					}

					var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;
					await QueueSemaphoreWithDelay(parser, foundObject, customSemaphoreAttr, newUntilTime, arg1, callbackState);
					return CallState.Empty;
				}

			case 3:
				{
					var customSemaphoreAttr = splitBySlashes[1].Split('`');
					var validation = await ValidateSemaphoreAttribute(foundObject, customSemaphoreAttr);

					if (validation is Error<string> error)
					{
						await NotifyService.Notify(executor, error.Value, executor);
						return new CallState(ErrorMessages.Returns.InvalidSemaphoreAttribute);
					}

					await QueueSemaphoreWithDelay(parser, foundObject, customSemaphoreAttr,
						TimeSpan.FromSeconds(untilTime), arg1, callbackState);
					return CallState.Empty;
				}

			default:
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidFirstArgumentFormat), executor);
				return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "FIRST ARGUMENT"));
		}
	}

	private ValueTask QueueSemaphore(IMUSHCodeParser parser, AnySharpObject located, string[] attribute,
		MString arg1, ParserState? callbackState = null)
		=> QueueSemaphoreWithDelay(parser, located, attribute, TimeSpan.FromDays(36500), arg1, callbackState);

	private async ValueTask QueueSemaphoreWithDelay(IMUSHCodeParser parser, AnySharpObject located,
		string[] attribute, TimeSpan delay, MString arg1, ParserState? callbackState = null)
	{
		var token = ExecutionBudget.CurrentToken;
		var attrValue = await Mediator.CreateStream(new GetAttributeQuery(located.Object().DBRef, attribute), token).LastOrDefaultAsync(token);
		if (attrValue is not null && attrValue.Value.Length > 0 && !int.TryParse(attrValue.Value.ToPlainText(), out _))
		{
			var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
			await NotifyService.Notify(executor, ErrorMessages.Returns.Integer, executor);
			return;
		}
		// Admission owns the counter transaction, including negative credits and schedule rollback.
		await Mediator.Send(new AdmitCommandListWithTimeoutRequest(arg1, callbackState ?? parser.CurrentState,
			new DbRefAttribute(located.Object().DBRef, attribute), 0, delay, ManageSemaphoreCount: true), token);
	}

	private async ValueTask<Option<CallState>> AtWaitForPid(IMUSHCodeParser parser, string? arg0,
		AnySharpObject executor, string? arg1,
		string[] switches)
	{
		if (!long.TryParse(arg0, out var pid))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidPidSpecified), executor);
			return new CallState(ErrorMessages.Returns.InvalidPid);
		}

		if (string.IsNullOrEmpty(arg1))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitWhatToDoWithProcess), executor);
			return new CallState(string.Format(ErrorMessages.Returns.TooFewArguments, "@WAIT", 2, 1));
		}

		if (!TryGetQueueEntry(parser.ServiceProvider.GetRequiredService<ITaskScheduler>(), pid, out var maybeFoundPid))
			return await QueueInspectionUnsupported(executor);

		if (maybeFoundPid is null || maybeFoundPid.RemainingDelay is null || maybeFoundPid.ReleasePending)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidPidSpecified), executor);
			return new CallState(ErrorMessages.Returns.InvalidPid);
		}

		if (!await parser.ServiceProvider.GetRequiredService<IQueueControlService>()
			.CanAccessLegacyAsync(executor, pid, mutate: true, ExecutionBudget.CurrentToken))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!long.TryParse(arg1, System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture, out var seconds))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeSpecified), executor);
			return new CallState(ErrorMessages.Returns.InvalidTime);
		}

		TimeSpan delay;
		try
		{
			var now = DateTimeOffset.UtcNow;
			delay = switches.Contains("UNTIL")
				? DateTimeOffset.FromUnixTimeSeconds(seconds) - now
				: arg1.StartsWith('+') || arg1.StartsWith('-')
					? maybeFoundPid.RemainingDelay.Value + TimeSpan.FromSeconds(seconds)
					: TimeSpan.FromSeconds(seconds);
			if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
			if (delay > DateTimeOffset.MaxValue - now) throw new ArgumentOutOfRangeException(nameof(seconds));
		}
		catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeSpecified), executor);
			return new CallState(ErrorMessages.Returns.InvalidTime);
		}

		await Mediator.Send(new RescheduleSemaphoreRequest(pid, delay));
		return new CallState(pid.ToString());
	}

	[SharpCommand(Name = "@COMMAND",
		Switches =
		[
			"ADD", "ALIAS", "CLONE", "DELETE", "EqSplit", "LSARGS", "RSARGS", "NOEVAL", "ON", "OFF", "QUIET", "ENABLE",
			"DISABLE", "RESTRICT", "NOPARSE", "RSNoParse"
		], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "command", "code"])]
	public async ValueTask<Option<CallState>> Command(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		var commandName = args["0"].Message?.ToPlainText()?.ToUpper();
		if (string.IsNullOrEmpty(commandName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		var isQuiet = switches.Contains("QUIET");

		// Administrative switches - wizard only (except DELETE which requires God)
		if (switches.Any(s => new[] { "ADD", "ALIAS", "CLONE", "DELETE", "DISABLE", "ENABLE", "RESTRICT" }.Contains(s)))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (switches.Contains("ADD"))
			{
				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAddNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("ALIAS"))
			{
				var aliasName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (string.IsNullOrEmpty(aliasName))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyAlias), executor);
					return new CallState(ErrorMessages.Returns.NoAliasSpecified);
				}

				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAliasNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("CLONE"))
			{
				var cloneName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (string.IsNullOrEmpty(cloneName))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyCloneName), executor);
					return new CallState(ErrorMessages.Returns.NoCloneNameSpecified);
				}

				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandCloneNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("DELETE"))
			{
				if (!executor.IsGod())
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandOnlyGodCanDelete), executor);
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}

				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandDeleteNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("DISABLE"))
			{
				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandDisableNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("ENABLE"))
			{
				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandEnableNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("RESTRICT"))
			{
				var restriction = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandRestrictNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}
		}

		if (CommandLibrary == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandLibraryUnavailable), executor);
			return new CallState(ErrorMessages.Returns.LibraryUnavailable);
		}

		if (!CommandLibrary.TryGetValue(commandName, out var commandInfo))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandNotFoundFormat), executor, commandName);
			return new CallState(ErrorMessages.Returns.CommandNotFound);
		}

		var (definition, isSystem) = commandInfo;
		var attr = definition.Attribute;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoNameFormat), executor, attr.Name);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoTypeFormat), executor, isSystem ? "Built-in" : "User-defined");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoMinArgsFormat), executor, attr.MinArgs);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoMaxArgsFormat), executor, attr.MaxArgs);

		if (attr.Switches != null && attr.Switches.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoSwitchesFormat), executor, string.Join(", ", attr.Switches));
		}

		var behaviors = new List<string>();
		if ((attr.Behavior & CB.Default) != 0) behaviors.Add("Default");
		if ((attr.Behavior & CB.EqSplit) != 0) behaviors.Add("EqSplit");
		if ((attr.Behavior & CB.LSArgs) != 0) behaviors.Add("LSArgs");
		if ((attr.Behavior & CB.RSArgs) != 0) behaviors.Add("RSArgs");
		if ((attr.Behavior & CB.RSNoParse) != 0) behaviors.Add("RSNoParse");
		if ((attr.Behavior & CB.NoGagged) != 0) behaviors.Add("NoGagged");
		if ((attr.Behavior & CB.NoParse) != 0) behaviors.Add("NoParse");

		if (behaviors.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoBehaviorFormat), executor, string.Join(" | ", behaviors));
		}

		if (!string.IsNullOrEmpty(attr.CommandLock))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoLockFormat), executor, attr.CommandLock);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@DRAIN", Switches = ["ALL", "ANY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["object", "attribute"])]
	public async ValueTask<Option<CallState>> Drain(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message?.ToPlainText();
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (switches.Length > 1)
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.TooManySwitches, executor);
			return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		if (HelperFunctions.SplitDbRefAndOptionalAttr(arg0) is not { Object: var target, Attribute: var maybeAttribute })
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.CantSeeThat, executor);
			return new CallState(ErrorMessages.Returns.CantSeeThat);
		}

		return await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, target,
			LocateFlags.All) switch
		{
			AnySharpObject objectToDrain => await DrainObjectAsync(parser, executor, objectToDrain, switches, arg1,
				maybeAttribute),
			None => new CallState(ErrorMessages.Returns.CantSeeThat),
			Error<string> error => new CallState(error.Value)
		};
	}

	private async ValueTask<Option<CallState>> DrainObjectAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject objectToDrain, string[] switches, string? arg1, string? maybeAttribute)
	{
		if (!await PermissionService.Controls(executor, objectToDrain) &&
			!await objectToDrain.Object().Flags.Value.AnyAsync(flag => flag.Name.Equals("LINK_OK", StringComparison.OrdinalIgnoreCase), ExecutionBudget.CurrentToken))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}
		var attribute = maybeAttribute?.Split("`") ?? DefaultSemaphoreAttributeArray;
		var hasAll = switches.Contains("ALL");
		var hasAny = switches.Contains("ANY");

		int? drainCount = null;
		if (!string.IsNullOrEmpty(arg1))
		{
			if (!int.TryParse(arg1, out var count) || count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyInvalidNumber), executor);
				return new CallState(ErrorMessages.Returns.InvalidNumber);
			}
			drainCount = count;
		}

		if (hasAny && maybeAttribute is not null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DrainCannotSpecifyBothAnyAndAttribute), executor);
			return new CallState(ErrorMessages.Returns.InvalidCombination);
		}

		if (hasAll && drainCount.HasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DrainCannotSpecifyBothAllAndNumber), executor);
			return new CallState(ErrorMessages.Returns.InvalidCombination);
		}

		using var semaphoreMutation = await parser.ServiceProvider.GetRequiredService<ITaskScheduler>().EnterSemaphoreMutationAsync();
		async ValueTask<CallState?> DrainAttribute(DbRefAttribute target)
		{
			var validation = await ValidateSemaphoreAttribute(objectToDrain, target.Attribute);
			if (validation is Error<string> validationError) return await ReportSemaphoreCommandError(executor, validationError.Value);
			return await SemaphoreCommandAccounting(objectToDrain, target.Attribute,
				(old, selected) => drainCount.HasValue && old < 0 ? old : Math.Max(0, (long)old - selected), true) switch
			{
				SemaphoreAccounting counted => await DrainCounted(target, counted),
				Error<string> accountingError => await ReportSemaphoreCommandError(executor, accountingError.Value),
			};
		}

		async ValueTask<CallState?> DrainCounted(DbRefAttribute target, SemaphoreAccounting counted)
		{
			await parser.ServiceProvider.GetRequiredService<ITaskScheduler>().ApplySemaphoreCommandAsync(target,
				drainCount, true, counted.Persist, counted.Reconcile);
			return null;
		}

		if (hasAny)
		{
			var pids = Mediator.CreateStream(new ScheduleSemaphoreQuery(objectToDrain.Object().DBRef), ExecutionBudget.CurrentToken);
			var filteredPids = pids
				.GroupBy(data => string.Join('`', data.SemaphoreSource.Attribute), x => x.SemaphoreSource)
				.Select(x => x.First());
			await foreach (var uniqueAttribute in filteredPids.WithCancellation(ExecutionBudget.CurrentToken))
			{
				if (await DrainAttribute(uniqueAttribute) is { } error) return error;
			}
		}
		else
		{
			if (await DrainAttribute(new DbRefAttribute(objectToDrain.Object().DBRef, attribute)) is { } error) return error;
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@FORCE", Switches = ["NOEVAL", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSBrace, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "command"])]
	public async ValueTask<Option<CallState>> Force(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var objArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty);
		var cmdListArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Empty);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// RSBrace preserves outer braces during argument parsing (PennMUSH CS_BRACES).
		// Strip them here before execution (PennMUSH PE_COMMAND_BRACES equivalent).
		cmdListArg = HelperFunctions.StripOuterBraces(cmdListArg);

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objArg.ToPlainText(),
				LocateFlags.All) switch
		{
			AnySharpObject found => await ForceAsync(parser, executor, found, cmdListArg),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> ForceAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject found, MString cmdListArg)
	{
		// God cannot be forced by anyone (PennMUSH src/wiz.c).
		if (found.IsGod() && !executor.IsGod())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantForceGod), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!await PermissionService.Controls(executor, found))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ForcePermissionDeniedDoNotControl), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (cmdListArg.Length < 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ForceThemToDoWhat), executor);
			return new CallState(ErrorMessages.Returns.NothingToDo);
		}

		var switches = parser.CurrentState.Switches.ToArray();
		var hasLocalize = switches.Contains("LOCALIZE");
		var hasClearRegs = switches.Contains("CLEARREGS");

		// Implement /LOCALIZE: save Q-registers so forced code cannot permanently change
		// the caller's Q-registers. /CLEARREGS: start with empty Q-registers.
		// NOTE: Save must happen before Clear (both use a single TryPeek for safety).
		Dictionary<string, MString>? savedRegisters = null;
		if ((hasLocalize || hasClearRegs) && parser.CurrentState.Registers.TryPeek(out var forceTopRegs))
		{
			if (hasLocalize)
			{
				savedRegisters = new Dictionary<string, MString>(forceTopRegs);
			}

			if (hasClearRegs)
			{
				forceTopRegs.Clear();
			}
		}

		CallState? nestedResult = null;
		try
		{
			// Note: Queue infrastructure available via AdmitCommandListRequest if needed
			// Currently executes inline for immediate response (default PennMUSH behavior)
			nestedResult = await parser.With(
				state => state with
				{
					Executor = found.Object().DBRef,
					Caller = state.Executor
				},
				async newParser => await newParser.CommandListParseVisitor(cmdListArg)());
		}
		finally
		{
			if (hasLocalize && savedRegisters != null && parser.CurrentState.Registers.TryPeek(out var regsToRestore))
			{
				regsToRestore.Clear();
				foreach (var (key, value) in savedRegisters)
				{
					regsToRestore[key] = value;
				}
			}
		}

		return CallState.Empty with { HadErrors = nestedResult?.HadErrors == true };
	}

	[SharpCommand(Name = "@SEARCH", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = int.MaxValue, ParameterNames = ["player", "class=restriction..."])]
	public async ValueTask<Option<CallState>> Search(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var (playerText, pairs) = ParseSearchCommandArgs(args);

		DBRef? ownerFilter;
		if (playerText == null)
		{
			// PennMUSH: no <player> given defaults to ANY_OWNER for wizards (See_All/Search_All), else the executor's own objects.
			ownerFilter = await executor.IsWizard() ? null : executor.Object().DBRef;
		}
		else if (playerText.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			ownerFilter = null;
		}
		else if (playerText.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			ownerFilter = executor.Object().DBRef;
		}
		else
		{
			var maybeOwner = await LocateService.Locate(parser, executor, executor, playerText, LocateFlags.All);
			if (maybeOwner is not AnySharpObject owner)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchUnknownOwner), executor);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			ownerFilter = owner.Object().DBRef;
		}

		var search = await SearchSpecEngine.ExecuteResultAsync(
			parser, Mediator, LocateService, AttributeService, BooleanExpressionParser, PermissionService,
			executor, ownerFilter, pairs, useRegex: false);
		var matches = search.Matches;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchAdvancedHeader), executor);

		if (playerText != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchPlayerFilterFormat), executor, playerText);
		}

		if (pairs.Count > 0)
		{
			var criteria = string.Join(", ", pairs.Select(p => $"{p.ClassType.ToUpperInvariant()}={p.Restriction}"));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchCriteriaFormat), executor, criteria);
		}

		if (matches.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchNothingFound), executor);
			return new CallState("0") { HadErrors = search.HadErrors };
		}

		foreach (var obj in matches)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchObjectEntryFormat), executor, obj.Key, obj.Name, obj.Type);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchObjectsFoundFormat), executor, matches.Count);

		return new CallState(matches.Count.ToString()) { HadErrors = search.HadErrors };
	}

	/// <summary>
	/// Parses @search's own syntax, per PennMUSH's <c>do_search</c>:
	/// <c>@search [&lt;player&gt;] [&lt;class1&gt;=&lt;restriction1&gt;[,&lt;class2&gt;=&lt;restriction2&gt;...]]</c>.
	/// <para>The command's <c>CB.EqSplit | CB.RSArgs</c> behavior only splits the RAW text on the FIRST
	/// top-level '=' (giving <c>args["0"]</c> the whole left side verbatim) and then comma-splits
	/// everything right of it (<c>args["1"]</c>, <c>args["2"]</c>, ...). Unlike lsearch(), whose
	/// positional function args are already one token per class/restriction, @search's own player name
	/// and its first search class are both crammed into that same left-hand chunk (e.g. "all type" for
	/// <c>@search all type=PLAYER</c>), and every restriction after the first carries its own class via
	/// an embedded '=' inside its comma chunk (e.g. "flags=W" in "...,flags=W"). This re-splits that
	/// left chunk on its first whitespace run, then walks the remaining chunks for their own '='.</para>
	/// </summary>
	private static (string? Player, List<SearchSpecEngine.SearchPair> Pairs) ParseSearchCommandArgs(
		IReadOnlyDictionary<string, CallState> args)
	{
		var lhs = args.TryGetValue("0", out var arg0) ? arg0.Message?.ToPlainText() ?? "" : "";

		var rhsChunks = new List<string>();
		for (var i = 1; args.TryGetValue(i.ToString(), out var chunk); i++)
		{
			rhsChunks.Add(chunk.Message?.ToPlainText() ?? "");
		}

		string? player;
		string? leadingClass = null;

		if (lhs.Length == 0)
		{
			player = null;
		}
		else if (lhs[0] == '"')
		{
			var closeIndex = lhs.IndexOf('"', 1);
			if (closeIndex >= 0)
			{
				player = lhs[1..closeIndex];
				var remainder = lhs[(closeIndex + 1)..].TrimStart();
				leadingClass = remainder.Length > 0 ? remainder : null;
			}
			else
			{
				player = lhs.TrimStart('"');
			}
		}
		else
		{
			var spaceIndex = lhs.IndexOf(' ');
			if (spaceIndex < 0)
			{
				// A single bare token: it's the leading class if there's a restriction waiting for it
				// on the right of the '=' (e.g. "type=room"); otherwise it's a plain player/owner filter
				// (e.g. "@search SomePlayer").
				if (rhsChunks.Count > 0)
				{
					leadingClass = lhs;
					player = null;
				}
				else
				{
					player = lhs;
				}
			}
			else
			{
				player = lhs[..spaceIndex];
				var remainder = lhs[(spaceIndex + 1)..].TrimStart();
				leadingClass = remainder.Length > 0 ? remainder : null;
			}
		}

		var pairs = new List<SearchSpecEngine.SearchPair>();
		var chunkIndex = 0;

		if (leadingClass != null && chunkIndex < rhsChunks.Count)
		{
			pairs.Add(new SearchSpecEngine.SearchPair(leadingClass, rhsChunks[chunkIndex]));
			chunkIndex++;
		}

		for (; chunkIndex < rhsChunks.Count; chunkIndex++)
		{
			var chunk = rhsChunks[chunkIndex];
			var eqIndex = chunk.IndexOf('=');
			if (eqIndex > 0)
			{
				pairs.Add(new SearchSpecEngine.SearchPair(chunk[..eqIndex], chunk[(eqIndex + 1)..]));
			}
		}

		return (player, pairs);
	}

	[SharpCommand(Name = "@WHEREIS", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["name"])]
	public async ValueTask<Option<CallState>> WhereIs(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WhereIsMustSpecifyPlayer), executor);
			return new CallState(ErrorMessages.Returns.NoPlayerSpecified);
		}

		var targetName = args["0"].Message!.ToPlainText();

		var maybeTarget = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (maybeTarget is not AnySharpObject target)
		{
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		if (target is not SharpPlayer targetPlayer)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WhereIsCanOnlyLocatePlayers), executor);
			return new CallState(ErrorMessages.Returns.NotAPlayer);
		}

		var targetObject = target.Object();

		var isUnfindable = await targetObject.Flags.Value
			.AnyAsync(f => f.Symbol == "U" || f.Name.Equals("UNFINDABLE", StringComparison.OrdinalIgnoreCase));

		if (isUnfindable)
		{
			await NotifyService.Notify(target,
				$"{executor.Object().Name} tried to locate you, but was unable to.", executor);
			await NotifyService.Notify(executor,
				$"{targetObject.Name} is UNFINDABLE.", executor);
			return new CallState(ErrorMessages.Returns.Unfindable);
		}

		var targetLocation = await target.AsContent.Location();
		var locationName = targetLocation.Object().Name;

		await NotifyService.Notify(target,
			$"{executor.Object().Name} has just located your position.", executor);

		await NotifyService.Notify(executor,
			$"{targetObject.Name} is in {locationName}.", executor);

		return new CallState(targetLocation.Object().DBRef.ToString());
	}

	[SharpCommand(Name = "@CONFIG", Switches = ["SET", "SAVE", "LOWERCASE", "LIST"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["option", "value"])]
	public async ValueTask<Option<CallState>> Config(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var useLowercase = switches.Contains("LOWERCASE");

		var allCategories = ConfigGenerated.ConfigAccessor.Categories.ToList();

		IEnumerable<(string Category, string PropertyName, SharpConfigAttribute ConfigAttr, object? Value)> getAllOptions() =>
			ConfigGenerated.ConfigMetadata.PropertyToAttributeName.Keys.Select(propName => (
				Category: ConfigGenerated.ConfigAccessor.GetCategoryForProperty(propName) ?? "",
				PropertyName: propName,
				ConfigAttr: ConfigGenerated.ConfigMetadata.PropertyMetadata[propName],
				Value: ConfigGenerated.ConfigAccessor.GetValue(Configuration.CurrentValue, propName)));

		if (switches.Contains("SET") || switches.Contains("SAVE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (switches.Contains("SAVE") && !executor.IsGod())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOnlyGodCanUseSave), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigSetSaveNotImplemented), executor);
			return new CallState(ErrorMessages.Returns.NotImplemented);
		}

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCategoriesHeader), executor);
			foreach (var cat in allCategories)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCategoryItemFormat), executor, cat);
			}
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigUseCategoryHelp), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigUseOptionHelp), executor);
			return CallState.Empty;
		}

		var searchTerm = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";

		var matchingCategory = allCategories.FirstOrDefault(c =>
			c.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingCategory != null)
		{
			var categoryOptions = getAllOptions()
				.Where(opt => opt.Category.Equals(matchingCategory, StringComparison.OrdinalIgnoreCase))
				.OrderBy(opt => opt.ConfigAttr.Name)
				.ToList();

			if (categoryOptions.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigNoOptionsInCategoryFormat), executor, matchingCategory);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionsInCategoryFormat), executor, matchingCategory);
			foreach (var opt in categoryOptions)
			{
				var name = useLowercase ? opt.ConfigAttr.Name.ToLower() : opt.ConfigAttr.Name;
				var value = opt.Value?.ToString() ?? "null";
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor, name, value);
			}
			return CallState.Empty;
		}

		var allOptions = getAllOptions();
		var matchingOption = allOptions.FirstOrDefault(opt =>
			opt.ConfigAttr.Name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingOption.PropertyName != null)
		{
			var name = useLowercase ? matchingOption.ConfigAttr.Name.ToLower() : matchingOption.ConfigAttr.Name;
			var value = matchingOption.Value?.ToString() ?? "null";
			var desc = matchingOption.ConfigAttr.Description;

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), executor, name, value);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionDescriptionFormat), executor, desc);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionCategoryFormat), executor, matchingOption.Category);
			return new CallState(value);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigNoCategoryOrOptionFormat), executor, searchTerm);
		return new CallState(ErrorMessages.Returns.NotFound);
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

	[SharpCommand(Name = "@FUNCTION",
		Switches = ["ALIAS", "BUILTIN", "CLONE", "DELETE", "ENABLE", "DISABLE", "PRESERVE", "RESTORE", "RESTRICT", "LOCAL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 5, ParameterNames = ["name", "object/attribute"])]
	public async ValueTask<Option<CallState>> Function(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("LOCAL")) return await LocalFunctionCommand(parser, executor, switches);

		if (args.Count == 0)
		{
			if (FunctionLibrary == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable), executor);
				return new CallState(ErrorMessages.Returns.LibraryUnavailable);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionGlobalUserDefinedHeader), executor);

			var canSeeDetails = await executor.IsWizard();

			// Global user-defined functions live in the in-memory registry (@function), not the
			// FunctionLibrary; the library holds only built-ins (and any compiled-in defs).
			var registry = parser.ServiceProvider.GetService<IUserDefinedFunctionService>();
			var userFunctions = registry?.All().ToArray() ?? [];
			var builtinFunctions = FunctionLibrary.Where(kvp => kvp.Value.IsSystem).ToArray();

			if (canSeeDetails)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionUserDefinedCountFormat), executor, userFunctions.Length);
				foreach (var fn in userFunctions.Take(10))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEntryFormat), executor, fn.Name, fn.MinArgs, fn.MaxArgs, fn.Enabled ? "Enabled" : "Disabled");
				}
				if (userFunctions.Length > 10)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionAndMoreFormat), executor, userFunctions.Length - 10);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltInCountFormat), executor, builtinFunctions.Length);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionUserDefinedSummaryFormat), executor, userFunctions.Length);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltInSummaryFormat), executor, builtinFunctions.Length);
			}

			return CallState.Empty;
		}

		var functionName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(functionName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyName), executor);
			return new CallState(ErrorMessages.Returns.NoFunctionSpecified);
		}

		var userFunctionService = parser.ServiceProvider.GetService<IUserDefinedFunctionService>();
		if (userFunctionService == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable), executor);
			return new CallState(ErrorMessages.Returns.LibraryUnavailable);
		}

		if (switches.Contains("ALIAS"))
		{
			var aliasName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(aliasName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyAliasName), executor);
				return new CallState(ErrorMessages.Returns.NoAliasSpecified);
			}

			// @function/alias <alias>=<existing-user-function>
			// functionName is the alias being created; aliasName is the existing target.
			if (!userFunctionService.Alias(functionName, aliasName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, aliasName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionAliasWouldCreateFormat), executor, functionName, aliasName);
			return CallState.Empty;
		}

		if (switches.Contains("CLONE"))
		{
			// @function/clone <new>=<existing>: create <new> mirroring <existing> (built-in or user)
			// so the clone can be independently restricted/disabled without touching the original.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var existingName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(existingName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyCloneName), executor);
				return new CallState(ErrorMessages.Returns.NoCloneNameSpecified);
			}

			// Prefer a user-defined source; fall back to a built-in in the FunctionLibrary.
			if (userFunctionService.Get(existingName) is not null)
			{
				if (!userFunctionService.Clone(functionName, existingName))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, existingName);
					return new CallState(ErrorMessages.Returns.FunctionNotFound);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionClonedFormat), executor, functionName, existingName);
				return CallState.Empty;
			}

			if (FunctionLibrary != null && FunctionLibrary.TryGetValue(existingName.ToUpper(), out var builtinSource) && builtinSource.IsSystem)
			{
				// Register the clone under <new> pointing at the SAME FunctionDefinition; it is a
				// system function (IsSystem=true) so it resolves like a built-in, but its name is
				// distinct, so @function/restrict and @function/builtin can act on it alone.
				FunctionLibrary[functionName.ToUpper()] = builtinSource;
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionClonedFormat), executor, functionName, existingName);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, existingName);
			return new CallState(ErrorMessages.Returns.FunctionNotFound);
		}

		if (switches.Contains("BUILTIN"))
		{
			// @function/builtin <function>: discard a user override/clone so the original built-in
			// (regenerated by the function-library source generator) resolves again. We remove any
			// registry entry, any restriction overlay, and any cloned/overridden library entry for
			// the name. The generated built-in is re-added lazily on next call (DiscoverBuiltInFunction).
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			userFunctionService.Delete(functionName);
			userFunctionService.SetBuiltinRestriction(functionName, null);
			FunctionLibrary?.Remove(functionName.ToUpper());
			RestoreBuiltinFunction(functionName);

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltinRestoredFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("PRESERVE"))
		{
			// @function/preserve <function>: mark a user function to survive a bulk
			// @function/restore reset and to be reported for re-registration.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (!userFunctionService.SetPreserved(functionName, true))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionPreservedFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("RESTORE"))
		{
			// @function/restore <function>: discard the user override of a single name so its
			// built-in resolves again (same outcome as /builtin).
			// @function/restore * : bulk reset — remove every user function NOT marked /preserve,
			// keeping the preserved set for re-registration.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (functionName.Equals("*", StringComparison.Ordinal))
			{
				var removed = userFunctionService.ResetUnpreserved();
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestoredResetFormat), executor, removed);
				return CallState.Empty;
			}

			userFunctionService.Delete(functionName);
			userFunctionService.SetBuiltinRestriction(functionName, null);
			FunctionLibrary?.Remove(functionName.ToUpper());
			RestoreBuiltinFunction(functionName);

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestoredOneFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			// /delete removes a user-defined or cloned function, OR "deletes" a built-in from the
			// library so a user @function can override it (PennMUSH semantics). The "deleted"
			// built-in is still reachable via fn() and can be brought back with /builtin or /restore.
			var removedUser = userFunctionService.Delete(functionName);
			var removedBuiltin = FunctionLibrary?.Remove(functionName.ToUpper()) ?? false;

			if (!removedUser && !removedBuiltin)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDeleteWouldDeleteFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("DISABLE"))
		{
			if (!userFunctionService.SetEnabled(functionName, false))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDisableWouldDisableFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("ENABLE"))
		{
			if (!userFunctionService.SetEnabled(functionName, true))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEnableWouldEnableFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("RESTRICT"))
		{
			// @function/restrict <function>=<restriction>: set the permission restriction on a
			// function. A user function stores it on its registry entry; a built-in (or "deleted"
			// built-in / clone) stores it in the registry's built-in restriction overlay, consulted
			// at call time. An empty restriction clears it.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var restriction = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			var clearing = string.IsNullOrWhiteSpace(restriction);

			if (userFunctionService.Get(functionName) is not null)
			{
				userFunctionService.SetRestriction(functionName, restriction);
			}
			else if (FunctionLibrary != null && FunctionLibrary.ContainsKey(functionName.ToUpper()))
			{
				userFunctionService.SetBuiltinRestriction(functionName, restriction);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			if (clearing)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestrictionClearedFormat), executor, functionName);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestrictedFormat), executor, functionName, restriction!);
			}

			return CallState.Empty;
		}

		// Defining a new function: @function <name>=<obj>,<attrib>[,<min>,<max>]
		// CB.RSArgs splits the RHS on commas: args["1"]=obj, ["2"]=attrib, ["3"]=min, ["4"]=max.
		if (args.Count >= 2)
		{
			var objSpec = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			var attribSpec = args.GetValueOrDefault("2")?.Message?.ToPlainText();

			if (!string.IsNullOrEmpty(objSpec))
			{
				if (string.IsNullOrEmpty(attribSpec))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyName), executor);
					return new CallState(ErrorMessages.Returns.NoFunctionSpecified);
				}

				// Built-in functions take precedence and may not be overridden by a user function.
				if (FunctionLibrary != null && FunctionLibrary.TryGetValue(functionName.ToUpper(), out var existing) && existing.IsSystem)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
					return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, functionName.ToUpperInvariant()));
				}

				// Parse min/max arg bounds (default 0..32, the engine-wide max).
				var minArgs = 0;
				var maxArgs = 32;
				if (args.Count >= 4 && int.TryParse(args.GetValueOrDefault("3")?.Message?.ToPlainText(), out var parsedMin))
				{
					minArgs = parsedMin;
				}
				if (args.Count >= 5 && int.TryParse(args.GetValueOrDefault("4")?.Message?.ToPlainText(), out var parsedMax))
				{
					maxArgs = parsedMax;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser, executor, executor, objSpec, LocateFlags.All, async targetObject =>
				{
					if (!await PermissionService.Controls(executor, targetObject))
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
						return new CallState(ErrorMessages.Returns.PermissionDenied);
					}

					if (await AttributeService.GetAttributeAsync(
							executor, targetObject, attribSpec, IAttributeService.AttributeMode.Read, false)
						is not SharpAttribute[] attributeChain)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, attribSpec);
						return new CallState(ErrorMessages.Returns.NoSuchAttribute);
					}

					var attributeLongName = attributeChain.Last().LongName!.ToUpper();

					userFunctionService.Define(new UserDefinedFunction(
						Name: functionName,
						Object: targetObject.Object().DBRef,
						Attribute: attributeLongName,
						MinArgs: minArgs,
						MaxArgs: maxArgs,
						Enabled: true,
						AliasOf: null));

					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDefineWouldDefineFormat), executor, functionName, $"{targetObject.Object().DBRef}/{attributeLongName}");
					return CallState.Empty;
				});
			}
		}

		var registeredFunction = userFunctionService.Get(functionName);
		if (registeredFunction != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), executor, registeredFunction.Name);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoTypeFormat), executor, "User-defined");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMinArgsFormat), executor, registeredFunction.MinArgs);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMaxArgsFormat), executor, registeredFunction.MaxArgs);
			return CallState.Empty;
		}

		if (FunctionLibrary == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable), executor);
			return new CallState(ErrorMessages.Returns.LibraryUnavailable);
		}

		var functionNameUpper = functionName.ToUpper();
		if (!FunctionLibrary.TryGetValue(functionNameUpper, out var functionInfo))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
			return new CallState(ErrorMessages.Returns.FunctionNotFound);
		}

		var (definition, isSystem) = functionInfo;
		var attr = definition.Attribute;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), executor, attr.Name);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoTypeFormat), executor, isSystem ? "Built-in" : "User-defined");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMinArgsFormat), executor, attr.MinArgs);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMaxArgsFormat), executor, attr.MaxArgs);

		var flags = Enum.GetValues<FunctionFlags>()
			.Where(flag => flag != FunctionFlags.Regular && flag != FunctionFlags.Arg_Mask && attr.Flags.HasFlag(flag))
			.Select(flag => flag.ToString()).ToList();
		if (flags.Count == 0) flags.Add(nameof(FunctionFlags.Regular));

		if (flags.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoFlagsFormat), executor, string.Join(" | ", flags));
		}

		if (attr.Restrict != null && attr.Restrict.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoRestrictionsFormat), executor, string.Join(", ", attr.Restrict));
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Re-registers a built-in function into the shared FunctionLibrary from the source-generated
	/// definition dictionary, undoing a prior <c>@function/delete</c> of a built-in. No-op if the
	/// name is not a generated built-in (e.g. it was a pure user function).
	/// </summary>
	private void RestoreBuiltinFunction(string functionName)
	{
		var key = functionName.ToLowerInvariant();
		if (Functions.Builtins.TryGetValue(key, out var definition))
		{
			FunctionLibrary[key] = (definition, true);
		}
	}

	private static bool TryGetQueueEntry(ITaskScheduler scheduler, long pid,
		out SharpMUSH.Library.Models.SchedulerModels.QueueEntrySnapshot? entry)
	{
		try { entry = scheduler.GetQueueEntry(pid); return true; }
		catch (NotSupportedException) { entry = null; return false; }
	}

	private async ValueTask<Option<CallState>> QueueInspectionUnsupported(AnySharpObject executor)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotSupportedForSharpMUSH), executor);
		return new CallState(ErrorMessages.Returns.ErrorNotSupported);
	}

	[SharpCommand(Name = "@PS", Switches = ["ALL", "SUMMARY", "COUNT", "QUICK", "DEBUG", "HISTORY"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["player, pid, or history-limit"])]
	public async ValueTask<Option<CallState>> ProcessStatus(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (parser.CurrentState.Switches.Contains("HISTORY")) return await QueueHistory(parser);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();

		if (switches.Contains("DEBUG"))
		{
			var pidStr = args.GetValueOrDefault("0")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(pidStr))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltMustSpecifyPid), executor);
				return new CallState(ErrorMessages.Returns.NoPidSpecified);
			}

			if (!long.TryParse(pidStr, out var pid))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltInvalidPidFormat), executor);
				return new CallState(ErrorMessages.Returns.InvalidPid);
			}

			if (!TryGetQueueEntry(scheduler, pid, out var queued)) return await QueueInspectionUnsupported(executor);
			if (queued is null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsNoTaskWithPidFormat), executor, pid);
				return new CallState(ErrorMessages.Returns.NotFound);
			}
			if (!await parser.ServiceProvider.GetRequiredService<IQueueControlService>()
				.CanAccessLegacyAsync(executor, pid, mutate: false, ExecutionBudget.CurrentToken))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var task = await Mediator.CreateStream(new ScheduleSemaphoreQuery(pid), ExecutionBudget.CurrentToken).FirstOrDefaultAsync(ExecutionBudget.CurrentToken);
			if (task is null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsNoTaskWithPidFormat), executor, pid);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugTaskFormat), executor, pid);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugOwnerFormat), executor, queued.Owner?.ToString() ?? "?");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugSemaphoreFormat), executor, task.SemaphoreSource);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugCommandFormat), executor, task.Command.ToPlainText());
			if (task.RunDelay.HasValue)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugDelayFormat), executor, task.RunDelay.Value.TotalSeconds.ToString("F1"));
			}

			return CallState.Empty;
		}

		AnySharpObject target;
		if (args.Count > 0)
		{
			var playerName = args["0"].Message?.ToPlainText();
			if (string.IsNullOrEmpty(playerName))
			{
				target = executor;
			}
			else
			{
				if (await LocateService.LocateAndNotifyIfInvalid(
						parser, executor, executor, playerName, LocateFlags.All) is not AnySharpObject located)
				{
					return new CallState(ErrorMessages.Returns.InvalidTarget);
				}
				target = located;
			}
		}
		else
		{
			target = executor;
		}

		if (!await PermissionService.Controls(executor, target) && !await executor.IsPriv() && !await executor.HasPower("SEE_QUEUE"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var targetDbRef = target.Object().DBRef;

		if (switches.Contains("ALL"))
		{
			if (!await executor.IsPriv() && !await executor.HasPower("SEE_QUEUE"))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var allTasks = await Mediator.CreateStream(new ScheduleAllTasksQuery()).ToArrayAsync();
			// Usage is an optional extension; legacy schedulers still provide the queue listing.
			SharpMUSH.Library.Models.SchedulerModels.QueueUsage? usage;
			try { usage = scheduler.GetQueueUsage(); }
			catch (NotSupportedException) { usage = null; }
			if (usage is not null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueUsage), usage.Total, Configuration.CurrentValue.Limit.GlobalQueueLimit, Configuration.CurrentValue.Limit.PlayerQueueLimit);
				foreach (var rejection in usage.Rejections.OrderBy(x => x.Key))
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueueRejections), rejection.Key, rejection.Value);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAllHeader), executor);
			foreach (var (group, tasks) in allTasks)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAllGroupFormat), executor, group, tasks.Length);
			}

			return CallState.Empty;
		}


		SharpMUSH.Library.Models.SchedulerModels.SemaphoreTaskData[] semaphoreTasks;
		try
		{
			semaphoreTasks = await Mediator.CreateStream(new ScheduleSemaphoreQuery(targetDbRef))
				.Where(async (task, ct) => await parser.ServiceProvider.GetRequiredService<IQueueControlService>()
					.CanAccessLegacyAsync(executor, task.Pid, mutate: false, ct)).ToArrayAsync();
		}
		catch (NotSupportedException)
		{
			// A legacy scheduler cannot prove source ownership for these command bodies.
			return await QueueInspectionUnsupported(executor);
		}
		var delayTasks = await Mediator.CreateStream(new ScheduleDelayQuery(targetDbRef)).ToArrayAsync();
		var enqueueTasks = await Mediator.CreateStream(new ScheduleEnqueueQuery(targetDbRef)).ToArrayAsync();

		if (switches.Contains("SUMMARY"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSummaryHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsCommandQueueFormat), executor, enqueueTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueFormat), executor, delayTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreQueueFormat), executor, semaphoreTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsLoadAverageZero), executor);
			return CallState.Empty;
		}

		if (switches.Contains("QUICK"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsQuickHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsCommandQueueFormat), executor, enqueueTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueFormat), executor, delayTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreQueueFormat), executor, semaphoreTasks.Length);
			return CallState.Empty;
		}

		var targetName = target.Object().DBRef.ToString();
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsQueueForTargetFormat), executor, targetName);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsCommandQueueFormat), executor, enqueueTasks.Length);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueFormat), executor, delayTasks.Length);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreQueueFormat), executor, semaphoreTasks.Length);

		if (semaphoreTasks.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EmptyLine), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreTasksHeader), executor);
			foreach (var task in semaphoreTasks.Take(10))
			{
				var delay = task.RunDelay.HasValue ? $"+{task.RunDelay.Value.TotalSeconds:F1}s" : "ready";
				var commandText = task.Command.ToPlainText();
				var truncatedCommand = commandText.Length > 40
					? commandText[..40]
					: commandText;
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreTaskEntryFormat), executor, task.Pid, task.SemaphoreSource, delay, truncatedCommand);
			}
			if (semaphoreTasks.Length > 10)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAndMoreFormat), executor, semaphoreTasks.Length - 10);
			}
		}

		if (delayTasks.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EmptyLine), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueHeader), executor);
			foreach (var pid in delayTasks.Take(10))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitTaskEntryFormat), executor, pid);
			}
			if (delayTasks.Length > 10)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAndMoreFormat), executor, delayTasks.Length - 10);
			}
		}
		IReadOnlyList<SharpMUSH.Library.Models.SchedulerModels.QueueEntrySnapshot>? entries;
		try { entries = scheduler.GetQueueEntries(); }
		catch (NotSupportedException) { entries = null; }
		if (entries is not null)
		{
			var paused = entries.Count(e => e.State == SharpMUSH.Library.Models.SchedulerModels.QueueEntryState.Paused
				&& (e.Source == targetDbRef || e.Owner == targetDbRef));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QueuePausedHint), executor, paused);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@TRIGGER",
		Switches = ["CLEARREGS", "SPOOF", "INLINE", "NOBREAK", "LOCALIZE", "INPLACE", "MATCH"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 1, MaxArgs = int.MaxValue, ParameterNames = ["object/attribute", "arguments..."])]
	public async ValueTask<Option<CallState>> Trigger(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerMustSpecifyAttributePath), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		var parts = attributePath.Split('/', 2);
		if (parts.Length < 2)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerMustSpecifyObjectAttributePath), executor);
			return new CallState(ErrorMessages.Returns.InvalidPath);
		}

		var objectName = parts[0];
		var attributeName = parts[1];

		// Locate the target object AS THE EXECUTOR (looker=executor, perm=executor). The object running
		// @trigger names and must control the target (the Controls(executor, target) check below), and
		// PennMUSH matches command arguments relative to the executor (oracle-confirmed). Passing the
		// enactor as the permission object made the looker-gate (LocateService: !Nearby && !See_All &&
		// !Controls) fail when a mortal, REMOTE enactor triggered a $-command that does @trigger %!/attr.
		// This only fixes the LOOKUP; @trigger's distinct semantics are unchanged — the attribute is still
		// QUEUED to run AS the target object (the new executor), with the triggerer as the enactor (below).
		return await LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, objectName, LocateFlags.All) switch
		{
			AnySharpObject targetObject => await TriggerAsync(parser, executor, enactor, targetObject, args, switches,
				attributeName),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> TriggerAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject enactor, AnySharpObject targetObject, Dictionary<string, CallState> args, string[] switches,
		string attributeName)
	{
		if (!await PermissionService.Controls(executor, targetObject))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerPermissionDeniedDoNotControl), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (await AttributeService.GetAttributeAsync(
				executor, targetObject, attributeName, IAttributeService.AttributeMode.Read, false)
			is not SharpAttribute[] attributeChain)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerNoSuchAttributeFormat), executor, attributeName);
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		var attribute = attributeChain.Last();
		var attributeText = attribute.Value.ToPlainText();
		var attributeLongName = attribute.LongName!.ToUpper();

		if (string.IsNullOrWhiteSpace(attributeText))
		{
			return CallState.Empty;
		}

		// Determine enactor for execution based on /spoof switch.
		// PennMUSH semantics (@trigger2 help):
		//   No /spoof (default): the object USING @trigger (executor) becomes the enactor (%#)
		//   /spoof: preserve the current enactor (the original player who started the chain)
		var executionEnactor = switches.Contains("SPOOF") ? enactor.Object().DBRef : executor.Object().DBRef;

		// Build argument registers from all provided arguments.
		// args["0"] is the object/attribute path (LHS); args["1"] onward are the comma-separated
		// RSArgs that become %0, %1, %2, … inside the triggered attribute.
		// These go into EnvironmentRegisters (the positional %0-%9 args), NOT the q-register stack.
		var envRegisters = new Dictionary<string, CallState>();
		for (var i = 1; i < args.Count; i++)
		{
			if (args.TryGetValue(i.ToString(), out var argValue) && argValue.Message != null)
			{
				envRegisters[(i - 1).ToString()] = argValue;
			}
		}

		// Q-registers from the calling context are copied into the triggered attribute unless
		// /clearregs is specified (PennMUSH @trigger2 help: "Q-registers set at the time @trigger
		// is run will be copied and made available in the triggered attribute").
		var registerStack = new ConcurrentStack<Dictionary<string, MString>>();
		if (switches.Contains("CLEARREGS"))
		{
			registerStack.Push(new Dictionary<string, MString>());
		}
		else
		{
			parser.CurrentState.Registers.TryPeek(out var currentRegs);
			registerStack.Push(currentRegs != null ? new Dictionary<string, MString>(currentRegs) : new());
		}

		if (switches.Contains("MATCH"))
		{
			// With /match, the first argument (index 1) is the test string
			if (!args.TryGetValue("1", out var matchArg) || matchArg.Message == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerMustProvideMatchString), executor);
				return new CallState(ErrorMessages.Returns.NoMatchString);
			}

			var testString = matchArg.Message.ToPlainText();

			var patterns = attributeText.Split(new[] { '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);

			bool matchFound = false;
			foreach (var pattern in patterns)
			{
				var trimmedPattern = pattern.Trim();
				if (string.IsNullOrEmpty(trimmedPattern)) continue;

				var regex = SoftcodeRegex.Wildcard(trimmedPattern);

				if (SoftcodeRegex.IsMatch(regex, testString))
				{
					matchFound = true;
					break;
				}
			}

			if (!matchFound)
			{
				return CallState.Empty;
			}
		}

		// Note: INLINE switch executes immediately (current default behavior).
		// Queue dispatch available via AdmitCommandListRequest if needed for future enhancements.

		return await ExecuteAttributeWithTracking(parser, attributeLongName, async () =>
		{
			var stateWithRegisters = parser.CurrentState with
			{
				Executor = targetObject.Object().DBRef,
				Enactor = executionEnactor,
				Caller = parser.CurrentState.Executor,
				Registers = registerStack,
				EnvironmentRegisters = envRegisters
			};

			var result = await parser.With(state => stateWithRegisters, newParser => newParser.WithAttributeDebug(attribute,
				async p => await p.CommandListParseVisitor(attribute.Value)()));

			return CallState.Empty with { HadErrors = result?.HadErrors == true };
		});
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

	[SharpCommand(Name = "@DECOMPILE", Switches = ["DB", "NAME", "PREFIX", "TF", "FLAGS", "ATTRIBS", "SKIPDEFAULTS"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object", "name"])]
	public async ValueTask<Option<CallState>> Decompile(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouMustSpecifyObjectToDecompile), executor);
			return new CallState(ErrorMessages.Returns.NoObjectSpecified);
		}

		var objectSpec = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(objectSpec))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouMustSpecifyObjectToDecompile), executor);
			return new CallState(ErrorMessages.Returns.NoObjectSpecified);
		}

		var prefix = args.Count >= 2 ? args["1"].Message?.ToPlainText() ?? "" : "";

		if (switches.Contains("TF"))
		{
			var tfPrefixAttr = await AttributeService.GetAttributeAsync(executor, executor, "TFPREFIX",
				IAttributeService.AttributeMode.Read, false);

			prefix = tfPrefixAttr is SharpAttribute[] attr
				? attr.Last().Value.ToPlainText()
				: "FugueEdit > ";
		}

		string? attributePattern = null;
		AnyOptionalSharpObject target;

		if (HelperFunctions.SplitDbRefAndOptionalAttr(objectSpec) is { Object: var objectName, Attribute: var maybeAttributePattern })
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

			target = located;
		}
		else
		{
			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				objectSpec,
				LocateFlags.All);

			if (locate is not AnySharpObject located)
			{
				return new None();
			}

			target = located;
		}

		if (target is not AnySharpObject targetKnown)
		{
			return new None();
		}

		var canExamine = await PermissionService.CanExamine(executor, targetKnown);
		if (!canExamine)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var obj = targetKnown.Object();
		var useDbRef = switches.Contains("DB");
		var useName = switches.Contains("NAME") || !useDbRef; // NAME is default
		var showFlags = switches.Contains("FLAGS") || (!switches.Contains("ATTRIBS") && string.IsNullOrEmpty(attributePattern));
		var showAttribs = switches.Contains("ATTRIBS") || (!switches.Contains("FLAGS") && string.IsNullOrEmpty(attributePattern)) || !string.IsNullOrEmpty(attributePattern);
		var skipDefaults = switches.Contains("SKIPDEFAULTS");
		var isTf = switches.Contains("TF");

		if (!string.IsNullOrEmpty(attributePattern))
		{
			showFlags = false;
			showAttribs = true;
		}

		var objectRef = useDbRef ? $"#{obj.DBRef.Number}" : obj.Name;
		var outputs = new List<string>();

		if (showFlags)
		{
			var createCmd = obj.Type.ToUpperInvariant() switch
			{
				"ROOM" => $"@dig {objectRef}",
				"EXIT" => $"@open {objectRef}",
				"THING" => $"@create {objectRef}",
				"PLAYER" => $"@pcreate {objectRef}",
				_ => $"@create {objectRef}"
			};
			outputs.Add($"{prefix}{createCmd}");

			await foreach (var flag in obj.Flags.Value)
			{
				if (skipDefaults && IsDefaultFlag(obj.Type, flag.Name))
				{
					continue;
				}
				outputs.Add($"{prefix}@set {objectRef}={flag.Name}");
			}

			await foreach (var power in obj.Powers.Value)
			{
				outputs.Add($"{prefix}@power {objectRef}={power.Name}");
			}

			foreach (var (lockName, lockData) in obj.Locks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
			{
				if (!BooleanExpressionParser.IsBound(lockData.LockString))
				{
					outputs.Add($"{prefix}@@ Invalid {lockName} lock omitted; replace it explicitly before decompiling.");
					continue;
				}
				var expression = await BooleanExpressionParser.RenderAsync(lockData.LockString, executor, LockRenderMode.Decompile, ExecutionBudget.CurrentToken);
				var standard = LockService.SystemLocks.TryGetValue(lockName, out var defaults);
				var switchName = standard ? LockNames.Display(lockName) : $"user:{lockName}";
				outputs.Add($"{prefix}@lock/{switchName} {objectRef}={expression}");
				foreach (var (flagName, (_, flag)) in LockService.LockPrivileges)
				{
					var set = lockData.Flags.HasFlag(flag);
					if (set && (!skipDefaults || !defaults.HasFlag(flag))) outputs.Add($"{prefix}@lset {objectRef}/{lockName}={flagName}");
					else if (!set && defaults.HasFlag(flag)) outputs.Add($"{prefix}@lset {objectRef}/{lockName}=!{flagName}");
				}
			}

			if (await obj.Parent.WithCancellation(CancellationToken.None) is AnySharpObject parent)
			{
				var parentObj = parent.Object();
				outputs.Add($"{prefix}@parent {objectRef}={parentObj.DBRef}");
			}
		}

		if (showAttribs)
		{
			SharpAttributesOrError atrs;
			if (!string.IsNullOrEmpty(attributePattern))
			{
				atrs = await AttributeService.GetAttributePatternAsync(
					executor,
					targetKnown,
					attributePattern,
					false, // don't check parents for decompile
					IAttributeService.AttributePatternMode.Wildcard);
			}
			else
			{
				atrs = await AttributeService.GetVisibleAttributesAsync(executor, targetKnown);
			}

			if (atrs is SharpAttribute[] decompiledAttributes)
			{
				foreach (var attr in decompiledAttributes)
				{
					const string VeiledFlagName = "VEILED";
					if (attr.Flags.Any(f => f.Name.Equals(VeiledFlagName, StringComparison.OrdinalIgnoreCase)))
					{
						continue;
					}

					var hasAnsi = ContainsAnsiMarkup(attr.Value);

					if (hasAnsi)
					{
						// Use @set format with decomposed value to ensure evaluation
						var decomposedValue = DecomposeAttributeValue(attr.Value);
						outputs.Add($"{prefix}@set {objectRef}={attr.Name}:{decomposedValue}");
					}
					else
					{
						var plainValue = attr.Value.ToPlainText();
						outputs.Add($"{prefix}&{attr.Name} {objectRef}={plainValue}");
					}

					if (!isTf && attr.Flags.Any())
					{
						if (!skipDefaults || !await AreDefaultAttrFlagsAsync(attr.Name, attr.Flags))
						{
							foreach (var flag in attr.Flags)
							{
								outputs.Add($"{prefix}@set {objectRef}/{attr.Name}={flag.Name}");
							}
						}
					}
				}
			}
		}

		foreach (var output in outputs)
		{
			await NotifyService.Notify(executor, output, executor);
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Checks if an MString contains ANSI markup
	/// </summary>
	private bool ContainsAnsiMarkup(MString str)
	{
		var hasAnsi = false;
		MarkupWalker.EvaluateWith((markupType, innerText) =>
		{
			if (markupType is Ansi)
			{
				hasAnsi = true;
			}
			return innerText;
		}, str);
		return hasAnsi;
	}

	/// <summary>
	/// Decomposes an attribute value using the decompose() logic from StringFunctions
	/// </summary>
	private string DecomposeAttributeValue(MString input)
	{
		// Use same logic as decompose() function from StringFunctions
		var reconstructed = MarkupWalker.EvaluateWith((markupType, innerText) =>
		{
			return markupType switch
			{
				Ansi ansiMarkup
					=> Implementation.Functions.Functions.ReconstructAnsiCall(ansiMarkup.Style, innerText),
				_ => innerText
			};
		}, input);

		var result = reconstructed
			.Replace("\\", @"\\")
			.Replace("%", "\\%")
			.Replace(";", "\\;")
			.Replace("[", "\\[")
			.Replace("]", "\\]")
			.Replace("{", "\\{")
			.Replace("}", "\\}")
			.Replace("(", "\\(")
			.Replace(")", "\\)")
			.Replace(",", "\\,")
			.Replace("^", "\\^")
			.Replace("$", "\\$");

		result = MultipleWhitespaceRegex().Replace(result, m => string.Join("", Enumerable.Repeat("%b", m.Length)));

		result = result.Replace("\r", "%r").Replace("\n", "%r").Replace("\t", "%t");

		return result;
	}

	/// <summary>
	/// Checks if a flag is a default flag for the object type
	/// </summary>
	private bool IsDefaultFlag(string type, string flagName)
	{
		var defaultFlags = type.ToUpperInvariant() switch
		{
			"PLAYER" => Configuration?.CurrentValue.Flag.PlayerFlags ?? [],
			"ROOM" => Configuration?.CurrentValue.Flag.RoomFlags ?? [],
			"THING" => Configuration?.CurrentValue.Flag.ThingFlags ?? [],
			"EXIT" => Configuration?.CurrentValue.Flag.ExitFlags ?? [],
			_ => Array.Empty<string>()
		};

		return defaultFlags.Any(f => f.Equals(flagName, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Checks if attribute flags are the default for that attribute
	/// </summary>
	private async ValueTask<bool> AreDefaultAttrFlagsAsync(string attrName, IEnumerable<SharpAttributeFlag> flags)
	{
		var entry = await Mediator.Send(new GetAttributeEntryQuery(attrName.ToUpper()));

		if (entry == null)
		{
			// No entry means no custom defaults; empty flags are considered default
			return !flags.Any();
		}

		var currentFlagNames = flags.Select(f => f.Name.ToUpper()).OrderBy(n => n);
		var defaultFlagNames = entry.DefaultFlags.Select(f => f.ToUpper()).OrderBy(n => n);

		return currentFlagNames.SequenceEqual(defaultFlagNames);
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

	[SharpCommand(Name = "@STATS", Switches = ["CHUNKS", "FREESPACE", "PAGING", "REGIONS", "TABLES", "FLAGS"],
		Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> Stats(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("TABLES"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsTablesNotImplemented), executor);
			return new CallState(ErrorMessages.Returns.NotImplemented);
		}

		if (switches.Contains("FLAGS"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsFlagsNotImplemented), executor);
			return new CallState(ErrorMessages.Returns.NotImplemented);
		}

		if (switches.Contains("CHUNKS") || switches.Contains("FREESPACE") ||
				switches.Contains("PAGING") || switches.Contains("REGIONS"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsMemorySwitchesNotImplemented), executor);
			return new CallState(ErrorMessages.Returns.NotImplemented);
		}

		string? playerName = null;
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			playerName = args["0"].Message?.ToPlainText();
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsDatabaseStatisticsHeader), executor);

		if (playerName != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsForPlayerFormat), executor, playerName);
		}

		var countsByType = await Mediator.CreateStream(new GetAllObjectsQuery())
			.CountBy(o => o.Type)
			.ToDictionaryAsync(x => x.Key, x => x.Value);
		var roomCount = countsByType.GetValueOrDefault("ROOM");
		var exitCount = countsByType.GetValueOrDefault("EXIT");
		var thingCount = countsByType.GetValueOrDefault("THING");
		var playerCount = countsByType.GetValueOrDefault("PLAYER");
		var totalCount = roomCount + exitCount + thingCount + playerCount;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsRoomsFormat), executor, roomCount);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsExitsFormat), executor, exitCount);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsThingsFormat), executor, thingCount);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsPlayersFormat), executor, playerCount);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsTotalFormat), executor, totalCount);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@ENTRANCES", Switches = ["EXITS", "THINGS", "PLAYERS", "ROOMS"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 3, ParameterNames = ["object", "flags"])]
	public async ValueTask<Option<CallState>> Entrances(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		AnySharpObject targetObject;

		if (args.Count > 0 && args.ContainsKey("0"))
		{
			var targetName = args["0"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(targetName))
			{
				if (await LocateService.LocateAndNotifyIfInvalid(
						parser, executor, executor, targetName, LocateFlags.All) is not AnySharpObject located)
				{
					return new CallState(ErrorMessages.Returns.NotFound);
				}

				targetObject = located;
			}
			else
			{
				var location = await executor.AsContent.Location();
				targetObject = location.WithExitOption();
			}
		}
		else
		{
			var location = await executor.AsContent.Location();
			targetObject = location.WithExitOption();
		}

		int? beginDbref = null;
		int? endDbref = null;

		if (args.Count > 1 && args.ContainsKey("1"))
		{
			var beginStr = args["1"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(beginStr) && int.TryParse(beginStr, out var begin))
			{
				beginDbref = begin;
			}
		}

		if (args.Count > 2 && args.ContainsKey("2"))
		{
			var endStr = args["2"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out var end))
			{
				endDbref = end;
			}
		}

		var targetObj = targetObject.Object();
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesToFormat), executor, targetObj.Name);

		var filterTypes = new List<string>();
		if (switches.Contains("EXITS")) filterTypes.Add("exits");
		if (switches.Contains("THINGS")) filterTypes.Add("things");
		if (switches.Contains("PLAYERS")) filterTypes.Add("players");
		if (switches.Contains("ROOMS")) filterTypes.Add("rooms");

		if (filterTypes.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesFilteringForFormat), executor, string.Join(", ", filterTypes));
		}

		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesRangeFormat), executor, beginDbref ?? 0, endDbref?.ToString() ?? "end");
		}

		var entrances = await Mediator.CreateStream(new GetEntrancesQuery(targetObj.DBRef)).ToListAsync();

		if (filterTypes.Count > 0 && !filterTypes.Contains("exits"))
		{
			entrances.Clear(); // GetEntrancesQuery only returns exits, so if exits not requested, clear
		}

		if (beginDbref.HasValue || endDbref.HasValue)
		{
			entrances = entrances.Where(e =>
			{
				var key = e.Object.Key;
				return (!beginDbref.HasValue || key >= beginDbref.Value) &&
							 (!endDbref.HasValue || key <= endDbref.Value);
			}).ToList();
		}

		if (entrances.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesZeroFound), executor);
		}
		else
		{
			foreach (var entrance in entrances)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesObjectEntryFormat), executor, entrance.Object.Key, entrance.Object.Name);
			}
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesCountFormat), executor, entrances.Count);
		}

		return new CallState(entrances.Count.ToString());
	}

	[SharpCommand(Name = "@GREP", Switches = ["LIST", "PRINT", "ILIST", "IPRINT", "REGEXP", "WILD", "NOCASE", "PARENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "pattern"])]
	public async ValueTask<Option<CallState>> Grep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		if (!args.TryGetValue("0", out var objAttrArg) || !args.TryGetValue("1", out var patternArg))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidArguments), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var objAttrText = objAttrArg.Message!.ToPlainText();
		var pattern = patternArg.Message!.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText) is not { Object: var dbref, Attribute: var maybeAttributePattern })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return new CallState(ErrorMessages.Returns.InvalidObject);
		}

		var attributePattern = string.IsNullOrEmpty(maybeAttributePattern) ? "*" : maybeAttributePattern;

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			dbref,
			LocateFlags.All) switch
		{
			AnySharpObject targetObject => await GrepAsync(parser, executor, targetObject, switches, pattern, attributePattern),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> GrepAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject targetObject, IEnumerable<string> switches, string pattern, string attributePattern)
	{
		var checkParents = switches.Contains("PARENT");

		// PennMUSH treats the obj/attr half of @grep as a single wildcard pattern
		// (predicat.c:1610-1617 defaults it to "*", then hands it to atr_iter_get), and "**" is
		// not a separate matching mode - it is the attribute-name wildcard that is allowed to
		// cross "`" (wild.c:89-107, real_atr_wild). That distinction lives in the wildcard-to-regex
		// translation in the database providers, so every pattern here is Wildcard.
		return await AttributeService.GetAttributePatternAsync(
			executor,
			targetObject,
			attributePattern,
			checkParents,
			IAttributeService.AttributePatternMode.Wildcard) switch
		{
			SharpAttribute[] attributes => await GrepAttributesAsync(parser, executor, attributes, switches, pattern),
			Error<string> error => await GrepUnreadableAsync(executor, error.Value)
		};
	}

	private async ValueTask<Option<CallState>> GrepUnreadableAsync(AnySharpObject executor, string error)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepErrorReadingAttributesFormat), executor, error);
		return new CallState($"#-1 {error}");
	}

	/// <summary>Reports the attributes whose value matches <paramref name="pattern"/>, the way the switches ask.</summary>
	private async ValueTask<Option<CallState>> GrepAttributesAsync(IMUSHCodeParser parser, AnySharpObject executor,
		SharpAttribute[] attributes, IEnumerable<string> switches, string pattern)
	{
		var isWild = switches.Contains("WILD");
		var isRegexp = switches.Contains("REGEXP");
		var isNoCase = switches.Contains("NOCASE") || switches.Contains("ILIST") || switches.Contains("IPRINT");
		var isPrint = switches.Contains("PRINT") || switches.Contains("IPRINT");

		var matchingAttributes = new List<SharpAttribute>();

		foreach (var attr in attributes)
		{
			var attrValue = attr.Value.ToPlainText();
			bool matches = false;

			if (isRegexp)
			{
				try
				{
					var regexOptions = isNoCase ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None;
					matches = System.Text.RegularExpressions.Regex.IsMatch(attrValue, pattern, regexOptions, TimeSpan.FromSeconds(1));
				}
				catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepRegexpTimedOutFormat), executor, pattern);
					return new CallState(ErrorMessages.Returns.RegexpTimeout);
				}
				catch
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidRegexpFormat), executor, pattern);
					return new CallState(ErrorMessages.Returns.InvalidRegexp);
				}
			}
			else if (isWild)
			{
				try
				{
					// grep_util passes cs = ((flags & GREP_NOCASE) == 0), so the wildcard grep is
					// case-SENSITIVE unless this is the "i" variant — unlike every other wildcard in
					// the game, which goes through quick_wild and its cs = 0.
					matches = SoftcodeRegex.Wildcard(pattern, caseSensitive: !isNoCase).IsMatch(attrValue);
				}
				catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepWildcardTimedOutFormat), executor, pattern);
					return new CallState(ErrorMessages.Returns.PatternTimeout);
				}
				catch
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidWildcardFormat), executor, pattern);
					return new CallState(ErrorMessages.Returns.InvalidPattern);
				}
			}
			else
			{
				var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
				matches = attrValue.Contains(pattern, comparison);
			}

			if (matches)
			{
				matchingAttributes.Add(attr);
			}
		}

		if (matchingAttributes.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound), executor);
			return new CallState(string.Empty);
		}

		if (isPrint)
		{
			// Lazily computed: only a flagged attribute needs it, and most @grep/PRINT calls have none.
			int? width = null;

			foreach (var attr in matchingAttributes)
			{
				var parseType = attr.SyntaxParseType();

				MString displayValue;

				if (parseType is null)
				{
					// Byte-identical to the pre-formatting behavior: nothing below this branch may
					// change when SyntaxParseType() is null, since that is the regression contract
					// covering all existing traffic.
					if (isRegexp || isWild)
					{
						displayValue = attr.Value;
					}
					else
					{
						// Highlight the matching parts using Span to avoid allocations
						var plainValue = attr.Value.ToPlainText();
						var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
						var index = plainValue.IndexOf(pattern, comparison);

						if (index >= 0)
						{
							var valueSpan = plainValue.AsSpan();
							var before = valueSpan.Slice(0, index).ToString();
							var match = valueSpan.Slice(index, pattern.Length).ToString();
							var after = valueSpan.Slice(index + pattern.Length).ToString();

							displayValue = MarkupText.Concat(MarkupText.Concat(MarkupText.Plain(before), MarkupText.Plain(match).Hilight()), MarkupText.Plain(after));
						}
						else
						{
							displayValue = attr.Value;
						}
					}
				}
				else
				{
					// The attribute carries a syntax flag: render the formatted, wrapped block instead
					// of the raw value. Empty values are left alone (mirrors @examine) rather than run
					// through the formatter, since an empty funsyntax/cmdsyntax body is itself a parse
					// error and would otherwise surface a stray parser-failure summary in place of blank.
					MString formatted;

					// Plain-text length of the code portion of `formatted` — everything but the error
					// summary the formatter appends beneath it.
					int codeLength;

					if (attr.Value.Length == 0)
					{
						formatted = attr.Value;
						codeLength = 0;
					}
					else
					{
						width ??= await ExecutorFormatWidthAsync(executor);

						var source = attr.Value;
						var tokens = parser.Tokenize(source);
						var semanticTokens = parser.GetSemanticTokens(source, parseType.Value);
						var errors = SoftcodeSource.Validate(parser, source, parseType.Value);
						formatted = SoftcodeFormatter.Format(source, tokens, semanticTokens, errors, width.Value, parser,
							parseType.Value, out codeLength);
					}

					if (isRegexp || isWild)
					{
						displayValue = formatted;
					}
					else
					{
						// Same highlight as the unflagged path, but sliced from the formatted block via
						// MarkupText.Substring (rather than rebuilt from plain-text spans) so the formatter's
						// own syntax colouring survives around the highlighted match.
						//
						// Bounded by codeLength: the attribute matched on its *value*, so the match is in the
						// code. Searching the whole block would let a pattern that occurs only in the appended
						// "#-1 PARSER FAILURE ..." summary highlight as though it were the match that put this
						// attribute in the result set.
						var plainFormatted = formatted.ToPlainText();
						var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
						var index = plainFormatted.IndexOf(pattern, 0, codeLength, comparison);

						if (index >= 0)
						{
							var before = formatted.Substring(0, index);
							var match = formatted.Substring(index, pattern.Length).Hilight();
							var after = formatted.Substring(index + pattern.Length, plainFormatted.Length - index - pattern.Length);

							displayValue = MarkupText.Concat(MarkupText.Concat(before, match), after);
						}
						else
						{
							displayValue = formatted;
						}
					}
				}

				await NotifyService.Notify(executor,
					MarkupText.Concat(MarkupText.Plain($"{attr.Name}: ").Hilight(), displayValue), executor);
			}
		}
		else
		{
			var attrNames = string.Join(" ", matchingAttributes.Select(a => a.Name));
			await NotifyService.Notify(executor, attrNames, executor);
		}

		return new CallState(string.Empty);
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

	[SharpCommand(Name = "@RESTART", Switches = ["ALL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Restart(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();

		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			await foreach (var obj in Mediator.CreateStream(new GetAllTypedObjectsQuery()))
			{
				await Mediator.Send(new HaltObjectQueueRequest(obj.Object().DBRef));
			}

			// Then run @STARTUP on every object — the same pass used at boot, so global
			// @function registrations etc. re-establish identically. Errors are swallowed.
			await StartupAttributeRunner.RunAllAsync(parser, Mediator, AttributeService, executor);

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllObjectsRestarted), executor);
			return CallState.Empty;
		}

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartMustSpecifyObject), executor);
			return new CallState(ErrorMessages.Returns.NoObjectSpecified);
		}

		var targetName = args["0"].Message!.ToPlainText();

		var maybeTarget = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (maybeTarget is not AnySharpObject target)
		{
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		if (!await PermissionService.Controls(executor, target))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var targetObject = target.Object();

		await Mediator.Send(new HaltObjectQueueRequest(targetObject.DBRef));

		if (target.IsPlayer)
		{
			await foreach (var obj in Mediator.CreateStream(new GetAllTypedObjectsQuery()))
			{
				var owner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (owner.Object.DBRef == targetObject.DBRef)
				{
					await Mediator.Send(new HaltObjectQueueRequest(obj.Object().DBRef));

					// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed
					try
					{
						await AttributeService.EvaluateAttributeFunctionAsync(
							parser, executor, obj, "STARTUP",
							new Dictionary<string, CallState>(),
							evalParent: false);
					}
					catch
					{
						// Ignore @STARTUP errors - they're non-fatal
					}
				}
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartedPlayerAndObjectsFormat), executor, targetObject.Name);
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartedObjectFormat), executor, targetObject.Name);
		}

		// Trigger @STARTUP attribute if it exists (never inherited per PennMUSH spec)
		try
		{
			await AttributeService.EvaluateAttributeFunctionAsync(
				parser, executor, target, "STARTUP",
				new Dictionary<string, CallState>(),
				evalParent: false);
		}
		catch
		{
			// Ignore @STARTUP errors - they're non-fatal
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@SWEEP", Switches = ["CONNECTED", "HERE", "INVENTORY", "EXITS"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["flags"])]
	public async ValueTask<Option<CallState>> Sweep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var switches = parser.CurrentState.Switches.ToHashSet();
		var connectFlag = switches.Contains("CONNECTED");
		var hereFlag = switches.Contains("HERE");
		var inventoryFlag = switches.Contains("INVENTORY");
		var exitsFlag = switches.Contains("EXITS");

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var perceive = await ObserveRealityAsync(parser, executor);
		var location = await executor.Where();
		var locationObj = location.Object();
		var locationAnyObject = location.WithExitOption();
		var locationOwner = await locationObj.Owner.WithCancellation(CancellationToken.None);

		if (!inventoryFlag && !exitsFlag)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningInRoom), executor);

			if (connectFlag)
			{
				if (await IsConnectedOrPuppetConnected(locationAnyObject))
				{
					if (location.IsPlayer)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), executor, locationObj.Name);
					}
					else
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), executor, locationObj.Name, locationOwner.Object.Name);
					}
				}
			}
			else
			{
				if (await locationAnyObject.IsHearer(ConnectionService, AttributeService) ||
						await locationAnyObject.IsListener())
				{
					if (await ConnectionService.IsConnected(locationAnyObject))
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomSpeechConnectedFormat), executor, locationObj.Name);
					else
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomSpeechFormat), executor, locationObj.Name);
				}

				if (await locationAnyObject.HasActiveCommands(AttributeService))
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomCommandsFormat), executor, locationObj.Name);
				if (await locationAnyObject.IsAudible())
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomBroadcastingFormat), executor, locationObj.Name);
			}

			var contents = location.Content(Mediator)
				.Where((item, ct) => perceive(item.Object().DBRef, ct));
			await foreach (var obj in contents.WithCancellation(ExecutionBudget.CurrentToken))
			{
				var fullObj = obj.WithRoomOption();
				var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (connectFlag)
				{
					if (await IsConnectedOrPuppetConnected(fullObj))
					{
						if (obj.IsPlayer)
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), executor, obj.Object().Name);
						}
						else
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), executor, obj.Object().Name, objOwner.Object.Name);
						}
					}
				}
				else
				{
					if (await fullObj.IsHearer(ConnectionService, AttributeService) || await fullObj.IsListener())
					{
						if (await ConnectionService.IsConnected(fullObj))
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechConnectedFormat), executor, obj.Object().Name);
						else
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechFormat), executor, obj.Object().Name);
					}

					if (await fullObj.HasActiveCommands(AttributeService))
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectCommandsFormat), executor, obj.Object().Name);
				}
			}
		}

		if (!connectFlag && !inventoryFlag && location.IsRoom && exitsFlag)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningExits), executor);
			if (await locationAnyObject.IsAudible())
			{
				var exits = location.Content(Mediator).Where(x => x.IsExit)
					.Where((item, ct) => perceive(item.Object().DBRef, ct));
				await foreach (var exit in exits.WithCancellation(ExecutionBudget.CurrentToken))
				{
					if (await exit.WithRoomOption().IsAudible())
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepExitBroadcastingFormat), executor, exit.Object().Name);
					}
				}
			}
		}

		if (!hereFlag && !exitsFlag && inventoryFlag)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningInInventory), executor);
			await foreach (var obj in executor.AsContainer.Content(Mediator)
				.Where((item, ct) => perceive(item.Object().DBRef, ct)).WithCancellation(ExecutionBudget.CurrentToken))
			{
				var fullObj = obj.WithRoomOption();
				var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (connectFlag)
				{
					if (await IsConnectedOrPuppetConnected(fullObj))
					{
						if (obj.IsPlayer)
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), executor, obj.Object().Name);
						}
						else
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), executor, obj.Object().Name, objOwner.Object.Name);
						}
					}
				}
				else
				{
					if (await fullObj.IsHearer(ConnectionService, AttributeService) || await fullObj.IsListener())
					{
						if (await ConnectionService.IsConnected(fullObj))
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechConnectedFormat), executor, obj.Object().Name);
						else
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechFormat), executor, obj.Object().Name);
					}

					if (await fullObj.HasActiveCommands(AttributeService))
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectCommandsFormat), executor, obj.Object().Name);
				}
			}
		}

		return CallState.Empty;

		async Task<bool> IsConnectedOrPuppetConnected(AnySharpObject obj)
		{
			if (await ConnectionService.IsConnected(obj)) return true;

			return await obj.IsPuppet()
						 && await ConnectionService.IsConnected(await obj.Object().Owner.WithCancellation(CancellationToken.None));
		}
	}

	/// <summary>
	/// Line-for-line the shape of PennMUSH's <c>do_version</c> (src/version.c): the game's name, the
	/// address <em>only when one is configured</em>, the restart time, then the version banner — which is
	/// the very string <c>version()</c> returns, exactly as PennMUSH's <c>fun_version</c> and
	/// <c>do_version</c> both format from VERSION/PATCHLEVEL/PATCHDATE.
	/// </summary>
	[SharpCommand(Name = "@VERSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Version(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var uptimeData = await ObjectDataService.GetExpandedServerDataAsync<UptimeData>();
		var net = Configuration.CurrentValue.Net;

		var lines = new List<MString> { MarkupText.Plain($"You are connected to {net.MudName}") };

		// PennMUSH: `if (MUDURL && *MUDURL)`. An unset mud_url means the game has no published address,
		// which is not the same fact as "the address is Unknown" — so the line is omitted, not filled in.
		if (!string.IsNullOrWhiteSpace(net.MudUrl))
		{
			lines.Add(MarkupText.Plain($"Address: {net.MudUrl}"));
		}

		if (uptimeData != null)
		{
			lines.Add(MarkupText.Plain($"Last restarted: {uptimeData.LastRebootTime:ddd MMM dd HH:mm:ss yyyy}"));
		}

		lines.Add(MarkupText.Plain(Implementation.Generated.VersionInfo.Version));

		var result = MarkupText.Join(MarkupText.NewLine, lines);

		await NotifyService.Notify(executor, result, executor);

		return new CallState(result);
	}

	[SharpCommand(Name = "@ATTRIBUTE",
		Switches = ["ACCESS", "DELETE", "RENAME", "RETROACTIVE", "LIMIT", "ENUM", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2, ParameterNames = ["attribute", "options..."])]
	public async ValueTask<Option<CallState>> Attribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("DECOMPILE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var pattern = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "*";
			var retroactive = switches.Contains("RETROACTIVE");

			var matchingEntries = await Mediator.CreateStream(new GetAllAttributeEntriesQuery())
				.Where(entry =>
					pattern == "*" ||
					entry.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
					(pattern.Contains('*') && MatchesWildcard(entry.Name, pattern)))
				.ToArrayAsync();

			if (matchingEntries.Length == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNoMatchPatternFormat), executor, pattern);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileHeaderFormat), executor, matchingEntries.Length, pattern);

			foreach (var entry in matchingEntries.OrderBy(e => e.Name))
			{
				var flagList = string.Join(" ", entry.DefaultFlags);
				var retroFlag = retroactive ? "/retroactive" : "";
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileAccessFormat), executor, retroFlag, entry.Name, flagList);

				if (!string.IsNullOrEmpty(entry.Limit))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileLimitFormat), executor, entry.Name, entry.Limit);
				}

				if (entry.Enum != null && entry.Enum.Length > 0)
				{
					var enumList = string.Join(" ", entry.Enum);
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileEnumFormat), executor, entry.Name, enumList);
				}
			}

			return CallState.Empty;
		}

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAttribute), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		var attrName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attrName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAttribute), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		if (switches.Contains("ACCESS"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyFlags), executor);
				return new CallState(ErrorMessages.Returns.NoFlagsSpecified);
			}

			var flagList = args["1"].Message?.ToPlainText() ?? "none";
			var retroactive = switches.Contains("RETROACTIVE");

			var flagNames = flagList.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(f => f.ToUpper())
				.ToArray();

			var allFlags = await Mediator.CreateStream(new GetAttributeFlagsQuery()).ToArrayAsync();
			foreach (var flagName in flagNames)
			{
				if (!allFlags.Any(f => f.Name.Equals(flagName, StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandUnknownFlagFormat), executor, flagName);
					return new CallState(ErrorMessages.Returns.UnknownFlag);
				}
			}

			var entry = await Mediator.Send(new CreateAttributeEntryCommand(attrName.ToUpper(), flagNames));
			if (entry == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandFailedToCreate), executor);
				return new CallState(ErrorMessages.Returns.CreateFailed);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandPermissionsNowFormat), executor, attrName.ToUpperInvariant(), string.Join(" ", flagNames.Select(f => f.ToLowerInvariant())));

			// TODO: Retroactive flag updates to existing attribute instances.
			// When /retroactive is set, should update flags on all existing copies of this attribute
			// across all objects in the database. Requires bulk update operation.
			if (retroactive)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRetroactiveNotImplemented), executor);
			}

			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var deleted = await Mediator.Send(new DeleteAttributeEntryCommand(attrName.ToUpper()));

			if (deleted)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRemovedFromTableFormat), executor, attrName);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandExistingCopiesRemain), executor);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundInTableFormat), executor, attrName);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			return CallState.Empty;
		}

		if (switches.Contains("RENAME"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyNewName), executor);
				return new CallState(ErrorMessages.Returns.NoNewNameSpecified);
			}

			var newName = args["1"].Message?.ToPlainText();
			if (string.IsNullOrEmpty(newName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyNewName), executor);
				return new CallState(ErrorMessages.Returns.NoNewNameSpecified);
			}

			var renamed = await Mediator.Send(new RenameAttributeEntryCommand(attrName.ToUpper(), newName.ToUpper()));

			if (renamed != null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRenamedFormat), executor, attrName, newName);
				// Note: Existing attribute instances keep their original names - this only affects new instances
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundInTableFormat), executor, attrName);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			return CallState.Empty;
		}

		if (switches.Contains("LIMIT"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyPattern), executor);
				return new CallState(ErrorMessages.Returns.NoPatternSpecified);
			}

			var pattern = args["1"].Message?.ToPlainText();
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitSettingPatternFormat), executor, attrName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitPatternFormat), executor, pattern ?? string.Empty);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitNewValuesMustMatch), executor);

			// TODO: Attribute validation via regex patterns.
			// Requirements:
			// - Store regexp pattern with attribute in table
			// - Validate all new attribute values against pattern
			// - Pattern is case insensitive unless (?-i) is used
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandValidationNotImplemented), executor);

			return new CallState(ErrorMessages.Returns.NotImplemented);
		}

		if (switches.Contains("ENUM"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyChoices), executor);
				return new CallState(ErrorMessages.Returns.NoChoicesSpecified);
			}

			var choices = args["1"].Message?.ToPlainText();
			var choiceArray = choices?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

			if (choiceArray.Length == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAtLeastOneChoice), executor);
				return new CallState(ErrorMessages.Returns.NoChoicesSpecified);
			}

			var existingEntry = await Mediator.Send(new GetAttributeEntryQuery(attrName.ToUpper()));
			var defaultFlags = existingEntry?.DefaultFlags ?? [];
			var limit = existingEntry?.Limit;

			// Note: Command parameter is EnumValues, model property is Enum
			var enumAttrEntry = await Mediator.Send(new CreateAttributeEntryCommand(
				attrName.ToUpper(),
				defaultFlags,
				Limit: limit,
				EnumValues: choiceArray));

			if (enumAttrEntry == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandFailedToUpdate), executor);
				return new CallState(ErrorMessages.Returns.UpdateFailed);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumSetChoicesFormat), executor, attrName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumChoicesFormat), executor, string.Join(" ", choiceArray));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumNewValuesMustMatch), executor);

			return CallState.Empty;
		}

		var attrEntry = await Mediator.Send(new GetAttributeEntryQuery(attrName.ToUpper()));

		if (attrEntry == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundNotErrorFormat), executor, attrName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundNotError2), executor);
			return CallState.Empty;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandInfoFormat), executor, attrEntry.Name);

		if (attrEntry.DefaultFlags.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDefaultFlagsFormat), executor, string.Join(" ", attrEntry.DefaultFlags));
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDefaultFlagsNone), executor);
		}

		if (!string.IsNullOrEmpty(attrEntry.Limit))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitPatternValueFormat), executor, attrEntry.Limit);
		}

		if (attrEntry.Enum != null && attrEntry.Enum.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumValuesFormat), executor, string.Join(" ", attrEntry.Enum));
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Simple wildcard matching helper for attribute name patterns.
	/// Supports * as wildcard character.
	/// </summary>
	private bool MatchesWildcard(string text, string pattern)
	{
		var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
		.Replace("\\*", ".*") + "$";

		return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern,
		System.Text.RegularExpressions.RegexOptions.IgnoreCase);
	}

	[GeneratedRegex(@"\s{2,}")]
	private static partial Regex MultipleWhitespaceRegex();
}
