using Microsoft.Extensions.DependencyInjection;
using OneOf;
using OneOf.Types;
using SharpMUSH.Configuration;
using SharpMUSH.Database;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Tools;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using ConfigGenerated = SharpMUSH.Configuration.Generated;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private const string DefaultSemaphoreAttribute = "SEMAPHORE";
	private static readonly string[] DefaultSemaphoreAttributeArray = [DefaultSemaphoreAttribute];

	/// <summary>
	/// Handles delimiter/pid extraction for @dolist and @map commands when /DELIMIT or /PID is used.
	/// Format: @dolist/delimit <delimiter> <list>=<action>
	/// Returns the delimiter/pid value and the remaining list text.
	/// </summary>
	private static (string paramValue, MString listText) ExtractFirstParameter(MString originalListText, bool extractParameter)
	{
		if (!extractParameter)
		{
			return (" ", originalListText);
		}

		var plainListText = MModule.plainText(originalListText);
		if (plainListText.Length == 0)
		{
			// Empty list - return default delimiter and empty list
			return (" ", originalListText);
		}

		// Split on first space to separate parameter from list
		var spaceIndex = plainListText.IndexOf(' ');
		if (spaceIndex <= 0)
		{
			// No space found or space at beginning - treat entire string as parameter, empty list
			return (plainListText, MModule.empty());
		}

		// Use AsSpan() to avoid substring allocations
		var textSpan = plainListText.AsSpan();
		var paramValue = textSpan.Slice(0, spaceIndex).ToString();
		var remainingText = plainListText.Length > spaceIndex + 1
			? MModule.single(textSpan.Slice(spaceIndex + 1).ToString())
			: MModule.empty();

		return (paramValue, remainingText);
	}

	/// <summary>
	/// Helper method to execute attribute content with recursion tracking.
	/// This ensures commands like @INCLUDE, @TRIGGER, etc. track recursion the same way u/ufun/ulocal do.
	/// </summary>
	private static async ValueTask<CallState> ExecuteAttributeWithTracking(
		IMUSHCodeParser parser,
		string attributeLongName,
		Func<Task<CallState>> executeFunc)
	{
		// Get shared tracking collections from parser state
		var callDepth = parser.CurrentState.CallDepth;
		var recursionDepths = parser.CurrentState.FunctionRecursionDepths;
		var limitExceeded = parser.CurrentState.LimitExceeded;

		// If tracking isn't initialized (shouldn't happen in normal flow), just execute without tracking
		if (callDepth == null || recursionDepths == null || limitExceeded == null)
		{
			return await executeFunc();
		}

		// Increment tracking
		callDepth.Increment();
		if (!recursionDepths.TryGetValue(attributeLongName, out var depth))
		{
			depth = 0;
		}
		recursionDepths[attributeLongName] = ++depth;

		// Check recursion limit
		if (depth > Configuration!.CurrentValue.Limit.FunctionRecursionLimit)
		{
			limitExceeded.IsExceeded = true;
			callDepth.Decrement();
			recursionDepths[attributeLongName] = depth - 1;
			return new CallState(Errors.ErrorRecursion);
		}

		try
		{
			return await executeFunc();
		}
		finally
		{
			// Decrement tracking when done
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
	private static async ValueTask<OneOf<Success, Error<string>>> ValidateSemaphoreAttribute(
		AnySharpObject targetObject,
		string[] attributePath)
	{
		// SEMAPHORE attribute is always valid
		if (attributePath.Length == 1 && attributePath[0].Equals(DefaultSemaphoreAttribute, StringComparison.OrdinalIgnoreCase))
		{
			return new Success();
		}

		// Check if this is a built-in standard attribute
		var allStandardAttributes = Mediator!.CreateStream(new GetAllAttributeEntriesQuery());
		var isStandardAttribute = await allStandardAttributes
			.AnyAsync(stdAttr => stdAttr.Name.Equals(attributePath[0], StringComparison.OrdinalIgnoreCase));

		// Get the attribute if it exists
		var god = await HelperFunctions.GetGod(Mediator!);
		var attrResult = await AttributeService!.GetAttributeAsync(
			god, targetObject, string.Join("`", attributePath), IAttributeService.AttributeMode.Read, false);

		if (attrResult.IsNone)
		{
			// Attribute not set - check if it's a built-in
			if (isStandardAttribute)
			{
				return new Error<string>($"Cannot use built-in attribute '{attributePath[0]}' as semaphore.");
			}
			// Not set and not built-in - valid
			return new Success();
		}

		if (attrResult.IsError)
		{
			return new Error<string>(attrResult.AsError.Value);
		}

		// Attribute exists - validate owner, flags, and value
		var attribute = attrResult.AsAttribute.Last();

		// Check owner (must be God - DBRef 1)
		// Note: Owner is guaranteed to exist for attributes
		var owner = await attribute.Owner.WithCancellation(CancellationToken.None);
		if (owner!.Object.Key != 1)
		{
			return new Error<string>($"Semaphore attribute must be owned by God (#1). Current owner: #{owner.Object.Key}");
		}

		// Check value (must be numeric or empty)
		var value = attribute.Value.ToPlainText();
		if (!string.IsNullOrEmpty(value) && !int.TryParse(value, out _))
		{
			return new Error<string>($"Semaphore attribute must have a numeric or empty value. Current value: {value}");
		}

		// Check flags (must have no_inherit, no_clone, and locked)
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
	public static ValueTask<Option<CallState>> At(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ValueTask.FromResult(new Option<CallState>(CallState.Empty));

	[SharpCommand(Name = "THINK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["expression"])]
	public static async ValueTask<Option<CallState>> Think(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (parser.CurrentState.Arguments.Count <= 0)
		{
			return new None();
		}

		await NotifyService!.Notify(executor, parser.CurrentState.Arguments["0"].Message!.ToString(), executor);
		return parser.CurrentState.Arguments["0"];
	}

	[SharpCommand(Name = "HUH_COMMAND", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> HuhCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HuhTypeHelp));
		return new CallState("#-1 HUH");
	}

	[SharpCommand(Name = "@MAP", Behavior = CB.EqSplit | CB.RSNoParse, MinArgs = 1, MaxArgs = 2,
		Switches = ["CLEARREGS", "DELIMIT", "INLINE", "INPLACE", "LOCALIZE", "NOBREAK", "NOTIFY"], ParameterNames = ["object", "code"])]
	public static async ValueTask<Option<CallState>> Map(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (args.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapMustSpecifyAttribute));
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}

		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapMustSpecifyAttribute));
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}

		// Parse object/attribute path
		var pathSplit = HelperFunctions.SplitDbRefAndOptionalAttr(attributePath);
		if (!pathSplit.TryPickT0(out var pathDetails, out _))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapInvalidObjectAttributePath));
			return new CallState("#-1 INVALID PATH");
		}

		var (objSpec, attrName) = pathDetails;
		if (string.IsNullOrEmpty(attrName))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapMustSpecifyAttribute));
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}

		// Handle /DELIMIT switch using helper method
		var originalListText = args.Count >= 2 ? args["1"].Message! : MModule.empty();
		var (delimiter, listText) = ExtractFirstParameter(originalListText, switches.Contains("DELIMIT"));
		var list = MModule.split(delimiter, listText);

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapWouldIterateFormat), list.Length, objSpec, attrName);

		if (switches.Contains("INLINE"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapModeInline));
		}

		if (switches.Contains("NOTIFY"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapWillQueueNotify));
		}

		if (switches.Contains("CLEARREGS"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapWillClearRegisters));
		}

		if (switches.Contains("LOCALIZE"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapWillLocalizeRegisters));
		}

		// Locate the target object
		var targetObject = await LocateService!.LocateAndNotifyIfInvalid(
			parser, executor, executor, objSpec, LocateFlags.All);

		if (!targetObject.IsValid())
		{
			return new CallState("#-1 OBJECT NOT FOUND");
		}

		var target = targetObject.WithoutError().WithoutNone();

		// Get the attribute to execute
		var attributeResult = await AttributeService!.GetAttributeAsync(
			executor, target, attrName, IAttributeService.AttributeMode.Read, false);

		if (attributeResult.IsNone || attributeResult.IsError)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapAttributeNotFoundOnObjectFormat), attrName, target.Object().Name);
			return new CallState("#-1 NO SUCH ATTRIBUTE");
		}

		var attribute = attributeResult.AsAttribute.Last();
		var attributeText = attribute.Value.ToPlainText();

		if (string.IsNullOrWhiteSpace(attributeText))
		{
			// Empty attribute - nothing to execute
			return CallState.Empty;
		}

		var isInline = switches.Contains("INLINE");
		var results = new List<string>();

		if (isInline)
		{
			// Inline execution - execute immediately for each element
			foreach (var element in list)
			{
				// Set %0 to the current element
				var registerDict = new Dictionary<string, MString> { ["0"] = element! };
				var registerStack = new ConcurrentStack<Dictionary<string, MString>>();
				registerStack.Push(registerDict);

				var stateForElement = parser.CurrentState with
				{
					Registers = registerStack,
					Executor = target.Object().DBRef,
					Caller = parser.CurrentState.Executor
				};

				// Execute the attribute with the element as %0
				var result = await parser.With(state => stateForElement, async newParser =>
				{
					return await newParser.WithAttributeDebug(attribute,
						async p => await p.CommandListParse(attribute.Value));
				});

				if (result != null && result.Message != null)
				{
					results.Add(result.Message.ToPlainText() ?? string.Empty);
				}
			}

			// If /notify switch is present, queue "@notify" after inline execution
			if (switches.Contains("NOTIFY"))
			{
				await Mediator!.Send(new QueueCommandListRequest(
					MModule.single("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(executor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			return new CallState(string.Join(" ", results));
		}
		else
		{
			// Queued execution - queue each element's execution
			foreach (var element in list)
			{
				// Set %0 to the current element  
				var registerDict = new Dictionary<string, MString> { ["0"] = element! };
				var registerStack = new ConcurrentStack<Dictionary<string, MString>>();
				registerStack.Push(registerDict);

				var stateForElement = parser.CurrentState with
				{
					Registers = registerStack,
					Executor = target.Object().DBRef,
					Caller = parser.CurrentState.Executor
				};

				await Mediator!.Send(new QueueCommandListRequest(
					attribute.Value,
					stateForElement,
					new DbRefAttribute(target.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			// If /notify switch is present, queue "@notify" after all executions
			if (switches.Contains("NOTIFY"))
			{
				await Mediator!.Send(new QueueCommandListRequest(
					MModule.single("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(executor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			return CallState.Empty;
		}
	}

	[SharpCommand(Name = "@DOLIST", Behavior = CB.EqSplit | CB.RSNoParse, MinArgs = 1, MaxArgs = 2,
		Switches = ["CLEARREGS", "DELIMIT", "INLINE", "INPLACE", "LOCALIZE", "NOBREAK", "NOTIFY", "PID"], ParameterNames = ["list", "command"])]
	public static async ValueTask<Option<CallState>> DoList(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var switches = parser.CurrentState.Switches;

		if (parser.CurrentState.Arguments.Count < 2)
		{
			await NotifyService!.NotifyLocalized(enactor, nameof(ErrorMessages.Notifications.DoListWhatToDoWithList));
			return new None();
		}

		// Handle /DELIMIT or /PID switch - extract delimiter or PID from first argument
		var hasDelimit = switches.Contains("DELIMIT");
		var hasPid = switches.Contains("PID");

		string delimiter = " ";
		string? notifyPid = null;
		MString listText;

		if (hasDelimit)
		{
			// Format: @dolist/delimit <delimiter> <list>=<action>
			var (delimiterParam, extractedList) = ExtractFirstParameter(
				parser.CurrentState.Arguments["0"].Message!,
				true);
			delimiter = delimiterParam;
			listText = extractedList;
		}
		else if (hasPid)
		{
			// Format: @dolist/pid <pid> <list>=<action>
			var (pidParam, extractedList) = ExtractFirstParameter(
				parser.CurrentState.Arguments["0"].Message!,
				true);
			notifyPid = pidParam;
			listText = extractedList;
		}
		else
		{
			// Format: @dolist <list>=<action>
			listText = parser.CurrentState.Arguments["0"].Message!;
		}

		var list = MModule.split(delimiter, listText);
		var command = parser.CurrentState.Arguments["1"].Message!;

		// Replace ## with %iL in the command for PennMUSH backward compatibility
		var commandParts = MModule.split("##", command);
		if (commandParts.Length > 1)
		{
			command = MModule.multipleWithDelimiter(MModule.single("%iL"), commandParts);
		}

		var isInline = switches.Contains("INLINE") || switches.Contains("INPLACE");

		if (isInline)
		{
			var noBreak = switches.Contains("NOBREAK") || switches.Contains("INPLACE");
			var wrappedIteration = new IterationWrapper<MString>
			{ Value = MModule.empty(), Break = false, NoBreak = noBreak, Iteration = 0 };
			parser.CurrentState.IterationRegisters.Push(wrappedIteration);

			var lastCallState = CallState.Empty;
			var visitorFunc = parser.CommandListParseVisitor(command);
			foreach (var item in list)
			{
				wrappedIteration.Value = item!;
				wrappedIteration.Iteration++;

				// Note: Command is parsed once (line above loop), then the visitor is called
				// multiple times with different iteration register values. This is optimized.
				lastCallState = await visitorFunc();
			}

			parser.CurrentState.IterationRegisters.TryPop(out _);

			// If /notify or /pid switch is present with /inline, queue "@notify" after inline execution
			if (switches.Contains("NOTIFY"))
			{
				await Mediator!.Send(new QueueCommandListRequest(
					MModule.single("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}
			else if (hasPid && !string.IsNullOrEmpty(notifyPid))
			{
				await Mediator!.Send(new QueueCommandListRequest(
					MModule.single($"@notify {notifyPid}"),
					parser.CurrentState,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			return lastCallState!;
		}
		else
		{
			var iteration = 0u;
			var noBreak = switches.Contains("NOBREAK") || switches.Contains("INPLACE");

			foreach (var item in list)
			{
				iteration++;

				var iterationWrapper = new IterationWrapper<MString>
				{
					Value = item!,
					Break = false,
					NoBreak = noBreak,
					Iteration = iteration
				};

				var iterationStack = new ConcurrentStack<IterationWrapper<MString>>();
				iterationStack.Push(iterationWrapper);

				var stateForIteration = parser.CurrentState with
				{
					IterationRegisters = iterationStack
				};

				await Mediator!.Send(new QueueCommandListRequest(
					command,
					stateForIteration,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			// If /notify or /pid switch is present, queue "@notify" after all iterations
			if (switches.Contains("NOTIFY"))
			{
				await Mediator!.Send(new QueueCommandListRequest(
					MModule.single("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}
			else if (hasPid && !string.IsNullOrEmpty(notifyPid))
			{
				await Mediator!.Send(new QueueCommandListRequest(
					MModule.single($"@notify {notifyPid}"),
					parser.CurrentState,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			return CallState.Empty;
		}
	}

	[SharpCommand(Name = "LOOK", Switches = ["OUTSIDE", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> Look(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var forceOpaque = switches.Contains("OPAQUE");
		var lookOutside = switches.Contains("OUTSIDE");

		// Void detection: if player's location is invalid, rescue them before LOOK
		if (executor.IsPlayer)
		{
			var fallbackHome = new DBRef((int)Configuration!.CurrentValue.Database.PlayerStart);
			if (await MoveService!.RescueFromVoidAsync(executor, fallbackHome))
			{
				// Player was rescued - refresh executor to get updated location
				executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
			}
		}

		AnyOptionalSharpObject viewing = new None();

		if (lookOutside && executor.IsContent)
		{
			var container = await executor.AsContent.Location();
			viewing = container.WithRoomOption().Match<AnyOptionalSharpObject>(
				player => player,
				room => room,
				exit => exit,
				thing => thing
			);
		}
		else if (args.Count == 1)
		{
			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				args["0"].Message!.ToString(),
				LocateFlags.All);

			if (locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
		}
		else
		{
			viewing = (await Mediator!.Send(new GetCertainLocationQuery(executor.Id()!))).WithExitOption()
				.WithNoneOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var realViewing = viewing.Known;
		var viewingObject = realViewing.Object();

		var executorLocation = executor.IsContent
			? await executor.AsContent.Location()
			: null;
		var viewingFromInside = executorLocation != null
			&& executorLocation.Object().DBRef == viewingObject.DBRef;

		var baseName = viewingObject.Name;
		var baseDesc = MModule.empty();
		var useIdesc = viewingFromInside && !lookOutside;

		var descAttrName = useIdesc ? "IDESCRIBE" : "DESCRIBE";
		var descResult = await AttributeService!.GetAttributeAsync(executor, realViewing, descAttrName,
			IAttributeService.AttributeMode.Read, false);

		if (descResult.IsAttribute)
		{
			var descAttr = descResult.AsAttribute.Last();
			baseDesc = MModule.getLength(descAttr.Value) > 0
				? descAttr.Value
				: (useIdesc ? MModule.empty() : MModule.single("You see nothing special."));
		}
		else if (!useIdesc)
		{
			baseDesc = MModule.single("You see nothing special.");
		}

		var flags = await viewingObject.Flags.Value.ToArrayAsync();
		var defaultFormattedName = MModule.concat(
			baseName.Hilight(),
			MModule.single($"(#{viewingObject.DBRef.Number}{string.Join(string.Empty, flags.Select(x => x.Symbol))})"));

		var formattedName = defaultFormattedName;
		if (realViewing.IsRoom && viewingFromInside)
		{
			var nameFormatArgs = new Dictionary<string, CallState>
			{
				["0"] = new CallState(viewingObject.DBRef.ToString()),
				["1"] = new CallState(defaultFormattedName)
			};

			formattedName = await AttributeHelpers.EvaluateFormatAttribute(
				AttributeService, parser, executor, realViewing, "NAMEFORMAT",
				nameFormatArgs, defaultFormattedName, checkParents: false);
		}

		var formatAttrName = useIdesc ? "IDESCFORMAT" : "DESCFORMAT";
		var descFormatArgs = new Dictionary<string, CallState>
		{
			["0"] = new CallState(baseDesc)
		};

		var formattedDesc = await AttributeHelpers.EvaluateFormatAttribute(
			AttributeService, parser, executor, realViewing, formatAttrName,
			descFormatArgs, baseDesc, checkParents: false);

		await NotifyService!.Notify(executor, formattedName, executor);
		if (MModule.getLength(formattedDesc) > 0)
		{
			await NotifyService.Notify(executor, formattedDesc, executor);
		}

		var showInventory = realViewing.IsContainer
			&& !forceOpaque
			&& !(await realViewing.IsOpaque());

		if (showInventory)
		{
			var allContents = await Mediator!.CreateStream(new GetContentsQuery(realViewing.AsContainer))!.ToListAsync();

			var isRoomLight = realViewing.IsRoom && await realViewing.IsLight();
			var isRoomDark = realViewing.IsRoom && await realViewing.IsDarkLegal();
			var canSeeAll = await executor.IsSee_All();

			var visibleContents = new List<AnySharpContent>();
			var visibleExits = new List<AnySharpContent>();

			foreach (var item in allContents)
			{
				var itemObj = item.WithRoomOption();
				var isDark = await itemObj.IsDarkLegal();
				var isLight = await itemObj.IsLight();

				bool visible = false;
				if (isRoomLight)
				{
					visible = true;
				}
				else if (isRoomDark)
				{
					visible = canSeeAll || isLight;
				}
				else
				{
					visible = !isDark || canSeeAll;
				}

				if (visible)
				{
					if (item.IsExit)
					{
						if (!isDark || canSeeAll)
						{
							visibleExits.Add(item);
						}
					}
					else
					{
						visibleContents.Add(item);
					}
				}
			}

			if (visibleContents.Count > 0)
			{
				var contentDbrefs = string.Join(" ", visibleContents.Select(x => x.Object().DBRef.ToString()));
				var contentNames = string.Join("|", visibleContents.Select(x => x.Object().Name));
				var contentsLabel = realViewing.IsRoom ? "Contents:" : "Carrying:";

				// PennMUSH: wizards/see_all see Name(#dbrefFlags), mortals see plain Name
				var contentLines = await Task.WhenAll(visibleContents.Select(async item =>
				{
					if (canSeeAll)
					{
						return await MessageHelpers.FormatObjectWithDbref(item.Object());
					}
					return item.Object().Name;
				}));
				var defaultContents = MModule.single($"{contentsLabel}\n{string.Join("\n", contentLines)}");

				var conFormatArgs = new Dictionary<string, CallState>
				{
					["0"] = new CallState(contentDbrefs),
					["1"] = new CallState(contentNames)
				};

				var formattedContents = await AttributeHelpers.EvaluateFormatAttribute(
					AttributeService, parser, executor, realViewing, "CONFORMAT",
					conFormatArgs, defaultContents, checkParents: false);

				await NotifyService.Notify(executor, formattedContents, executor);
			}

			if (visibleExits.Count > 0 && realViewing.IsRoom)
			{
				var exitDbrefs = string.Join(" ", visibleExits.Select(x => x.Object().DBRef.ToString()));
				var exitFormatArgs = new Dictionary<string, CallState>
				{
					["0"] = new CallState(exitDbrefs)
				};

				// Build default exit display
				var isTransparent = await realViewing.IsTransparent();
				MString defaultExits;
				if (isTransparent)
				{
					var exitDisplays = new List<string>();
					foreach (var exit in visibleExits)
					{
						var exitObj = exit.WithRoomOption().Object();
						var destination = exit.IsExit ? await exit.AsExit.Home.WithCancellation(CancellationToken.None) : null;
						var destName = destination != null ? destination.Object().Name : "*UNLINKED*";

						if (await exit.WithRoomOption().IsOpaque())
						{
							exitDisplays.Add(exitObj.Name);
						}
						else
						{
							exitDisplays.Add($"{exitObj.Name} to {destName}");
						}
					}
					defaultExits = MModule.single(string.Join("\n", exitDisplays));
				}
				else
				{
					var names = visibleExits.Select(x => x.Object().Name).ToList();
					defaultExits = MModule.single($"Obvious exits:\n{MessageHelpers.FormatWithOxfordComma(names)}");
				}

				var formattedExits = await AttributeHelpers.EvaluateFormatAttribute(
					AttributeService, parser, executor, realViewing, "EXITFORMAT",
					exitFormatArgs, defaultExits, checkParents: false);

				// If a custom format was applied, send it as a single notification
				// If using default format in transparent rooms, send one notification per exit to match original behavior
				if (formattedExits == defaultExits && isTransparent)
				{
					// Original behavior: one notification per exit in transparent rooms
					foreach (var exit in visibleExits)
					{
						var exitObj = exit.WithRoomOption().Object();
						var destination = exit.IsExit ? await exit.AsExit.Home.WithCancellation(CancellationToken.None) : null;
						var destName = destination != null ? destination.Object().Name : "*UNLINKED*";

						if (await exit.WithRoomOption().IsOpaque())
						{
							await NotifyService.Notify(executor, exitObj.Name, executor);
						}
						else
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ExitNameToDestFormat), exitObj.Name, destName);
						}
					}
				}
				else
				{
					await NotifyService.Notify(executor, formattedExits, executor);
				}
			}
		}

		return new CallState(viewingObject.DBRef.ToString());
	}

	[SharpCommand(Name = "EXAMINE", Switches = ["BRIEF", "DEBUG", "MORTAL", "PARENT", "ALL", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> Examine(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		AnyOptionalSharpObject viewing;
		string? attributePattern = null;

		if (args.Count == 1)
		{
			var argText = args["0"].Message!.ToPlainText();
			var split = HelperFunctions.SplitDbRefAndOptionalAttr(argText);

			if (split.TryPickT0(out var details, out _))
			{
				var (objectName, maybeAttributePattern) = details;
				attributePattern = maybeAttributePattern;

				var locate = await LocateService!.LocateAndNotifyIfInvalid(
					parser,
					enactor,
					enactor,
					objectName,
					LocateFlags.All);

				if (locate.IsValid())
				{
					viewing = locate.WithoutError();
				}
				else
				{
					return new None();
				}
			}
			else
			{
				var locate = await LocateService!.LocateAndNotifyIfInvalid(
					parser,
					enactor,
					enactor,
					argText,
					LocateFlags.All);

				if (locate.IsValid())
				{
					viewing = locate.WithoutError();
				}
				else
				{
					return new None();
				}
			}
		}
		else
		{
			viewing = (await Mediator!.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var viewingKnown = viewing.Known();

		var canExamine = await PermissionService!.CanExamine(executor, viewingKnown);

		if (switches.Contains("MORTAL") && await executor.IsWizard())
		{
			canExamine = await PermissionService.Controls(executor, viewingKnown);
		}

		if (!canExamine)
		{
			var limitedObj = viewingKnown.Object();
			var limitedOwnerObj = (await limitedObj.Owner.WithCancellation(CancellationToken.None)).Object;
			await NotifyService!.Notify(enactor, MModule.multiple([
				limitedObj.Name.Hilight(),
				MModule.single(" is owned by "),
				limitedOwnerObj.Name.Hilight(),
				MModule.single(".")
			]), enactor);
			return new CallState(limitedObj.DBRef.ToString());
		}

		var contents = (switches.Contains("OPAQUE") || viewing.IsExit)
			? []
			: await Mediator!.CreateStream(new GetContentsQuery(viewingKnown.AsContainer))
				.ToArrayAsync();

		var obj = viewingKnown.Object()!;
		var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var description = (await AttributeService!.GetAttributeAsync(enactor, viewingKnown, "DESCRIBE",
				IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => MModule.getLength(attr.Last().Value) == 0
					? MModule.single("There is nothing to see here")
					: attr.Last().Value,
				none => MModule.single("There is nothing to see here"),
				error => MModule.empty());

		var objFlags = await obj.Flags.Value.ToArrayAsync();
		var ownerObjFlags = await ownerObj.Flags.Value.ToArrayAsync();
		var objParent = await obj.Parent.WithCancellation(CancellationToken.None);
		var objPowers = obj.Powers.Value;
		var objZone = await obj.Zone.WithCancellation(CancellationToken.None);

		var outputSections = new List<MString>();

		var showFlags = Configuration!.CurrentValue.Cosmetic.FlagsOnExamine;

		// Name row: Name(#flagSymbols) — no space before (
		var nameRow = MModule.concat(
			name.Hilight(),
			MModule.single(showFlags
				? $"(#{obj.DBRef.Number}{string.Join(string.Empty, objFlags.Select(x => x.Symbol))})"
				: $"(#{obj.DBRef.Number})"));
		outputSections.Add(nameRow);

		// Type / Flags row
		outputSections.Add(showFlags
			? MModule.single($"Type: {obj.Type} Flags: {string.Join(" ", objFlags.Select(x => x.Name))}")
			: MModule.single($"Type: {obj.Type}"));

		// Description — only in full examine, not brief
		if (!switches.Contains("BRIEF"))
		{
			outputSections.Add(description);
		}

		// Zone section (for owner row)
		MString zoneSection;
		if (objZone.IsNone)
		{
			zoneSection = MModule.single("  Zone: *NOTHING*");
		}
		else
		{
			var zoneObject = objZone.Known.Object();
			var zoneFlags = await zoneObject.Flags.Value.ToArrayAsync();
			zoneSection = MModule.multiple([
				MModule.single("  Zone: "),
				zoneObject.Name.Hilight(),
				MModule.single($"(#{zoneObject.DBRef.Number}{string.Join(string.Empty, zoneFlags.Select(x => x.Symbol))})")
			]);
		}

		// Owner row: "Owner: Name(#flags)  Zone: ..." — owner name hilighted as MString
		var ownerRow = MModule.multiple([
			MModule.single("Owner: "),
			ownerName.Hilight(),
			MModule.single(showFlags
				? $"(#{ownerObj.DBRef.Number}{string.Join(string.Empty, ownerObjFlags.Select(x => x.Symbol))})"
				: $"(#{ownerObj.DBRef.Number})"),
			zoneSection
		]);
		outputSections.Add(ownerRow);

		// Parent row: "Parent: Name(#flags)" or "Parent: *NOTHING*"
		var parentObject = objParent.Object();
		if (parentObject == null)
		{
			outputSections.Add(MModule.single("Parent: *NOTHING*"));
		}
		else
		{
			var parentFlags = await parentObject.Flags.Value.ToArrayAsync();
			outputSections.Add(MModule.multiple([
				MModule.single("Parent: "),
				parentObject.Name.Hilight(),
				MModule.single($"(#{parentObject.DBRef.Number}{string.Join(string.Empty, parentFlags.Select(x => x.Symbol))})")
			]));
		}

		// Locks: one line per lock — no "Locks:" header
		// Output format: "{LockName} Lock [#<dbrefNumber><flagChars>]: <lockExpression>"
		foreach (var lockKvp in obj.Locks)
		{
			var flagsStr = LockService!.FormatLockFlags(lockKvp.Value.Flags);
			outputSections.Add(MModule.single(
				$"{lockKvp.Key} Lock [#{obj.DBRef.Number}{flagsStr}]: {lockKvp.Value.LockString}"));
		}

		// Powers — always show even when empty
		var powersList = await objPowers.Select(x => x.Name).ToArrayAsync();
		outputSections.Add(MModule.single($"Powers: {string.Join(" ", powersList)}"));

		// Warnings checked — always show even when empty
		var warningsStr = obj.Warnings != WarningType.None
			? WarningTypeHelper.UnparseWarnings(obj.Warnings)
			: string.Empty;
		outputSections.Add(MModule.single($"Warnings checked: {warningsStr}"));

		if (switches.Contains("DEBUG") && await executor.IsWizard())
		{
			outputSections.Add(MModule.single($"Created: {obj.CreationTime} ({DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F})"));
		}
		else
		{
			outputSections.Add(MModule.single($"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):ddd MMM dd HH:mm:ss yyyy}"));
		}

		// Last modified — always shown in both examine and brief
		outputSections.Add(MModule.single($"Last modified: {DateTimeOffset.FromUnixTimeMilliseconds(obj.ModifiedTime):ddd MMM dd HH:mm:ss yyyy}"));

		// Quota — player objects only, shown in both examine and brief
		if (viewingKnown.IsPlayer)
		{
			outputSections.Add(MModule.single($"Quota: {viewingKnown.AsPlayer.Quota}"));
		}

		await NotifyService!.Notify(enactor, MModule.multipleWithDelimiter(MModule.single("\n"), outputSections), enactor);

		if (!switches.Contains("BRIEF"))
		{
			var checkParents = switches.Contains("PARENT");

			SharpAttributesOrError atrs;
			if (!string.IsNullOrEmpty(attributePattern))
			{
				var patternMode = IAttributeService.AttributePatternMode.Wildcard;

				atrs = await AttributeService.GetAttributePatternAsync(
					enactor,
					viewingKnown,
					attributePattern,
					checkParents,
					patternMode);
			}
			else
			{
				atrs = await AttributeService.GetVisibleAttributesAsync(enactor, viewingKnown);
			}

			if (atrs.IsAttribute)
			{
				var showPublicOnly = Configuration!.CurrentValue.Cosmetic.ExaminePublicAttributes;
				var showAll = switches.Contains("ALL");

				foreach (var attr in atrs.AsAttributes)
				{
					const string VeiledFlagName = "VEILED";
					if (!showAll && attr.Flags.Any(f => f.Name.Equals(VeiledFlagName, StringComparison.OrdinalIgnoreCase)))
					{
						continue;
					}

					var attrOwner = await attr.Owner.WithCancellation(CancellationToken.None);
					var attrFlagsStr = attr.Flags.Any() ? $"{string.Join("", attr.Flags.Select(f => f.Symbol))} " : "";

					if (!await PermissionService.CanViewAttribute(enactor, viewingKnown, attr))
					// || showPublicOnly && !showAll && !attr.IsVisual())
					{
						continue;
					}

					await NotifyService!.Notify(enactor,
						MModule.concat(
							MModule.single($"{attr.LongName} [{attrFlagsStr}#{attrOwner!.Object.DBRef.Number}]: ").Hilight(),
							attr.Value), enactor);
				}
			}

			// Contents — only if not OPAQUE
			if (!switches.Contains("OPAQUE") && contents.Length > 0)
			{
				var conFormatResult = await AttributeService!.GetAttributeAsync(executor, viewingKnown, "CONFORMAT",
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

					await NotifyService!.Notify(enactor, formattedContents, enactor);
				}
				else
				{
					// Default: "Carrying:" for non-rooms, "Contents:" for rooms; each item as Name(#flags)
					var contentsLabel = viewingKnown.IsRoom ? "Contents:" : "Carrying:";
					async ValueTask<MString> BuildContentLine(AnySharpContent content, CancellationToken _)
					{
						var cObj = content.Object();
						var cFlags = await cObj.Flags.Value.ToArrayAsync();
						return MModule.concat(
							cObj.Name.Hilight(),
							MModule.single($"(#{cObj.DBRef.Number}{string.Join(string.Empty, cFlags.Select(x => x.Symbol))})"));
					}
					var contentItems = await contents
						.ToAsyncEnumerable()
						.Select(BuildContentLine)
						.Prepend(MModule.single(contentsLabel))
						.ToListAsync();
					await NotifyService!.Notify(enactor,
						MModule.multipleWithDelimiter(MModule.single("\n"), contentItems), enactor);
				}
			}

			// Exits — only if not OPAQUE and object can contain exits (not itself an exit)
			if (!switches.Contains("OPAQUE") && !viewingKnown.IsExit)
			{
				var exits = await Mediator!.CreateStream(new GetExitsQuery(viewingKnown.AsContainer))
					.ToArrayAsync();

				if (exits.Length > 0)
				{
					async ValueTask<MString> BuildExitLine(SharpExit exit, CancellationToken _)
					{
						var eObj = exit.Object;
						var eFlags = await eObj.Flags.Value.ToArrayAsync();
						return MModule.concat(
							eObj.Name.Hilight(),
							MModule.single($"(#{eObj.DBRef.Number}{string.Join(string.Empty, eFlags.Select(x => x.Symbol))})"));
					}
					var exitLines = await exits
						.ToAsyncEnumerable()
						.Select(BuildExitLine)
						.Prepend(MModule.single("Exits:"))
						.ToListAsync();
					await NotifyService!.Notify(enactor,
						MModule.multipleWithDelimiter(MModule.single("\n"), exitLines), enactor);
				}
			}

			// Home and Location — non-room objects only
			if (!viewingKnown.IsRoom)
			{
				var homeContainer = await viewingKnown.MinusRoom().Home();
				var locationContainer = await viewingKnown.AsContent.Location();

				var homeObject = homeContainer.Object();
				var locationObject = locationContainer.Object();
				var homeFlags = await homeObject.Flags.Value.ToArrayAsync();
				var locationFlags = await locationObject.Flags.Value.ToArrayAsync();

				await NotifyService.Notify(enactor, MModule.multiple([
					MModule.single("Home: "),
					homeObject.Name.Hilight(),
					MModule.single($"(#{homeObject.DBRef.Number}{string.Join(string.Empty, homeFlags.Select(x => x.Symbol))})")
				]), enactor);
				await NotifyService.Notify(enactor, MModule.multiple([
					MModule.single("Location: "),
					locationObject.Name.Hilight(),
					MModule.single($"(#{locationObject.DBRef.Number}{string.Join(string.Empty, locationFlags.Select(x => x.Symbol))})")
				]), enactor);
			}
		}

		return new CallState(obj.DBRef.ToString());
	}

	[SharpCommand(Name = "@PEMIT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["target", "message"])]
	public static async ValueTask<Option<CallState>> PrivateEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			return new CallState(string.Empty);
		}

		var notification = args["1"].Message!.ToString();
		var targetListText = MModule.plainText(args["0"].Message!);
		var nameListTargets = ArgHelpers.NameList(targetListText);

		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		await CommunicationService!.SendToMultipleObjectsAsync(
			parser,
			executor,
			enactor,
			nameListTargets.ToAsyncEnumerable(),
			_ => notification,
			INotifyService.NotificationType.Announce,
			notifyOnPermissionFailure: true);

		return new None();
	}

	[SharpCommand(Name = "GOTO", Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["destination"])]
	public static async ValueTask<Option<CallState>> GoTo(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		if (parser.CurrentState.Arguments.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay));
			return CallState.Empty;
		}

		var exit = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			args["0"].Message!.ToString(),
			LocateFlags.ExitsInTheRoomOfLooker
			| LocateFlags.EnglishStyleMatching
			| LocateFlags.ExitsPreference
			| LocateFlags.OnlyMatchTypePreference
			| LocateFlags.FailIfNotPreferred);

		if (!exit.IsValid())
		{
			return CallState.Empty;
		}

		var exitObj = exit.AsExit;

		var homeLocation = await exitObj.Home.WithCancellation(CancellationToken.None);
		AnySharpContainer destination;

		if (homeLocation.Object().DBRef.Number == -1)
		{
			var destAttr = await AttributeService!.GetAttributeAsync(
				executor, exitObj, "DESTINATION", IAttributeService.AttributeMode.Read, false);

			if (destAttr.IsNone || destAttr.IsError)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ExitDestinationInvalid));
				return CallState.Empty;
			}

			var destValue = destAttr.AsAttribute.Last().Value.ToPlainText();
			var located = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				destValue!,
				LocateFlags.All);

			if (!located.IsValid())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ExitDestinationInvalid));
				return CallState.Empty;
			}

			var locatedObj = located.WithoutError().WithoutNone();
			if (!locatedObj.IsContainer)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ExitNoValidLocationDetail));
				return CallState.Empty;
			}

			destination = locatedObj.AsContainer;
		}
		else
		{
			destination = homeLocation;
		}

		if (!await PermissionService!.CanGoto(executor, exitObj, destination))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay));
			return CallState.Empty;
		}

		if (await MoveService!.WouldCreateLoop(executor.AsContent, destination))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWayContainmentLoop));
			return CallState.Empty;
		}

		await Mediator!.Send(new MoveObjectCommand(executor.AsContent, destination));

		return new CallState(destination.ToString());
	}


	[SharpCommand(Name = "@TELEPORT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2,
		Switches = ["LIST", "INSIDE", "QUIET"], ParameterNames = ["object", "destination"])]
	public static async ValueTask<Option<CallState>> Teleport(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var destinationString = MModule.plainText(args.Count == 1 ? args["0"].Message : args["1"].Message);
		var toTeleport = MModule.plainText(args.Count == 1 ? MModule.single(executor.Object().DBRef.ToString()) : args["0"].Message);

		var isList = parser.CurrentState.Switches.Contains("LIST");

		IEnumerable<OneOf<DBRef, string>> toTeleportList;
		if (isList)
		{
			toTeleportList = ArgHelpers.NameList(toTeleport);
		}
		else
		{
			var isDbRef = DBRef.TryParse(toTeleport, out var objToTeleport);
			toTeleportList = [isDbRef ? objToTeleport!.Value : toTeleport];
		}

		var toTeleportStringList = toTeleportList.Select(x => x.Match(
			dbref => dbref.ToString(),
			str => str));

		var destination = await LocateService!.LocateAndNotifyIfInvalid(parser,
			executor,
			executor,
			destinationString,
			LocateFlags.All);

		if (!destination.IsValid())
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay));
			return CallState.Empty;
		}

		var validDestination = destination.WithoutError().WithoutNone();

		AnySharpContainer destinationContainer;
		if (validDestination.IsExit)
		{
			// Teleporting through an exit - resolve the exit's destination using the same logic as @goto
			var exitObj = validDestination.AsExit;
			var homeLocation = await exitObj.Home.WithCancellation(CancellationToken.None);

			if (homeLocation.Object().DBRef.Number == -1)
			{
				// Exit is unlinked - check for DESTINATION attribute
				var destAttr = await AttributeService!.GetAttributeAsync(
					executor, exitObj, "DESTINATION", IAttributeService.AttributeMode.Read, false);

				const string exitUnlinkedMsg = "That exit doesn't go anywhere.";

				if (destAttr.IsNone || destAttr.IsError)
				{
					await NotifyService!.Notify(executor, exitUnlinkedMsg, executor);
					return CallState.Empty;
				}

				var destValue = destAttr.AsAttribute.Last().Value.ToPlainText();
				if (string.IsNullOrWhiteSpace(destValue))
				{
					await NotifyService!.Notify(executor, exitUnlinkedMsg, executor);
					return CallState.Empty;
				}

				var located = await LocateService!.LocateAndNotifyIfInvalid(
					parser,
					executor,
					executor,
					destValue,
					LocateFlags.All);

				if (!located.IsValid())
				{
					await NotifyService!.Notify(executor, exitUnlinkedMsg, executor);
					return CallState.Empty;
				}

				var locatedObj = located.WithoutError().WithoutNone();
				if (!locatedObj.IsContainer)
				{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ExitDestinationInvalid));
					return CallState.Empty;
				}

				destinationContainer = locatedObj.AsContainer;
			}
			else
			{
				destinationContainer = homeLocation;
			}
		}
		else
		{
			destinationContainer = validDestination.AsContainer;
		}

		foreach (var obj in toTeleportStringList)
		{
			var locateTarget = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, obj,
				LocateFlags.All);
			if (!locateTarget.IsValid() || locateTarget.IsRoom)
			{
				// Rooms cannot be teleported (PennMUSH src/wiz.c).
				if (locateTarget.IsRoom)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantTeleportRooms));
				}
				else
				{
					await NotifyService!.Notify(executor, Errors.ErrorNotVisible, executor);
				}
				continue;
			}

			var target = locateTarget.WithoutError().WithoutNone();
			var targetContent = target.AsContent;
			if (!await PermissionService!.Controls(executor, target))
			{
				await NotifyService!.Notify(executor, Errors.ErrorCannotTeleport, executor);
				continue;
			}

			// Zone teleport restriction: check if the source room blocks teleporting out.
			// PennMUSH src/wiz.c: NO_TEL flag prevents all non-wizard teleports from the room.
			// Zone mismatch with Zone lock failure prevents teleporting out of the zone.
			if (!await executor.IsWizard())
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
					// If we can't resolve location, skip zone checks
				}

				if (sourceLocation is not null)
				{
					var sourceObj = sourceLocation.WithExitOption();

					// NO_TEL flag on source room blocks teleport
					if (await sourceObj.HasFlag("NO_TEL"))
					{
						await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TeleportsNotAllowed));
						continue;
					}

					// Zone mismatch check: if source room has a zone that differs from destination's zone,
					// evaluate the Zone lock on the source room. Failure blocks teleport.
					var sourceZone = await sourceObj.Object().Zone.WithCancellation(CancellationToken.None);
					if (!sourceZone.IsNone)
					{
						var destObj = destinationContainer.WithExitOption();
						var destZone = await destObj.Object().Zone.WithCancellation(CancellationToken.None);
						var sourceZoneDbRef = sourceZone.Known.Object().DBRef;
						var destZoneDbRef = destZone.IsNone ? new DBRef(-1) : destZone.Known.Object().DBRef;

						if (!sourceZoneDbRef.Equals(destZoneDbRef))
						{
							if (!LockService!.Evaluate(LockType.Zone, sourceObj, executor))
							{
								await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoZoneTeleport));
								continue;
							}
						}
					}
				}
			}

			// Check TPort lock on the destination (PennMUSH src/wiz.c).
			// Wizards bypass the TPort lock check.
			if (!await executor.IsWizard())
			{
				var destObj = destinationContainer.WithExitOption();
				if (!LockService!.Evaluate(LockType.TPort, destObj, executor))
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TeleportsNotAllowed));
					continue;
				}
			}

			// Execute the move using the MoveService which handles:
			// - Containment loop checking
			// - Permission validation
			// - Enter/Leave/Teleport hook triggering
			// - Notifications
			var isSilent = parser.CurrentState.Switches.Contains("QUIET");
			var moveResult = await MoveService!.ExecuteMoveAsync(
				parser,
				targetContent,
				destinationContainer,
				executor.Object().DBRef,
				"teleport",
				isSilent);

			if (moveResult.IsT1)
			{
				await NotifyService!.Notify(executor, moveResult.AsT1.Value, executor);
				continue;
			}

			// If the target is a player and not silent, notify them of the teleport
			if (target.IsPlayer && !isSilent)
			{
				// Notify the target player that they were teleported
				await NotifyService!.NotifyLocalized(target.Object().DBRef, nameof(ErrorMessages.Notifications.TeleportedPlayerNotified));

				// Show the target player their new location by executing LOOK as them
				var targetPlayerState = parser.CurrentState with
				{
					Executor = target.Object().DBRef,
					Enactor = target.Object().DBRef
				};

				await Mediator!.Send(new QueueCommandListRequest(
					MModule.single("look"),
					targetPlayerState,
					new DbRefAttribute(target.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}
		}

		return new CallState(destination.ToString());
	}

	[SharpCommand(Name = "@FIND", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 0, MaxArgs = 3, ParameterNames = ["name", "flags"])]
	public static async ValueTask<Option<CallState>> Find(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
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

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindSearchingFormat), searchName != null ? string.Format(ErrorMessages.Notifications.FindSearchMatchingFormat, searchName) : "");

		// Query database for objects matching the criteria
		var filter = new ObjectSearchFilter
		{
			NamePattern = searchName,
			MinDbRef = beginDbref,
			MaxDbRef = endDbref
		};

		var results = await Mediator!.CreateStream(new GetFilteredObjectsQuery(filter)).ToListAsync();

		// Filter to only objects the executor controls
		var controlledResults = await results.ToAsyncEnumerable()
			.Where(async (obj, ct) =>
			{
				var objNode = await Mediator.Send(new GetObjectNodeQuery(obj.DBRef), ct);
				return !objNode.IsNone() && await PermissionService!.Controls(executor, objNode.WithoutNone());
			})
			.ToListAsync();

		matchCount = controlledResults.Count;

		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindRangeFormat), beginDbref ?? 0, endDbref?.ToString() ?? "end");
		}

		// Display results
		foreach (var obj in controlledResults)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindObjectResultFormat), obj.Key, obj.Name);
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindFoundMatchingFormat), matchCount);

		return new CallState(matchCount.ToString());
	}

	[SharpCommand(Name = "@HALT", Switches = ["ALL", "NOEVAL", "PID"], Behavior = CB.Default | CB.EqSplit | CB.RSBrace,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["object"])]
	public static async ValueTask<Option<CallState>> Halt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @halt[/noeval] <object>[=<action list>] 
		// @halt/pid <pid>
		// @halt/all or @allhalt

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();

		// @halt/all - halt all objects in the game (wizard only)
		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			// Halt all objects in the game
			await foreach (var obj in Mediator!.CreateStream(new GetAllObjectsQuery()))
			{
				await Mediator.Send(new HaltObjectQueueRequest(obj.DBRef));
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllObjectsHalted));
			return CallState.Empty;
		}

		// @halt/pid - halt specific queue entry
		if (switches.Contains("PID"))
		{
			var pidStr = args.GetValueOrDefault("0")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(pidStr))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltMustSpecifyPid));
				return new CallState("#-1 NO PID SPECIFIED");
			}

			if (!long.TryParse(pidStr, out var pid))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltInvalidPidFormat));
				return new CallState("#-1 INVALID PID");
			}

			// Halt the specific task by PID
			var halted = await Mediator!.Send(new HaltByPidRequest(pid));
			if (halted)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltTaskHaltedFormat), pid);
			}
			else
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltNoTaskWithPidFormat), pid);
				return new CallState("#-1 NOT FOUND");
			}

			return CallState.Empty;
		}

		// @halt with no arguments - clear executor's queue without setting HALT flag
		if (args.Count == 0)
		{
			await Mediator!.Send(new HaltObjectQueueRequest(executor.Object().DBRef));
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Halted));
			return CallState.Empty;
		}

		// @halt <object>[=<actions>]
		var targetName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(targetName))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltMustSpecifyTarget));
			return new CallState("#-1 NO TARGET SPECIFIED");
		}

		var maybeTarget = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (!maybeTarget.IsValid())
		{
			return new CallState("#-1 NOT FOUND");
		}

		var target = maybeTarget.WithoutError().WithoutNone();

		var hasHaltPower = await executor.HasPower("HALT");
		var canHalt = await PermissionService!.Controls(executor, target) ||
									await executor.IsWizard() || hasHaltPower;

		if (!canHalt)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
			return new CallState(Errors.ErrorPerm);
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
			await Mediator!.Send(new HaltObjectQueueRequest(targetObject.DBRef));

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
				await Mediator!.Send(new QueueCommandListRequest(
					replacementActions!,
					parser.CurrentState,
					new DbRefAttribute(targetObject.DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltedPlayerAndObjectsFormat), targetObject.Name);
		}
		else
		{
			await Mediator!.Send(new HaltObjectQueueRequest(targetObject.DBRef));

			if (hasReplacementActions)
			{
				await Mediator!.Send(new QueueCommandListRequest(
					replacementActions!,
					parser.CurrentState,
					new DbRefAttribute(targetObject.DBRef, DefaultSemaphoreAttributeArray),
					-1));
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltedObjectWithActionsFormat), targetObject.Name);
			}
			else
			{
				var haltFlag = await Mediator!.Send(new GetObjectFlagQuery("HALT"));
				if (haltFlag != null)
				{
					await Mediator.Send(new SetObjectFlagCommand(target, haltFlag));
				}
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltedObjectFormat), targetObject.Name);
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NOTIFY", Switches = ["ALL", "ANY", "SETQ", "QUIET"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
		MinArgs = 1, MaxArgs = 0, ParameterNames = ["target", "message"])]
	public static async ValueTask<Option<CallState>> Notify(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches.Except(["QUIET"]).ToArray();
		var notifyType = "ANY";
		var args = parser.CurrentState.Arguments;

		if ((parser.CurrentState.Arguments.Count == 0) || string.IsNullOrEmpty(args["0"].Message?.ToPlainText()))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyMustSpecifySemaphoreObject));
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
				// No switches - default to ANY
				break;
			default:
				return new CallState(Errors.ErrorTooManySwitches);
		}

		var objectAndAttribute = HelperFunctions.SplitDbRefAndOptionalAttr(args["0"].Message!.ToPlainText());
		if (objectAndAttribute.IsT1 && objectAndAttribute.AsT1 == false)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyMustSpecifyValidObjectAttribute));
			return new None();
		}

		var (db, maybeAttributeString) = objectAndAttribute.AsT0;
		var maybeObject = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
			db, LocateFlags.All);

		if (maybeObject.IsError) return maybeObject.AsError;
		var objectToNotify = maybeObject.AsSharpObject;

		var attribute = string.IsNullOrEmpty(maybeAttributeString) ? DefaultSemaphoreAttribute : maybeAttributeString;

		var attributeContents = await AttributeService!.GetAttributeAsync(executor, objectToNotify, attribute,
			IAttributeService.AttributeMode.Execute, false);

		if (attributeContents.IsError)
		{
			return new CallState(attributeContents.AsError.Value);
		}

		int oldSemaphoreCount = 0;
		if (attributeContents.IsAttribute &&
				int.TryParse(attributeContents.AsAttribute.Last().Value.ToPlainText(), out var semaphoreCount))
		{
			oldSemaphoreCount = semaphoreCount;
		}

		// Check for =<number> parameter or =<qreg>,<value> pairs for /setq
		int notifyCount = 1;
		Dictionary<string, MString>? qRegisters = null;

		if (notifyType == "SETQ")
		{
			// With CB.RSArgs, each comma-separated value becomes a separate argument
			// So @notify/setq obj=0,val1,1,val2 becomes: args[0]=obj, args[1]=0, args[2]=val1, args[3]=1, args[4]=val2
			if (args.Count < 3) // Need at least object, qreg, and value
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyMustSpecifyQregAssignments));
				return new CallState("#-1 MISSING QREG ASSIGNMENTS");
			}

			var qregArgCount = args.Count - 1; // Subtract 1 for arg[0] which is the object
			if (qregArgCount % 2 != 0)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyQregAssignmentsMustBePairs));
				return new CallState("#-1 INVALID QREG PAIRS");
			}

			qRegisters = new Dictionary<string, MString>();
			for (var i = 1; i < args.Count; i += 2)
			{
				var qregName = args[i.ToString()].Message!.ToPlainText().Trim();
				var qregValue = args[(i + 1).ToString()].Message!.ToPlainText();
				qRegisters[qregName] = MModule.single(qregValue);
			}
		}
		else if (args.Count > 1 && args.TryGetValue("1", out var arg1))
		{
			var countArg = arg1.Message?.ToPlainText();
			if (!string.IsNullOrEmpty(countArg) &&
					(!int.TryParse(countArg, out notifyCount) || notifyCount < 1))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyInvalidNumber));
				return new CallState("#-1 INVALID NUMBER");
			}
		}

		var dbRefAttribute = new DbRefAttribute(objectToNotify.Object().DBRef, attribute.Split("`"));

		switch (notifyType)
		{
			case "ANY":
				// Notify specified number of tasks (default 1)
				await Mediator!.Send(new NotifySemaphoreRequest(dbRefAttribute, oldSemaphoreCount, notifyCount));
				var newCount = oldSemaphoreCount - notifyCount;
				await AttributeService!.SetAttributeAsync(executor, objectToNotify, attribute,
					MModule.single(newCount.ToString()));
				break;
			case "ALL":
				await Mediator!.Send(new NotifyAllSemaphoreRequest(dbRefAttribute));
				await AttributeService!.SetAttributeAsync(executor, objectToNotify, attribute,
					MModule.single(0.ToString()));
				break;
			case "SETQ":
				// Modify Q-registers of the first waiting task, then trigger it
				var modified = await Mediator!.Send(new ModifyQRegistersRequest(dbRefAttribute, qRegisters!));
				if (!modified)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyNoTaskWaitingOnSemaphore));
					return new CallState("#-1 NO WAITING TASK");
				}
				// After modifying Q-registers, trigger the task execution (same as regular @notify but only 1 task)
				await Mediator!.Send(new NotifySemaphoreRequest(dbRefAttribute, oldSemaphoreCount, 1));
				var newCountSetQ = oldSemaphoreCount - 1;
				await AttributeService!.SetAttributeAsync(executor, objectToNotify, attribute,
					MModule.single(newCountSetQ.ToString()));
				// Don't show "Notified." for /setq (different from regular @notify)
				return new None();
		}

		if (!parser.CurrentState.Switches.Contains("QUIET"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Notified));
		}

		return new None();
	}

	[SharpCommand(Name = "@NSPROMPT", Switches = ["SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["target", "message"])]
	public static async ValueTask<Option<CallState>> NoSpoofPrompt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var target = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var maybeFound =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, target,
				LocateFlags.All);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError;
		}

		var found = maybeFound.AsSharpObject;

		if (!await PermissionService!.CanInteract(executor, found, InteractType.Hear))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectDoesNotWantToHearFromYouFormat), found.Object().Name);
			return CallState.Empty;
		}

		await NotifyService!.Prompt(found, message, executor, INotifyService.NotificationType.NSEmit);

		// SILENT: Don't notify the executor
		if (!switches.Contains("SILENT"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouPromptedFormat), found.Object().Name);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "@SCAN", Switches = ["ROOM", "SELF", "ZONE", "GLOBALS"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["object", "code"])]
	public static async ValueTask<Option<CallState>> Scan(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches.Any()
			? parser.CurrentState.Switches.ToArray()
			: ["ROOM", "SELF", "ZONE", "GLOBALS"];

		List<string> runningOutput = [];

		// Helper to check if executor can scan an object
		// Can scan if executor controls the object OR object has VISUAL flag
		async Task<bool> CanScan(AnySharpObject obj)
		{
			var controls = await PermissionService!.Controls(executor, obj);
			if (controls) return true;

			var isVisual = await obj.HasFlag("VISUAL");
			return isVisual;
		}

		if (executor.IsContent && switches.Contains("ROOM"))
		{
			var where = await executor.AsContent.Location();
			var whereContent = where.Content(Mediator!);

			// notify: Matches on contents of this room:
			var matchedContent =
				await CommandDiscoveryService!.MatchUserDefinedCommand(parser,
					whereContent.Select(x => x.WithRoomOption()),
					arg0);

			if (matchedContent.IsSome())
			{
				foreach (var (i, (obj, attr, _)) in matchedContent.AsValue().Index())
				{
					// Check permission before showing
					if (await CanScan(obj))
					{
						runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
						await NotifyService!.Notify(executor,
							$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]", executor);
					}
				}
			}
		}

		if (executor.IsContainer && switches.Contains("SELF"))
		{
			var executorContents = executor.AsContainer.Content(Mediator!);

			// notify: Matches on carried objects:
			var matchedContent =
				await CommandDiscoveryService!.MatchUserDefinedCommand(parser,
					executorContents.Select(x => x.WithRoomOption()),
					arg0);

			if (matchedContent.IsSome())
			{
				foreach (var (i, (obj, attr, _)) in matchedContent.AsValue().Index())
				{
					// Check permission before showing
					if (await CanScan(obj))
					{
						runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
						await NotifyService!.Notify(executor,
							$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]", executor);
					}
				}
			}
		}

		if (switches.Contains("ZONE"))
		{
			// Get the zone of the executor's location
			if (executor.IsContent)
			{
				var location = await executor.AsContent.Location();
				var locationZone = await location.Object().Zone.WithCancellation(CancellationToken.None);

				if (!locationZone.IsNone)
				{
					var zoneObject = locationZone.Known;

					// Get contents of the zone master object
					var zoneContents = Mediator!.CreateStream(new GetContentsQuery(zoneObject.Object().DBRef))
						?? AsyncEnumerable.Empty<AnySharpContent>();

					// Match user-defined commands in zone contents
					var zoneMatched =
						await CommandDiscoveryService!.MatchUserDefinedCommand(parser,
							zoneContents.Select(x => x.WithRoomOption()),
							arg0);

					if (zoneMatched.IsSome())
					{
						foreach (var (i, (obj, attr, _)) in zoneMatched.AsValue().Index())
						{
							// Check permission before showing
							if (await CanScan(obj))
							{
								runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
								await NotifyService!.Notify(executor,
									$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]", executor);
							}
						}
					}
				}
			}
		}

		if (switches.Contains("GLOBAL"))
		{
			var masterRoom = new DBRef(Convert.ToInt32(Configuration!.CurrentValue.Database.MasterRoom));
			var masterRoomContents = Mediator!.CreateStream(new GetContentsQuery(masterRoom))
															 ?? AsyncEnumerable.Empty<AnySharpContent>();

			var masterRoomContent =
				await CommandDiscoveryService!.MatchUserDefinedCommand(parser,
					masterRoomContents.Select(x => x.WithRoomOption()),
					arg0);

			foreach (var (i, (obj, attr, _)) in masterRoomContent.AsValue().Index())
			{
				// Check permission before showing
				if (await CanScan(obj))
				{
					runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
					await NotifyService!.Notify(executor,
						$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]", executor);
				}
			}
		}

		return new CallState(string.Join(" ", runningOutput));
	}

	[SharpCommand(Name = "@SWITCH",
		Switches = ["NOTIFY", "FIRST", "ALL", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 3, MaxArgs = int.MaxValue, ParameterNames = ["expression", "cases..."])]
	public static async ValueTask<Option<CallState>> Switch(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		//  @switch[/<switch>] <string>=<expr1>, <action1> [,<exprN>, <actionN>]... [,<default>]
		//  @switch/all runs <action>s for all matching <expr>s. Default for @switch.
		//  @switch/first runs <action> for the first matching <expr> only. Same as @select, and often the desired behaviour.
		//	@switch/notify queues "@notify me" after the last <action>. 
		//	@switch/inline runs all actions in place, instead of creating a new queue entry for them.
		//	@switch/regexp makes <expr>s regular expressions, not wildcard/glob patterns.

		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches.ToArray();
		var strArg = args["0"];
		var testString = strArg.Message?.ToPlainText() ?? string.Empty;
		Option<MString> defaultArg = new None();
		var matched = false;

		// Separate out the default action (last element when total arg count is even).
		// args["0"] is the test expression; remaining args are (pattern, action) pairs plus optional default.
		// Even total args means: test + pairs + default → odd remaining → default is last.
		var remainingArgs = args.Values.Skip(1).ToList();
		if (args.Count % 2 == 0)
		{
			// Even count: test + N*(pat,act) + default → take default, leave pairs
			defaultArg = remainingArgs.Last().Message!;
			remainingArgs = remainingArgs.Take(remainingArgs.Count - 1).ToList();
		}

		var isFirst = switches.Contains("FIRST") && !switches.Contains("ALL");
		var isRegexp = switches.Contains("REGEXP");

		// Implement /LOCALIZE: save Q-registers so matched actions cannot permanently change
		// the caller's Q-registers. /CLEARREGS: start each action with empty Q-registers.
		// NOTE: Save must happen before Clear. new Dictionary<> creates an independent copy,
		// so the subsequent Clear() of the original does not affect savedRegisters.
		var hasLocalize = switches.Contains("LOCALIZE");
		var hasClearRegs = switches.Contains("CLEARREGS");

		Dictionary<string, MString>? savedRegisters = null;
		if ((hasLocalize || hasClearRegs) && parser.CurrentState.Registers.TryPeek(out var switchTopRegs))
		{
			if (hasLocalize)
			{
				savedRegisters = new Dictionary<string, MString>(switchTopRegs);
			}

			if (hasClearRegs)
			{
				switchTopRegs.Clear();
			}
		}

		// Push the switch string onto the context stack
		parser.CurrentState.SwitchStack.Push(strArg.Message!);

		try
		{
			// Iterate over non-overlapping (pattern, action) pairs — step by 2.
			for (var i = 0; i + 1 < remainingArgs.Count; i += 2)
			{
				var exprArg = remainingArgs[i];
				var actionArg = remainingArgs[i + 1];

				if (exprArg is null) break;

				// Patterns are RSNoParse (stored raw); evaluate lazily before comparing.
				// This matches PennMUSH behavior where pattern expressions like [func()] are
				// evaluated at match time, not pre-evaluated.
				var evaluatedPattern = (await exprArg.ParsedMessage()) ?? exprArg.Message!;
				var patternText = evaluatedPattern.ToPlainText();

				bool patternMatched;
				if (isRegexp)
				{
					try
					{
						patternMatched = Regex.IsMatch(testString, patternText, RegexOptions.IgnoreCase);
					}
					catch (ArgumentException ex)
					{
						await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SwitchInvalidRegexpFormat), patternText, ex.Message);
						continue;
					}
				}
				else
				{
					patternMatched = MModule.isWildcardMatch(strArg.Message!, evaluatedPattern);
				}

				if (patternMatched)
				{
					matched = true;
					// Substitute #$ with the test string in the action, matching PennMUSH behavior.
					var actionText = actionArg.Message!.ToPlainText().Replace("#$", testString);
					await parser.CommandListParseVisitor(MModule.single(actionText))();

					// /FIRST (or no /ALL): stop after the first matching action.
					if (isFirst) break;
				}
			}

			if (defaultArg.IsSome() && !matched)
			{
				var defaultText = defaultArg.AsValue().ToPlainText().Replace("#$", testString);
				await parser.CommandListParseVisitor(MModule.single(defaultText))();
			}

			// /NOTIFY: queue "@notify me" after all actions have been queued/run.
			if (switches.Contains("NOTIFY"))
			{
				await Mediator!.Send(new QueueCommandListRequest(
					MModule.single("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(executor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			return new CallState(matched);
		}
		finally
		{
			// Pop the switch string from the context stack
			parser.CurrentState.SwitchStack.TryPop(out _);

			// Restore Q-registers if /localize was set
			if (hasLocalize && savedRegisters != null && parser.CurrentState.Registers.TryPeek(out var regsToRestore))
			{
				regsToRestore.Clear();
				foreach (var (key, value) in savedRegisters)
				{
					regsToRestore[key] = value;
				}
			}
		}
	}

	[SharpCommand(Name = "@WAIT", Switches = ["PID", "UNTIL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 1, MaxArgs = 2, ParameterNames = ["seconds", "command"])]
	public static async ValueTask<Option<CallState>> Wait(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message!.ToPlainText()!;
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

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

		/*
		 *  @wait/pid <pid>=<seconds>
		 *	@wait/pid <pid>=[+-]<adjustment>
		 *	@wait/pid/until <pid>=<time>
		 */
		if (switches.Contains("PID"))
		{
			return await AtWaitForPid(parser, arg0, executor, arg1?.ToPlainText(), switches);
		}

		if (arg1 is null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitCommandListMissing));
			return new CallState("#-1 MISSING COMMAND LIST ARGUMENT");
		}

		//  @wait[/until] <time>=<command_list>
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

			await Mediator!.Send(new QueueDelayedCommandListRequest(arg1, callbackState, convertedTime));
			return CallState.Empty;
		}

		var splitBySlashes = arg0.Split('/');

		// @wait <object>=<command_list>
		if (splitBySlashes.Length == 1)
		{
			var maybeObject = await
				LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0, LocateFlags.All);

			if (maybeObject.IsError)
			{
				return maybeObject.AsError;
			}

			var located = maybeObject.AsSharpObject;
			if (!await PermissionService!.Controls(executor, located))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			await QueueSemaphore(parser, located, DefaultSemaphoreAttributeArray, arg1, callbackState);
			return CallState.Empty;
		}

		var maybeLocate =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, splitBySlashes[0],
				LocateFlags.All);
		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var foundObject = maybeLocate.AsSharpObject;
		var untilTime = 0.0d;

		switch (splitBySlashes.Length)
		{
			// @wait[/until] <object>/<time>=<command_list>
			// @wait <object>/<attribute>=<command_list>
			case 2 when switches.Contains("UNTIL"):
				{
					if (!double.TryParse(splitBySlashes[1], out untilTime))
					{
						await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeArgumentFormat));
						return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "TIME ARGUMENT"));
					}

					var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;

					await QueueSemaphoreWithDelay(parser, foundObject, DefaultSemaphoreAttributeArray, newUntilTime, arg1, callbackState);
					return CallState.Empty;
				}

			case 2 when double.TryParse(splitBySlashes[1], out untilTime):
				await QueueSemaphoreWithDelay(parser, foundObject, DefaultSemaphoreAttributeArray, TimeSpan.FromSeconds(untilTime), arg1, callbackState);
				return CallState.Empty;

			// @wait <object>/<attribute>=<command list>
			// Validate semaphore attribute follows MUSH rules
			case 2:
				{
					var customSemaphoreAttr = splitBySlashes[1].Split('`');
					var validation = await ValidateSemaphoreAttribute(foundObject, customSemaphoreAttr);

					if (validation.IsT1)
					{
						await NotifyService!.Notify(executor, validation.AsT1.Value, executor);
						return new CallState("#-1 INVALID SEMAPHORE ATTRIBUTE");
					}

					await QueueSemaphore(parser, foundObject, customSemaphoreAttr, arg1, callbackState);
					return CallState.Empty;
				}

			// @wait[/until] <object>/<attribute>/<time>=<command list>
			case 3 when !double.TryParse(splitBySlashes[2], out untilTime):
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeArgumentFormat));
				return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "TIME ARGUMENT"));

			// Note: Attribute value validation for semaphore usage is handled in QueueSemaphore/QueueSemaphoreWithDelay
			// methods. If the attribute value is not a valid integer, an error is returned.
			case 3 when switches.Contains("UNTIL"):
				{
					var customSemaphoreAttr = splitBySlashes[1].Split('`');
					var validation = await ValidateSemaphoreAttribute(foundObject, customSemaphoreAttr);

					if (validation.IsT1)
					{
						await NotifyService!.Notify(executor, validation.AsT1.Value, executor);
						return new CallState("#-1 INVALID SEMAPHORE ATTRIBUTE");
					}

					var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;
					await QueueSemaphoreWithDelay(parser, foundObject, customSemaphoreAttr, newUntilTime, arg1, callbackState);
					return CallState.Empty;
				}

			case 3:
				{
					var customSemaphoreAttr = splitBySlashes[1].Split('`');
					var validation = await ValidateSemaphoreAttribute(foundObject, customSemaphoreAttr);

					if (validation.IsT1)
					{
						await NotifyService!.Notify(executor, validation.AsT1.Value, executor);
						return new CallState("#-1 INVALID SEMAPHORE ATTRIBUTE");
					}

					await QueueSemaphoreWithDelay(parser, foundObject, customSemaphoreAttr,
						TimeSpan.FromSeconds(untilTime), arg1, callbackState);
					return CallState.Empty;
				}

			default:
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidFirstArgumentFormat));
				return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "FIRST ARGUMENT"));
		}
	}

	private static async ValueTask QueueSemaphore(IMUSHCodeParser parser, AnySharpObject located, string[] attribute,
		MString arg1, ParserState? callbackState = null)
	{
		var stateForCallback = callbackState ?? parser.CurrentState;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var one = await Mediator!.Send(new GetObjectNodeQuery(new DBRef(1)));
		var attrValues = Mediator.CreateStream(new GetAttributeQuery(located.Object().DBRef, attribute));
		var attrValue = await attrValues.LastOrDefaultAsync();

		if (attrValue is null)
		{

			await Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single("0"),
				one.AsPlayer));

			var dbRefAttr = new DbRefAttribute(located.Object().DBRef, attribute);

			await Mediator.Send(new QueueCommandListRequest(arg1, stateForCallback,
				dbRefAttr, 0));

			return;
		}

		if (!int.TryParse(attrValue.Value.ToPlainText(), out var last))
		{
			await NotifyService!.Notify(executor, Errors.ErrorInteger, executor);
			return;
		}

		await Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single($"{last + 1}"),
			one.AsPlayer));

		var dbRefAttr2 = new DbRefAttribute(located.Object().DBRef, attribute);

		await Mediator.Send(new QueueCommandListRequest(arg1, stateForCallback,
			dbRefAttr2, last));

	}

	private static async ValueTask QueueSemaphoreWithDelay(IMUSHCodeParser parser, AnySharpObject located,
		string[] attribute, TimeSpan delay, MString arg1, ParserState? callbackState = null)
	{
		var stateForCallback = callbackState ?? parser.CurrentState;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var one = await Mediator!.Send(new GetObjectNodeQuery(new DBRef(1)));
		var attrValues = Mediator.CreateStream(new GetAttributeQuery(located.Object().DBRef, attribute));
		var attrValue = await attrValues.LastOrDefaultAsync();

		if (attrValue is null)
		{
			await Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single("0"),
				one.AsPlayer));
			await Mediator.Send(new QueueCommandListWithTimeoutRequest(arg1, stateForCallback,
				new DbRefAttribute(located.Object().DBRef, attribute), 0, delay));
			return;
		}

		if (!int.TryParse(attrValue.Value.ToPlainText(), out var last))
		{
			await NotifyService!.Notify(executor, Errors.ErrorInteger, executor);
			return;
		}

		await Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single($"{last + 1}"),
			one.AsPlayer));
		await Mediator.Send(new QueueCommandListWithTimeoutRequest(arg1, stateForCallback,
			new DbRefAttribute(located.Object().DBRef, attribute), last, delay));
	}

	private static async ValueTask<Option<CallState>> AtWaitForPid(IMUSHCodeParser parser, string? arg0,
		AnySharpObject executor, string? arg1,
		string[] switches)
	{
		if (!int.TryParse(arg0, out var pid))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidPidSpecified));
			return new CallState("#-1 INVALID PID");
		}

		if (string.IsNullOrEmpty(arg1))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitWhatToDoWithProcess));
			return new CallState(string.Format(Errors.ErrorTooFewArguments, "@WAIT", 2, 1));
		}

		var exists = Mediator!.CreateStream(new ScheduleSemaphoreQuery(pid));
		var maybeFoundPid = await exists.FirstOrDefaultAsync();

		if (maybeFoundPid is null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidPidSpecified));
			return new CallState("#-1 INVALID PID");
		}

		var timeArg = arg1;

		if (switches.Contains("UNTIL"))
		{
			if (!DateTimeOffset.TryParse(timeArg, out var dateTimeOffset))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeSpecified));
				return new CallState("#-1 INVALID TIME");
			}

			var until = DateTimeOffset.UtcNow - dateTimeOffset;
			await Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, until));

			return new CallState(maybeFoundPid.Pid.ToString());
		}

		if (arg1.StartsWith('+') || arg1.StartsWith('-'))
		{
			timeArg = arg1.Skip(1).ToString();
		}

		if (!long.TryParse(timeArg, out var secs))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WaitInvalidTimeSpecified));
			return new CallState("#-1 INVALID TIME");
		}

		if (arg1.StartsWith('+'))
		{
			var until = (maybeFoundPid.RunDelay ?? TimeSpan.Zero) + TimeSpan.FromSeconds(secs);
			await Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, until));
			return new CallState(maybeFoundPid.Pid.ToString());
		}

		if (arg1.StartsWith('-'))
		{
			var until = (maybeFoundPid.RunDelay ?? TimeSpan.Zero) - TimeSpan.FromSeconds(secs);
			await Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, until));
			return new CallState(maybeFoundPid.Pid.ToString());
		}

		await Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, TimeSpan.FromSeconds(secs)));
		return new CallState(maybeFoundPid.Pid.ToString());
	}

	[SharpCommand(Name = "@COMMAND",
		Switches =
		[
			"ADD", "ALIAS", "CLONE", "DELETE", "EqSplit", "LSARGS", "RSARGS", "NOEVAL", "ON", "OFF", "QUIET", "ENABLE",
			"DISABLE", "RESTRICT", "NOPARSE", "RSNoParse"
		], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "command", "code"])]
	public static async ValueTask<Option<CallState>> Command(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (args.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyName));
			return new CallState("#-1 NO COMMAND SPECIFIED");
		}

		var commandName = args["0"].Message?.ToPlainText()?.ToUpper();
		if (string.IsNullOrEmpty(commandName))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyName));
			return new CallState("#-1 NO COMMAND SPECIFIED");
		}

		var isQuiet = switches.Contains("QUIET");

		// Administrative switches - wizard only (except DELETE which requires God)
		if (switches.Any(s => new[] { "ADD", "ALIAS", "CLONE", "DELETE", "DISABLE", "ENABLE", "RESTRICT" }.Contains(s)))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			// Handle administrative operations
			if (switches.Contains("ADD"))
			{
				if (!isQuiet)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAddNotImplementedFormat));
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}

			if (switches.Contains("ALIAS"))
			{
				var aliasName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (string.IsNullOrEmpty(aliasName))
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyAlias));
					return new CallState("#-1 NO ALIAS SPECIFIED");
				}

				if (!isQuiet)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAliasNotImplementedFormat));
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}

			if (switches.Contains("CLONE"))
			{
				var cloneName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (string.IsNullOrEmpty(cloneName))
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyCloneName));
					return new CallState("#-1 NO CLONE NAME SPECIFIED");
				}

				if (!isQuiet)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandCloneNotImplementedFormat));
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}

			if (switches.Contains("DELETE"))
			{
				if (!executor.IsGod())
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandOnlyGodCanDelete));
					return new CallState(Errors.ErrorPerm);
				}

				if (!isQuiet)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandDeleteNotImplementedFormat));
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}

			if (switches.Contains("DISABLE"))
			{
				if (!isQuiet)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandDisableNotImplementedFormat));
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}

			if (switches.Contains("ENABLE"))
			{
				if (!isQuiet)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandEnableNotImplementedFormat));
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}

			if (switches.Contains("RESTRICT"))
			{
				var restriction = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (!isQuiet)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandRestrictNotImplementedFormat));
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}
		}

		// No switches - display command information
		if (CommandLibrary == null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandLibraryUnavailable));
			return new CallState("#-1 LIBRARY UNAVAILABLE");
		}

		// Try to find the command in the library
		if (!CommandLibrary.TryGetValue(commandName, out var commandInfo))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandNotFoundFormat), commandName);
			return new CallState("#-1 COMMAND NOT FOUND");
		}

		var (definition, isSystem) = commandInfo;
		var attr = definition.Attribute;

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoNameFormat), attr.Name);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoTypeFormat), isSystem ? "Built-in" : "User-defined");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoMinArgsFormat), attr.MinArgs);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoMaxArgsFormat), attr.MaxArgs);

		if (attr.Switches != null && attr.Switches.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoSwitchesFormat), string.Join(", ", attr.Switches));
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
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoBehaviorFormat), string.Join(" | ", behaviors));
		}

		if (!string.IsNullOrEmpty(attr.CommandLock))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoLockFormat), attr.CommandLock);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@DRAIN", Switches = ["ALL", "ANY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 1,
		MaxArgs = 2, ParameterNames = ["object", "attribute"])]
	public static async ValueTask<Option<CallState>> Drain(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message?.ToPlainText();
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var one = await Mediator!.Send(new GetObjectNodeQuery(new DBRef(1)));

		if (switches.Length > 1)
		{
			await NotifyService!.Notify(executor, Errors.ErrorTooManySwitches, executor);
			return new CallState(Errors.ErrorTooManySwitches);
		}

		var maybeObjectAndAttribute = HelperFunctions.SplitDbRefAndOptionalAttr(arg0);
		if (maybeObjectAndAttribute is { IsT1: true, AsT1: false })
		{
			await NotifyService!.Notify(executor, Errors.ErrorCantSeeThat, executor);
			return new CallState(Errors.ErrorCantSeeThat);
		}

		var (target, maybeAttribute) = maybeObjectAndAttribute.AsT0;
		var maybeObject = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, target,
			LocateFlags.All);

		switch (maybeObject)
		{
			case { IsError: true }:
				return new CallState(maybeObject.AsError.Value);
			case { IsNone: true }:
				return new CallState(Errors.ErrorCantSeeThat);
		}

		var objectToDrain = maybeObject.AsAnyObject;
		var attribute = maybeAttribute?.Split("`") ?? DefaultSemaphoreAttributeArray;
		var hasAll = switches.Contains("ALL");
		var hasAny = switches.Contains("ANY");

		// Parse the number parameter if provided
		int? drainCount = null;
		if (!string.IsNullOrEmpty(arg1))
		{
			if (!int.TryParse(arg1, out var count) || count < 1)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NotifyInvalidNumber));
				return new CallState("#-1 INVALID NUMBER");
			}
			drainCount = count;
		}

		// Cannot specify both /any and a specific attribute
		if (hasAny && maybeAttribute is not null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DrainCannotSpecifyBothAnyAndAttribute));
			return new CallState("#-1 INVALID COMBINATION");
		}

		// Cannot specify both /all and a number
		if (hasAll && drainCount.HasValue)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DrainCannotSpecifyBothAllAndNumber));
			return new CallState("#-1 INVALID COMBINATION");
		}

		//   @drain[/any][/all] <object>[/<attribute>][=<number>]
		if (hasAny)
		{
			// Drain all semaphores on the object
			var pids = Mediator!.CreateStream(new ScheduleSemaphoreQuery(objectToDrain.Object().DBRef));
			var filteredPids = pids
				.GroupBy(data => string.Join('`', data.SemaphoreSource.Attribute), x => x.SemaphoreSource)
				.Select(x => x.First());

			await foreach (var uniqueAttribute in filteredPids)
			{
				var dbRefAttrToDrain = uniqueAttribute;
				if (hasAll || !drainCount.HasValue)
				{
					// Drain all entries and clear attribute
					await Mediator.Send(new DrainSemaphoreRequest(dbRefAttrToDrain, null));
					await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, dbRefAttrToDrain.Attribute,
						MModule.single("0"),
						one.AsPlayer));
				}
				else
				{
					// Drain specified number
					await Mediator.Send(new DrainSemaphoreRequest(dbRefAttrToDrain, drainCount.Value));
					// Adjust semaphore count
					var currentAttr = await Mediator.CreateStream(
						new GetAttributeQuery(objectToDrain.Object().DBRef, dbRefAttrToDrain.Attribute)).LastOrDefaultAsync();
					if (currentAttr is not null && int.TryParse(currentAttr.Value.ToPlainText(), out var currentCount))
					{
						var newCount = currentCount + drainCount.Value;
						await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, dbRefAttrToDrain.Attribute,
							MModule.single(newCount.ToString()),
							one.AsPlayer));
					}
				}
			}
		}
		else
		{
			// Drain specific semaphore (or SEMAPHORE if not specified)
			var dbRefAttribute = new DbRefAttribute(objectToDrain.Object().DBRef, attribute);

			if (hasAll || !drainCount.HasValue)
			{
				// Drain all entries (default if no number specified)
				await Mediator!.Send(new DrainSemaphoreRequest(dbRefAttribute, null));
				if (hasAll)
				{
					// /all also clears the semaphore attribute
					await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, attribute,
						MModule.single("0"),
						one.AsPlayer));
				}
				else
				{
					// Without /all, just set to -1 to indicate no tasks waiting
					await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, attribute,
						MModule.single("-1"),
						one.AsPlayer));
				}
			}
			else
			{
				// Drain specified number
				await Mediator!.Send(new DrainSemaphoreRequest(dbRefAttribute, drainCount.Value));
				// Adjust semaphore count
				var currentAttr = await Mediator.CreateStream(
					new GetAttributeQuery(objectToDrain.Object().DBRef, attribute)).LastOrDefaultAsync();
				if (currentAttr is not null && int.TryParse(currentAttr.Value.ToPlainText(), out var currentCount))
				{
					var newCount = currentCount + drainCount.Value;
					await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, attribute,
						MModule.single(newCount.ToString()),
						one.AsPlayer));
				}
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@FORCE", Switches = ["NOEVAL", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSBrace, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "command"])]
	public static async ValueTask<Option<CallState>> Force(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var objArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.empty());
		var cmdListArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty());
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		// RSBrace preserves outer braces during argument parsing (PennMUSH CS_BRACES).
		// Strip them here before execution (PennMUSH PE_COMMAND_BRACES equivalent).
		cmdListArg = HelperFunctions.StripOuterBraces(cmdListArg);

		var maybeFound =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objArg.ToPlainText(),
				LocateFlags.All);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError;
		}

		var found = maybeFound.AsSharpObject;

		// God cannot be forced by anyone (PennMUSH src/wiz.c).
		if (found.IsGod() && !executor.IsGod())
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantForceGod));
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!await PermissionService!.Controls(executor, found))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ForcePermissionDeniedDoNotControl));
			return new CallState(Errors.ErrorPerm);
		}

		if (cmdListArg.Length < 1)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ForceThemToDoWhat));
			return new CallState(Errors.NothingToDo);
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

		try
		{
			// Note: Queue infrastructure available via QueueCommandListRequest if needed
			// Currently executes inline for immediate response (default PennMUSH behavior)
			await parser.With(
				state => state with
				{
					Executor = found.Object().DBRef,
					Caller = state.Executor
				},
				async newParser => await newParser.CommandListParseVisitor(cmdListArg)());
		}
		finally
		{
			// Restore Q-registers if /localize was set
			if (hasLocalize && savedRegisters != null && parser.CurrentState.Registers.TryPeek(out var regsToRestore))
			{
				regsToRestore.Clear();
				foreach (var (key, value) in savedRegisters)
				{
					regsToRestore[key] = value;
				}
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@IFELSE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 2, MaxArgs = 3, ParameterNames = ["condition", "true-command", "false-command"])]
	public static async ValueTask<Option<CallState>> IfElse(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var truthy = Predicates.Truthy(parsedIfElse!);

		if (truthy)
		{
			await parser.CommandListParse(parser.CurrentState.Arguments["1"].Message!);
		}
		else if (parser.CurrentState.Arguments.TryGetValue("2", out var arg2))
		{
			await parser.CommandListParse(arg2.Message!);
		}

		return new CallState(truthy);
	}

	[SharpCommand(Name = "@NSEMIT", Switches = ["ROOM", "NOEVAL", "SILENT"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public static async ValueTask<Option<CallState>> NoSpoofEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(Mediator!);
		var isSpoof = true;
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(executor, obj, InteractType.Hear));

		if (isSpoof)
		{
			var canSpoof = await executor.HasPower("CAN_SPOOF");
			var controlsExecutor = await PermissionService!.Controls(executor, enactor);

			if (!canSpoof && !controlsExecutor)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouDoNotHavePermissionToSpoofEmitsDetail));
				return new CallState(Errors.ErrorPerm);
			}
		}

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				isSpoof ? enactor : executor,
				INotifyService.NotificationType.NSEmit);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "@NSREMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["room", "message"])]
	public static async ValueTask<Option<CallState>> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail));
			return new CallState("#-1 Don't you have anything to say?");
		}

		var objects = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		var notificationType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			objects,
			LocateFlags.All,
			async target =>
			{
				if (!target.IsContainer)
				{
					return CallState.Empty;
				}

				var container = target.AsContainer;
				await CommunicationService!.SendToRoomAsync(
					executor,
					container,
					_ => message,
					notificationType);

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpCommand(Name = "@PROMPT", Switches = ["SILENT", "NOISY", "NOEVAL", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["target", "message"])]
	public static async ValueTask<Option<CallState>> Prompt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			return new CallState(string.Empty);
		}

		var notification = args["1"].Message!.ToString();
		var targetListText = MModule.plainText(args["0"].Message!);
		var nameListTargets = ArgHelpers.NameList(targetListText);

		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		foreach (var target in nameListTargets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			var maybeLocateTarget = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, enactor, enactor,
				targetString,
				LocateFlags.All);

			if (maybeLocateTarget.IsError)
			{
				await NotifyService!.Notify(executor, maybeLocateTarget.AsError.Message!, executor);
				continue;
			}

			var locateTarget = maybeLocateTarget.AsSharpObject;

			if (!await PermissionService!.CanInteract(executor, locateTarget, InteractType.Hear))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ObjectDoesNotWantToHearFromYouFormat), locateTarget.Object().Name);
				continue;
			}

			await NotifyService!.Prompt(locateTarget, notification);
		}

		return new None();
	}

	[SharpCommand(Name = "@SEARCH", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 3, ParameterNames = ["restriction..."])]
	public static async ValueTask<Option<CallState>> Search(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		// @search [<player>] [<classN>=<restrictionN>[,...]][,<begin>,<end>]
		// This is a complex command that searches the database with multiple filters

		string? playerName = null;
		string? searchCriteria = null;
		int? beginDbref = null;
		int? endDbref = null;

		// Parse arguments
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			var arg0 = args["0"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(arg0))
			{
				// Could be player name or search criteria
				playerName = arg0;
			}
		}

		if (args.Count > 1 && args.ContainsKey("1"))
		{
			searchCriteria = args["1"].Message?.ToPlainText();
		}

		if (args.Count > 2 && args.ContainsKey("2"))
		{
			var endStr = args["2"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out var end))
			{
				endDbref = end;
			}
		}

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchAdvancedHeader));

		if (playerName != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchPlayerFilterFormat), playerName);
		}

		if (searchCriteria != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchCriteriaFormat), searchCriteria);
		}

		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchRangeFormat), beginDbref ?? 0, endDbref?.ToString() ?? "end");
		}

		// Build search filter from criteria
		// For now, support basic search - future enhancement can parse complex criteria
		var filter = new ObjectSearchFilter
		{
			NamePattern = searchCriteria,
			MinDbRef = beginDbref,
			MaxDbRef = endDbref
		};

		// Query database with filters
		var results = await Mediator!.CreateStream(new GetFilteredObjectsQuery(filter)).ToListAsync();

		// Display results
		var count = 0;
		foreach (var obj in results)
		{
			// Check if executor can see this object (basic visibility check)
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchObjectEntryFormat), obj.Key, obj.Name, obj.Type);
			count++;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchObjectsFoundFormat), count);

		return new CallState(count.ToString());
	}

	[SharpCommand(Name = "@WHEREIS", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["name"])]
	public static async ValueTask<Option<CallState>> WhereIs(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WhereIsMustSpecifyPlayer));
			return new CallState("#-1 NO PLAYER SPECIFIED");
		}

		var targetName = args["0"].Message!.ToPlainText();

		// Locate the target player
		var maybeTarget = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (!maybeTarget.IsValid())
		{
			return new CallState("#-1 NOT FOUND");
		}

		var target = maybeTarget.WithoutError().WithoutNone();

		// Check if target is a player
		if (!target.IsPlayer)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WhereIsCanOnlyLocatePlayers));
			return new CallState("#-1 NOT A PLAYER");
		}

		var targetPlayer = target.AsPlayer;
		var targetObject = target.Object();

		// Check if target is UNFINDABLE
		var targetFlags = await targetObject.Flags.Value.ToListAsync();
		var isUnfindable = targetFlags.Any(f => f.Symbol == "U" || f.Name.Equals("UNFINDABLE", StringComparison.OrdinalIgnoreCase));

		// Notify the target that someone is trying to find them
		if (isUnfindable)
		{
			await NotifyService!.Notify(target,
				$"{executor.Object().Name} tried to locate you, but was unable to.");
			await NotifyService.Notify(executor,
				$"{targetObject.Name} is UNFINDABLE.", executor);
			return new CallState("#-1 UNFINDABLE");
		}

		// Get the target's location
		var targetLocation = await target.AsContent.Location();
		var locationName = targetLocation.Object().Name;

		// Notify the target that they were found
		await NotifyService!.Notify(target,
			$"{executor.Object().Name} has just located your position.");

		// Notify the executor of the target's location
		await NotifyService.Notify(executor,
			$"{targetObject.Name} is in {locationName}.", executor);

		return new CallState(targetLocation.Object().DBRef.ToString());
	}

	[SharpCommand(Name = "@BREAK", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 2, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Break(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Inline does nothing.
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var nargs = args.Count;

		// Note: INLINE is default behavior (immediate execution)
		// QUEUED switch queues the command for later execution via task scheduler
		var useQueue = switches.Contains("QUEUED");

		switch (nargs)
		{
			case 0:
				// No condition provided — treat as falsy (@break 0 = don't break).
				break;
			case 1:
				if (args["0"].Message.Truthy())
				{
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}

				return args["0"];
			case 2 when args["0"].Message.Truthy():
				var command = await args["1"].ParsedMessage();

				if (useQueue)
				{
					// Queue the command for later execution
					var executor = parser.CurrentState.Executor ?? throw new InvalidOperationException("Executor cannot be null");
					await Mediator!.Send(new QueueCommandListRequest(
						command!,
						parser.CurrentState,
						new DbRefAttribute(executor, ["BREAK"]),
						-1));
				}
				else
				{
					// Execute inline (default)
					var commandList = parser.CommandListParseVisitor(command!);
					await commandList();
				}

				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));

				return args["0"];
			case 2:
				return args["0"];
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@CONFIG", Switches = ["SET", "SAVE", "LOWERCASE", "LIST"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["option", "value"])]
	public static async ValueTask<Option<CallState>> Config(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var useLowercase = switches.Contains("LOWERCASE");

		// Get all configuration categories using generated accessor
		var allCategories = ConfigGenerated.ConfigAccessor.Categories.ToList();

		// Helper to get all config options with metadata using generated accessors
		var getAllOptions = () =>
		{
			var options = new List<(string Category, string PropertyName, SharpConfigAttribute ConfigAttr, object? Value)>();

			foreach (var propName in ConfigGenerated.ConfigMetadata.PropertyToAttributeName.Keys)
			{
				var attr = ConfigGenerated.ConfigMetadata.PropertyMetadata[propName];
				var value = ConfigGenerated.ConfigAccessor.GetValue(Configuration!.CurrentValue, propName);
				var category = ConfigGenerated.ConfigAccessor.GetCategoryForProperty(propName) ?? "";

				options.Add((category, propName, attr, value));
			}

			return options;
		};

		// @config/set or @config/save - requires wizard/god permissions
		if (switches.Contains("SET") || switches.Contains("SAVE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			if (switches.Contains("SAVE") && !executor.IsGod())
			{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOnlyGodCanUseSave));
				return new CallState(Errors.ErrorPerm);
			}

			// /set and /save not yet implemented - would require runtime config modification
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigSetSaveNotImplemented));
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		// @config with no arguments - list categories
		if (args.Count == 0)
		{
		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCategoriesHeader));
		foreach (var cat in allCategories)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigCategoryItemFormat), cat);
		}
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigUseCategoryHelp));
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigUseOptionHelp));
			return CallState.Empty;
		}

		var searchTerm = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";

		// Check if searchTerm is a category
		var matchingCategory = allCategories.FirstOrDefault(c =>
			c.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingCategory != null)
		{
			// List all options in the category
			var categoryOptions = getAllOptions()
				.Where(opt => opt.Category.Equals(matchingCategory, StringComparison.OrdinalIgnoreCase))
				.OrderBy(opt => opt.ConfigAttr.Name)
				.ToList();

			if (categoryOptions.Count == 0)
			{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigNoOptionsInCategoryFormat), matchingCategory);
				return CallState.Empty;
			}

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionsInCategoryFormat), matchingCategory);
		foreach (var opt in categoryOptions)
		{
			var name = useLowercase ? opt.ConfigAttr.Name.ToLower() : opt.ConfigAttr.Name;
			var value = opt.Value?.ToString() ?? "null";
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), name, value);
		}
			return CallState.Empty;
		}

		// Check if searchTerm is a specific option
		var allOptions = getAllOptions();
		var matchingOption = allOptions.FirstOrDefault(opt =>
			opt.ConfigAttr.Name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingOption.PropertyName != null)
		{
			var name = useLowercase ? matchingOption.ConfigAttr.Name.ToLower() : matchingOption.ConfigAttr.Name;
			var value = matchingOption.Value?.ToString() ?? "null";
			var desc = matchingOption.ConfigAttr.Description;

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionValueFormat), name, value);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionDescriptionFormat), desc);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigOptionCategoryFormat), matchingOption.Category);
			return new CallState(value);
		}

		// No match found
		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ConfigNoCategoryOrOptionFormat), searchTerm);
		return new CallState("#-1 NOT FOUND");
	}

	[SharpCommand(Name = "@EDIT", Switches = ["FIRST", "CHECK", "QUIET", "REGEXP", "NOCASE", "ALL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 1, MaxArgs = 0, ParameterNames = ["object/attribute", "from", "to"])]
	public static async ValueTask<Option<CallState>> Edit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		// Parse object/attribute pattern (left side of =)
		var objAttrArg = args.ElementAtOrDefault(0).Value;
		if (objAttrArg == null || objAttrArg.Message == null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditInvalidArguments));
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		var objAttrText = MModule.plainText(objAttrArg.Message);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText);

		if (!split.TryPickT0(out var details, out _) || string.IsNullOrEmpty(details.Attribute))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditInvalidFormat));
			return new CallState("#-1 INVALID FORMAT");
		}

		var (dbref, attrPattern) = details;

		// Locate object
		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			enactor, executor, dbref, LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var targetObject = locate.AsSharpObject;

		// Check permissions
		var canModify = await PermissionService!.Controls(executor, targetObject);
		if (!canModify)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// Parse search and replace strings (right side of =)
		// With RSArgs, the arguments after = are split by comma
		var searchArg = args.ElementAtOrDefault(1).Value;
		var replaceArg = args.ElementAtOrDefault(2).Value;

		if (searchArg == null || searchArg.Message == null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditMustSpecifySearchAndReplace));
			return new CallState("#-1 MISSING ARGUMENTS");
		}

		var search = searchArg.Message.ToPlainText();
		var replace = replaceArg?.Message != null ? replaceArg.Message.ToPlainText() : string.Empty;

		// Get matching attributes
		var attributes = await AttributeService!.GetAttributePatternAsync(
			executor, targetObject, attrPattern, false, IAttributeService.AttributePatternMode.Wildcard);

		if (attributes.IsError)
		{
			await NotifyService!.Notify(executor, attributes.AsError.Value, executor);
			return new CallState(attributes.AsError.Value);
		}

		var attrList = attributes.AsAttributes.ToList();
		if (attrList.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditNoMatchingAttributesFound));
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		// Process each attribute
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
				// Regex mode
				newText = await PerformRegexEdit(parser, originalText, search, replace, isAll, isNoCase);
			}
			else
			{
				// Simple string replacement mode
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
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditAttributeSetFormat), attrName);
			}
			else if (!isQuiet && isCheck)
			{
				// Show changes with highlighting (simple version for now)
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EditWouldChangeToFormat), attrName, newText);
			}

			// Actually set the attribute unless in check mode
			if (!isCheck)
			{
				await AttributeService!.SetAttributeAsync(executor, targetObject, attrName, MModule.single(newText));
			}
		}

		// Summary message
		if (isQuiet || (modifiedCount + unchangedCount > 1))
		{
			var checkPrefix = isCheck ? "Would edit" : "Edited";
			await NotifyService!.Notify(executor,
				$"{checkPrefix} {modifiedCount} attribute{(modifiedCount != 1 ? "s" : "")}. {unchangedCount} unchanged.", executor);
		}

		return new CallState(string.Empty);
	}

	/// <summary>
	/// Split search/replace text by comma, respecting curly brace escaping
	/// </summary>
	private static string[] SplitSearchReplace(string text)
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

		// Trim and remove outer braces if present
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
	private static string PerformSimpleEdit(string text, string search, string replace, bool firstOnly)
	{
		if (search == "^")
		{
			// Prepend
			return replace + text;
		}
		else if (search == "$")
		{
			// Append
			return text + replace;
		}
		else if (firstOnly)
		{
			// Replace only first occurrence
			int index = text.IndexOf(search);
			if (index >= 0)
			{
				return text[..index] + replace + text[(index + search.Length)..];
			}
			return text;
		}
		else
		{
			// Replace all occurrences
			return text.Replace(search, replace);
		}
	}

	/// <summary>
	/// Perform regex replacement with evaluation
	/// </summary>
	private static async ValueTask<string> PerformRegexEdit(IMUSHCodeParser parser, string text,
		string pattern, string replaceTemplate, bool all, bool nocase)
	{
		try
		{
			var options = RegexOptions.None;
			if (nocase)
			{
				options |= RegexOptions.IgnoreCase;
			}

			var regex = new Regex(pattern, options);

			if (all)
			{
				// Replace all matches, working backwards
				var matches = regex.Matches(text).Cast<Match>().Reverse().ToList();
				foreach (var match in matches)
				{
					var replacement = await EvaluateRegexReplacement(parser, regex, match, replaceTemplate);
					text = text[..match.Index] + replacement + text[(match.Index + match.Length)..];
				}
			}
			else
			{
				// Replace only first match
				var match = regex.Match(text);
				if (match.Success)
				{
					var replacement = await EvaluateRegexReplacement(parser, regex, match, replaceTemplate);
					text = text[..match.Index] + replacement + text[(match.Index + match.Length)..];
				}
			}

			return text;
		}
		catch (ArgumentException)
		{
			return text; // Return unchanged on regex error
		}
	}

	/// <summary>
	/// Evaluate replacement template with captured groups
	/// </summary>
	private static async ValueTask<string> EvaluateRegexReplacement(IMUSHCodeParser parser,
		Regex regex, Match match, string template)
	{
		var replacement = template;

		// Replace $0, $1, etc. with captured groups
		for (int j = 0; j < match.Groups.Count; j++)
		{
			replacement = replacement.Replace($"${j}", match.Groups[j].Value);
		}

		// Replace named captures
		foreach (var groupName in regex.GetGroupNames().Where(groupName => !int.TryParse(groupName, out _)))
		{
			var group = match.Groups[groupName];
			if (group.Success)
			{
				replacement = replacement.Replace($"$<{groupName}>", group.Value);
			}
		}

		// Evaluate the replacement
		var evaluatedReplacement = await parser.FunctionParse(MModule.single(replacement));
		return evaluatedReplacement?.Message?.ToPlainText() ?? replacement;
	}

	[SharpCommand(Name = "@FUNCTION",
		Switches = ["ALIAS", "BUILTIN", "CLONE", "DELETE", "ENABLE", "DISABLE", "PRESERVE", "RESTORE", "RESTRICT"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 5, ParameterNames = ["name", "object/attribute"])]
	public static async ValueTask<Option<CallState>> Function(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		// No arguments - list all user-defined functions
		if (args.Count == 0)
		{
			if (FunctionLibrary == null)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable));
				return new CallState("#-1 LIBRARY UNAVAILABLE");
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionGlobalUserDefinedHeader));

			// Check if executor has Functions power or is wizard
			var canSeeDetails = await executor.IsWizard();

			var userFunctions = FunctionLibrary.Where(kvp => !kvp.Value.IsSystem).ToArray();
			var builtinFunctions = FunctionLibrary.Where(kvp => kvp.Value.IsSystem).ToArray();

			if (canSeeDetails)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionUserDefinedCountFormat), userFunctions.Length);
				foreach (var (name, (def, _)) in userFunctions.Take(10))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEntryFormat), name, def.Attribute.MinArgs, def.Attribute.MaxArgs, def.Attribute.Flags);
				}
				if (userFunctions.Length > 10)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionAndMoreFormat), userFunctions.Length - 10);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltInCountFormat), builtinFunctions.Length);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionUserDefinedSummaryFormat), userFunctions.Length);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltInSummaryFormat), builtinFunctions.Length);
			}

			return CallState.Empty;
		}

		var functionName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(functionName))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyName));
			return new CallState("#-1 NO FUNCTION SPECIFIED");
		}

		// Handle administrative switches
		if (switches.Contains("ALIAS"))
		{
			var aliasName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(aliasName))
			{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyAliasName));
			return new CallState("#-1 NO ALIAS SPECIFIED");
		}

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionAliasWouldCreateFormat), aliasName, functionName);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionAliasingNotImplemented));
		return new CallState("#-1 NOT IMPLEMENTED");
	}

	if (switches.Contains("CLONE"))
	{
		var cloneName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
		if (string.IsNullOrEmpty(cloneName))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyCloneName));
				return new CallState("#-1 NO CLONE NAME SPECIFIED");
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionCloneWouldCloneFormat), functionName, cloneName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionCloningNotImplemented));
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		if (switches.Contains("DELETE"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDeleteWouldDeleteFormat), functionName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDeletionNotImplemented));
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		if (switches.Contains("DISABLE"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDisableWouldDisableFormat), functionName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDisablingNotImplemented));
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		if (switches.Contains("ENABLE"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEnableWouldEnableFormat), functionName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEnablingNotImplemented));
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		if (switches.Contains("RESTRICT"))
		{
			var restriction = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestrictWouldRestrictFormat), functionName, restriction ?? "none");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestrictionNotImplemented));
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		// Check if defining a new function: @function <name>=<obj>,<attrib>[,<min>,<max>[,<restrictions>]]
		if (args.Count >= 2)
		{
			var defString = args["1"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(defString))
			{
				// Parse definition: obj, attrib[, min, max[, restrictions]]
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDefineWouldDefineFormat), functionName, defString);

				// Parse min/max args if provided
				if (args.Count >= 3)
				{
					var minArgs = args.GetValueOrDefault("2")?.Message?.ToPlainText();
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMinArgsFormat), minArgs ?? "none");
				}

				if (args.Count >= 4)
				{
					var maxArgs = args.GetValueOrDefault("3")?.Message?.ToPlainText();
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMaxArgsFormat), maxArgs ?? "none");
				}

				if (args.Count >= 5)
				{
					var restrictions = args.GetValueOrDefault("4")?.Message?.ToPlainText();
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestrictionsArgFormat), restrictions ?? "none");
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDynamicDefinitionNotImplemented));
				return new CallState("#-1 NOT IMPLEMENTED");
			}
		}

		// Single argument - show function information
		if (FunctionLibrary == null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable));
			return new CallState("#-1 LIBRARY UNAVAILABLE");
		}

		// Try to find the function in the library
		var functionNameUpper = functionName.ToUpper();
		if (!FunctionLibrary.TryGetValue(functionNameUpper, out var functionInfo))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), functionName);
			return new CallState("#-1 FUNCTION NOT FOUND");
		}

		var (definition, isSystem) = functionInfo;
		var attr = definition.Attribute;

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), attr.Name);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoTypeFormat), isSystem ? "Built-in" : "User-defined");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMinArgsFormat), attr.MinArgs);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMaxArgsFormat), attr.MaxArgs);

		var flags = new List<string>();
		if ((attr.Flags & FunctionFlags.Regular) != 0) flags.Add("Regular");
		if ((attr.Flags & FunctionFlags.StripAnsi) != 0) flags.Add("StripAnsi");
		if ((attr.Flags & FunctionFlags.NoParse) != 0) flags.Add("NoParse");
		if ((attr.Flags & FunctionFlags.Localize) != 0) flags.Add("Localize");
		if ((attr.Flags & FunctionFlags.Literal) != 0) flags.Add("Literal");

		if (flags.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoFlagsFormat), string.Join(" | ", flags));
		}

		if (attr.Restrict != null && attr.Restrict.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoRestrictionsFormat), string.Join(", ", attr.Restrict));
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@LEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public static async ValueTask<Option<CallState>> LocationEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 1)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail));
			return new CallState("#-1 Don't you have anything to say?");
		}

		var executorLocation = await executor.OutermostWhere();
		var message = args["0"].Message!;

		await CommunicationService!.SendToRoomAsync(
			executor,
			executorLocation,
			_ => message,
			INotifyService.NotificationType.Emit);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NSLEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public static async ValueTask<Option<CallState>> NoSpoofLocationEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 1)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail));
			return new CallState("#-1 Don't you have anything to say?");
		}

		var spoofType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(Mediator!);
		var message = args["0"].Message!;

		await foreach (var obj in contents
										 .Where(async (x, _)
											 => await PermissionService.CanInteract(executor, x, InteractType.Hear)))
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				executor,
				spoofType);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NSZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["zone", "message"])]
	public static async ValueTask<Option<CallState>> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail));
			return new CallState("#-1 Don't you have anything to say?");
		}

		var zoneName = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		// Determine notification type based on nospoof permissions
		var notificationType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		// Locate the zone object
		await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			zoneName,
			LocateFlags.All,
			async zone =>
			{
				// Find all objects in the zone
				var zoneObjects = Mediator!.CreateStream(new GetObjectsByZoneQuery(zone));

				// Get all rooms in the zone
				var rooms = zoneObjects.Where(obj => obj.Type == DatabaseConstants.TypeRoom);

				// Send message to each room
				await foreach (var room in rooms)
				{
					var roomContents = Mediator!.CreateStream(new GetContentsQuery(new DBRef(room.Key)))!;
					await foreach (var content in roomContents)
					{
						await NotifyService!.Notify(content.WithRoomOption(), message, executor, notificationType);
					}
				}

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpCommand(Name = "@PS", Switches = ["ALL", "SUMMARY", "COUNT", "QUICK", "DEBUG"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 1, ParameterNames = ["player"])]
	public static async ValueTask<Option<CallState>> ProcessStatus(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @ps[/<switch>] [<player>]
		// @ps[/debug] <pid>

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();

		// Check if showing debug info for a specific PID
		if (switches.Contains("DEBUG"))
		{
			var pidStr = args.GetValueOrDefault("0")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(pidStr))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltMustSpecifyPid));
				return new CallState("#-1 NO PID SPECIFIED");
			}

			if (!long.TryParse(pidStr, out var pid))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HaltInvalidPidFormat));
				return new CallState("#-1 INVALID PID");
			}

			// Find the semaphore task with this PID
			var tasks = await Mediator!.CreateStream(new ScheduleSemaphoreQuery(pid)).ToArrayAsync();
			if (tasks.Length == 0)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsNoTaskWithPidFormat), pid);
				return new CallState("#-1 NOT FOUND");
			}

			var task = tasks[0];
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugTaskFormat), pid);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugOwnerFormat), task.Owner);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugSemaphoreFormat), task.SemaphoreSource);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugCommandFormat), task.Command.ToPlainText());
			if (task.RunDelay.HasValue)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsDebugDelayFormat), task.RunDelay.Value.TotalSeconds.ToString("F1"));
			}

			return CallState.Empty;
		}

		// Determine target object
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
				var maybeTarget = await LocateService!.LocateAndNotifyIfInvalid(
					parser, executor, executor, playerName, LocateFlags.All);
				if (!maybeTarget.IsValid())
				{
					return new CallState("#-1 INVALID TARGET");
				}
				target = maybeTarget.WithoutError().WithoutNone();
			}
		}
		else
		{
			target = executor;
		}

		var targetDbRef = target.Object().DBRef;

		// Get queue counts
		var semaphoreTasks = await Mediator!.CreateStream(new ScheduleSemaphoreQuery(targetDbRef)).ToArrayAsync();
		var delayTasks = await Mediator.CreateStream(new ScheduleDelayQuery(targetDbRef)).ToArrayAsync();
		var enqueueTasks = await Mediator.CreateStream(new ScheduleEnqueueQuery(targetDbRef)).ToArrayAsync();

		// Check for /summary switch
		if (switches.Contains("SUMMARY"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSummaryHeader));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsCommandQueueFormat), enqueueTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueFormat), delayTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreQueueFormat), semaphoreTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsLoadAverageZero));
			return CallState.Empty;
		}

		// Check for /quick switch
		if (switches.Contains("QUICK"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsQuickHeader));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsCommandQueueFormat), enqueueTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueFormat), delayTasks.Length);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreQueueFormat), semaphoreTasks.Length);
			return CallState.Empty;
		}

		// Check for /all switch (wizard only)
		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			// Get all tasks across the system
			var allTasks = await Mediator!.CreateStream(new ScheduleAllTasksQuery()).ToArrayAsync();

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAllHeader));
			foreach (var (group, tasks) in allTasks)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAllGroupFormat), group, tasks.Length);
			}

			return CallState.Empty;
		}

		// Show detailed queue for target
		var targetName = target.Object().DBRef.ToString();
		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsQueueForTargetFormat), targetName);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsCommandQueueFormat), enqueueTasks.Length);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueFormat), delayTasks.Length);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreQueueFormat), semaphoreTasks.Length);

		// List semaphore tasks
		if (semaphoreTasks.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EmptyLine));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreTasksHeader));
			foreach (var task in semaphoreTasks.Take(10))
			{
				var delay = task.RunDelay.HasValue ? $"+{task.RunDelay.Value.TotalSeconds:F1}s" : "ready";
				var commandText = task.Command.ToPlainText();
				var truncatedCommand = commandText.Length > 40
					? commandText[..40]
					: commandText;
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsSemaphoreTaskEntryFormat), task.Pid, task.SemaphoreSource, delay, truncatedCommand);
			}
			if (semaphoreTasks.Length > 10)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAndMoreFormat), semaphoreTasks.Length - 10);
			}
		}

		// List delay tasks
		if (delayTasks.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EmptyLine));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitQueueHeader));
			foreach (var pid in delayTasks.Take(10))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsWaitTaskEntryFormat), pid);
			}
			if (delayTasks.Length > 10)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsAndMoreFormat), delayTasks.Length - 10);
			}
		}
		// - Permission checks for viewing other players' queues
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PsQueueManagementNotImplemented));

		return CallState.Empty;
	}

	[SharpCommand(Name = "@SELECT",
		Switches = ["NOTIFY", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse, MinArgs = 1, MaxArgs = int.MaxValue, ParameterNames = ["expression", "cases..."])]
	public static async ValueTask<Option<CallState>> Select(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @select <string>=<expr1>, <action1> [,<exprN>, <actionN>]... [,<default>]
		// Like @switch but only runs the first matching action

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		var testString = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(testString))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectMustSpecifyTestString));
			return new CallState("#-1 NO TEST STRING");
		}

		// Pattern matching flags (declared outside try/finally for /localize restore access)
		bool isRegexp = switches.Contains("REGEXP");
		bool isInline = switches.Contains("INLINE") || switches.Contains("INPLACE");
		bool localizeRegs = switches.Contains("LOCALIZE");
		bool clearRegs = switches.Contains("CLEARREGS");

		// Implement /LOCALIZE: save Q-registers so matched actions cannot permanently change
		// the caller's Q-registers. /CLEARREGS: start the action with empty Q-registers.
		// NOTE: Save must happen before Clear (both use a single TryPeek for safety).
		Dictionary<string, MString>? savedRegisters = null;
		if ((localizeRegs || clearRegs) && parser.CurrentState.Registers.TryPeek(out var selectTopRegs))
		{
			if (localizeRegs)
			{
				savedRegisters = new Dictionary<string, MString>(selectTopRegs);
			}

			if (clearRegs)
			{
				selectTopRegs.Clear();
			}
		}

		// Push the switch string onto the context stack
		parser.CurrentState.SwitchStack.Push(args["0"].Message!);

		try
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectTestingStringFormat), testString);

			// Count expression/action pairs (args are: 0=test string, then pairs of expr,action)
			int pairCount = (args.Count - 1) / 2;
			bool hasDefault = (args.Count - 1) % 2 == 1;

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectExpressionActionPairsFormat), pairCount);
			if (hasDefault)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectHasDefaultAction));
			}

			// Check switches
			if (switches.Contains("REGEXP"))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectModeRegexp));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectModeWildcard));
			}

			if (switches.Contains("INLINE") || switches.Contains("INPLACE"))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectExecutionInline));

				if (switches.Contains("NOBREAK"))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectNoBreakWontPropagate));
				}

				if (switches.Contains("LOCALIZE"))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectQregistersLocalized));
				}

				if (switches.Contains("CLEARREGS"))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectQregistersCleared));
				}
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectExecutionQueued));
			}

			if (switches.Contains("NOTIFY"))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectWillQueueNotify));
			}


			// Process expression/action pairs
			bool matchFound = false;
			for (int i = 0; i < pairCount; i++)
			{
				var exprIndex = (i * 2) + 1;
				var actionIndex = (i * 2) + 2;

				var pattern = args[exprIndex.ToString()].Message?.ToPlainText() ?? "";
				var action = args[actionIndex.ToString()].Message;

				// Perform pattern matching
				bool matches = false;
				if (isRegexp)
				{
					// Regular expression matching
					try
					{
						var regex = new Regex(pattern, RegexOptions.None);
						matches = regex.IsMatch(testString);
					}
					catch (ArgumentException)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectInvalidRegexPatternFormat), pattern);
						continue;
					}
				}
				else
				{
					// Wildcard pattern matching
					var regexPattern = MModule.getWildcardMatchAsRegex2(pattern);
					var regex = new Regex(regexPattern, RegexOptions.None);
					matches = regex.IsMatch(testString);
				}

				if (matches && action != null)
				{
					matchFound = true;

					// Substitute #$ with test string in action
					var actionText = action.ToPlainText().Replace("#$", testString);
					var actionMString = MModule.single(actionText);

					// Execute action (inline for now, queue support can be added later)
					if (isInline)
					{
						await parser.CommandListParse(actionMString);
					}
					else
					{
						// Queue the action
						await Mediator!.Send(new QueueCommandListRequest(
							actionMString,
							parser.CurrentState,
							new DbRefAttribute(executor.Object().DBRef, []),
							0));
					}

					// @select only executes first match
					break;
				}
			}

			// Execute default action if no match and default exists
			if (!matchFound && hasDefault)
			{
				var defaultIndex = args.Count - 1;
				var defaultAction = args[defaultIndex.ToString()].Message;

				if (defaultAction != null)
				{
					var actionText = defaultAction.ToPlainText().Replace("#$", testString);
					var actionMString = MModule.single(actionText);

					if (isInline)
					{
						await parser.CommandListParse(actionMString);
					}
					else
					{
						await Mediator!.Send(new QueueCommandListRequest(
							actionMString,
							parser.CurrentState,
							new DbRefAttribute(executor.Object().DBRef, []),
							0));
					}
				}
			}

			return CallState.Empty;
		}
		finally
		{
			// Pop the switch string from the context stack
			parser.CurrentState.SwitchStack.TryPop(out _);

			// Restore Q-registers if /localize was set
			if (localizeRegs && savedRegisters != null && parser.CurrentState.Registers.TryPeek(out var regsToRestore))
			{
				regsToRestore.Clear();
				foreach (var (key, value) in savedRegisters)
				{
					regsToRestore[key] = value;
				}
			}
		}
	}

	[SharpCommand(Name = "@TRIGGER",
		Switches = ["CLEARREGS", "SPOOF", "INLINE", "NOBREAK", "LOCALIZE", "INPLACE", "MATCH"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 1, MaxArgs = int.MaxValue, ParameterNames = ["object/attribute", "arguments..."])]
	public static async ValueTask<Option<CallState>> Trigger(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @trigger[/<switches>] <object>/<attribute>[=<arg0>, <arg1>, ...]
		// @trigger/match[/<switches>] <object>/<attribute>=<string>

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerMustSpecifyAttributePath));
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}

		// Parse object/attribute
		var parts = attributePath.Split('/', 2);
		if (parts.Length < 2)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerMustSpecifyObjectAttributePath));
			return new CallState("#-1 INVALID PATH");
		}

		var objectName = parts[0];
		var attributeName = parts[1];

		// Locate the target object
		var maybeObject = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, enactor, objectName, LocateFlags.All);

		if (maybeObject.IsError)
		{
			return maybeObject.AsError;
		}

		var targetObject = maybeObject.AsSharpObject;

		// Check control permissions
		if (!await PermissionService!.Controls(executor, targetObject))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerPermissionDeniedDoNotControl));
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// Get the attribute - must be visible to executor (who controls the object and is issuing @trigger)
		var attributeResult = await AttributeService!.GetAttributeAsync(
			executor, targetObject, attributeName, IAttributeService.AttributeMode.Read, false);

		if (attributeResult.IsError)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerNoSuchAttributeFormat), attributeName);
			return new CallState("#-1 NO SUCH ATTRIBUTE");
		}

		if (attributeResult.IsNone)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerNoSuchAttributeFormat), attributeName);
			return new CallState("#-1 NO SUCH ATTRIBUTE");
		}

		var attribute = attributeResult.AsAttribute.Last();
		var attributeText = attribute.Value.ToPlainText();
		var attributeLongName = attribute.LongName!.ToUpper();

		if (string.IsNullOrWhiteSpace(attributeText))
		{
			// Empty attribute - nothing to trigger
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

		// Handle /match switch for pattern matching
		if (switches.Contains("MATCH"))
		{
			// With /match, the first argument (index 1) is the test string
			// The attribute should contain patterns to match against
			if (!args.TryGetValue("1", out var matchArg) || matchArg.Message == null)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TriggerMustProvideMatchString));
				return new CallState("#-1 NO MATCH STRING");
			}

			var testString = matchArg.Message.ToPlainText();

			// Parse attribute text as pattern list (one pattern per line or space-separated)
			var patterns = attributeText.Split(new[] { '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);

			bool matchFound = false;
			foreach (var pattern in patterns)
			{
				var trimmedPattern = pattern.Trim();
				if (string.IsNullOrEmpty(trimmedPattern)) continue;

				// Use wildcard pattern matching
				var regexPattern = MModule.getWildcardMatchAsRegex2(trimmedPattern);
				var regex = new Regex(regexPattern, RegexOptions.None);

				if (regex.IsMatch(testString))
				{
					matchFound = true;
					break;
				}
			}

			// Only execute if match was found
			if (!matchFound)
			{
				return CallState.Empty;
			}

			// Continue with execution below
		}

		// Note: INLINE switch executes immediately (current default behavior).
		// Queue dispatch available via QueueCommandListRequest if needed for future enhancements.

		// Execute with recursion tracking and DEBUG/VERBOSE support
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

			await parser.With(state => stateWithRegisters, newParser => newParser.WithAttributeDebug(attribute,
				async p => await p.CommandListParseVisitor(attribute.Value)()));

			return CallState.Empty;
		});
	}

	[SharpCommand(Name = "@ZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["zone", "message"])]
	public static async ValueTask<Option<CallState>> ZoneEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail));
			return new CallState("#-1 Don't you have anything to say?");
		}

		var zoneName = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		// Locate the zone object
		await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			zoneName,
			LocateFlags.All,
			async zone =>
			{
				// Find all objects in the zone
				var zoneObjects = Mediator!.CreateStream(new GetObjectsByZoneQuery(zone));

				// Get all rooms in the zone
				var rooms = zoneObjects.Where(obj => obj.Type == DatabaseConstants.TypeRoom);

				// Send message to each room
				await foreach (var room in rooms)
				{
					var roomContents = Mediator!.CreateStream(new GetContentsQuery(new DBRef(room.Key)))!;
					await foreach (var content in roomContents)
					{
						await NotifyService!.Notify(content.WithRoomOption(), message, executor, INotifyService.NotificationType.Emit);
					}
				}

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpCommand(Name = "@CHANNEL",
		Switches =
		[
			"LIST", "ADD", "DELETE", "RENAME", "MOGRIFIER", "NAME", "PRIVS", "QUIET", "DECOMPILE", "DESCRIBE", "CHOWN",
			"WIPE", "MUTE", "UNMUTE", "GAG", "UNGAG", "HIDE", "UNHIDE", "WHAT", "TITLE", "BRIEF", "RECALL", "BUFFER",
			"COMBINE", "UNCOMBINE", "ON", "JOIN", "OFF", "LEAVE", "WHO"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs, MinArgs = 0, MaxArgs = 0, ParameterNames = ["channel", "options..."])]
	public static async ValueTask<Option<CallState>> Channel(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("QUIET") && (!switches.Contains("LIST") || !switches.Contains("RECALL")))
		{
			return new CallState("CHAT: INCORRECT COMBINATION OF SWITCHES");
		}

		// Note: Channel visibility checking is handled by PermissionService.ChannelCanSeeAsync in each handler
		return switches switch
		{
			[.., "LIST"] => await ChannelCommand.ChannelList.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message!, switches),
			["WHAT"] => await ChannelWhat.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!),
			["WHO"] => await ChannelWho.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!),
			["ON"] or ["JOIN"] => await ChannelOn.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message),
			["OFF"] or ["LEAVE"] => await ChannelOff.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message),
			["GAG"] => await ChannelGag.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!, switches),
			["MUTE"] => await ChannelMute.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["HIDE"] => await ChannelHide.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["COMBINE"] => await ChannelCombine.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["TITLE"] => await ChannelTitle.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			[.., "RECALL"] => await ChannelRecall.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message!, switches),
			["ADD"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelAdd.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
					Configuration!, args["0"].Message!, args["1"].Message!),
			["PRIVS"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelPrivs.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
					args["0"].Message!, args["1"].Message!),
			["DESCRIBE"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelDescribe.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
					args["0"].Message!, args["1"].Message!),
			["BUFFER"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelBuffer.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
					Configuration!, args["0"].Message!, args["1"].Message!),
			[.., "DECOMPILE"] => await ChannelDecompile.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message!, switches),
			["CHOWN"] => await ChannelChown.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["RENAME"] => await ChannelRename.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				Configuration!, args["0"].Message!, args["1"].Message!),
			["WIPE"] => await ChannelWipe.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["DELETE"] => await ChannelDelete.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["MOGRIFIER"] => await ChannelMogrifier.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args.GetValueOrDefault("1")?.Message),
			_ => new CallState("What do you want to do with the channel?")
		};
	}

	[SharpCommand(Name = "@DECOMPILE", Switches = ["DB", "NAME", "PREFIX", "TF", "FLAGS", "ATTRIBS", "SKIPDEFAULTS"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object", "name"])]
	public static async ValueTask<Option<CallState>> Decompile(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		// Parse arguments: object[/attribute pattern][=prefix]
		if (args.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouMustSpecifyObjectToDecompile));
			return new CallState("#-1 NO OBJECT SPECIFIED");
		}

		var objectSpec = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(objectSpec))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouMustSpecifyObjectToDecompile));
			return new CallState("#-1 NO OBJECT SPECIFIED");
		}

		// Parse prefix if provided in arg 1 (from = split)
		var prefix = args.Count >= 2 ? args["1"].Message?.ToPlainText() ?? "" : "";

		// Handle /tf switch - sets prefix to TFPREFIX attribute or default
		if (switches.Contains("TF"))
		{
			var tfPrefixAttr = await AttributeService!.GetAttributeAsync(executor, executor, "TFPREFIX",
				IAttributeService.AttributeMode.Read, false);

			prefix = tfPrefixAttr.Match(
				attr => attr.Last().Value.ToPlainText(),
				none => "FugueEdit > ",
				error => "FugueEdit > ");
		}

		string? attributePattern = null;
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objectSpec);
		AnyOptionalSharpObject target;

		if (split.TryPickT0(out var details, out _))
		{
			var (objectName, maybeAttributePattern) = details;
			attributePattern = maybeAttributePattern;

			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				enactor,
				objectName,
				LocateFlags.All);

			if (locate.IsValid())
			{
				target = locate.WithoutError();
			}
			else
			{
				return new None();
			}
		}
		else
		{
			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				enactor,
				objectSpec,
				LocateFlags.All);

			if (locate.IsValid())
			{
				target = locate.WithoutError();
			}
			else
			{
				return new None();
			}
		}

		if (target.IsNone())
		{
			return new None();
		}

		var targetKnown = target.Known();

		var canExamine = await PermissionService!.CanExamine(executor, targetKnown);
		if (!canExamine)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
			return new CallState(Errors.ErrorPerm);
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
			// @create command based on object type
			var createCmd = obj.Type.ToUpperInvariant() switch
			{
				"ROOM" => $"@dig {objectRef}",
				"EXIT" => $"@open {objectRef}",
				"THING" => $"@create {objectRef}",
				"PLAYER" => $"@pcreate {objectRef}",
				_ => $"@create {objectRef}"
			};
			outputs.Add($"{prefix}{createCmd}");

			var flags = await obj.Flags.Value.ToArrayAsync();
			foreach (var flag in flags)
			{
				if (skipDefaults && IsDefaultFlag(obj.Type, flag.Name))
				{
					continue;
				}
				outputs.Add($"{prefix}@set {objectRef}={flag.Name}");
			}

			var powers = await obj.Powers.Value.ToArrayAsync();
			foreach (var power in powers)
			{
				outputs.Add($"{prefix}@power {objectRef}={power.Name}");
			}

			foreach (var lockEntry in obj.Locks)
			{
				var lockName = lockEntry.Key;
				var lockData = lockEntry.Value;
				var lockValue = lockData.LockString;

				if (lockName.Equals("Basic", StringComparison.OrdinalIgnoreCase))
				{
					outputs.Add($"{prefix}@lock {objectRef}={lockValue}");
				}
				else
				{
					outputs.Add($"{prefix}@lock/{lockName} {objectRef}={lockValue}");
				}
			}

			// Set parent if not default (default is no parent)
			var parent = await obj.Parent.WithCancellation(CancellationToken.None);
			if (!parent.IsNone)
			{
				var parentObj = parent.Known.Object();
				outputs.Add($"{prefix}@parent {objectRef}={parentObj.DBRef}");
			}
		}

		if (showAttribs)
		{
			SharpAttributesOrError atrs;
			if (!string.IsNullOrEmpty(attributePattern))
			{
				atrs = await AttributeService!.GetAttributePatternAsync(
					enactor,
					targetKnown,
					attributePattern,
					false, // don't check parents for decompile
					IAttributeService.AttributePatternMode.Wildcard);
			}
			else
			{
				atrs = await AttributeService!.GetVisibleAttributesAsync(enactor, targetKnown);
			}

			if (atrs.IsAttribute)
			{
				foreach (var attr in atrs.AsAttributes)
				{
					// Skip VEILED attributes
					const string VeiledFlagName = "VEILED";
					if (attr.Flags.Any(f => f.Name.Equals(VeiledFlagName, StringComparison.OrdinalIgnoreCase)))
					{
						continue;
					}

					// Check if attribute contains ANSI color
					var hasAnsi = ContainsAnsiMarkup(attr.Value);

					if (hasAnsi)
					{
						// Use @set format with decomposed value to ensure evaluation
						var decomposedValue = DecomposeAttributeValue(attr.Value);
						outputs.Add($"{prefix}@set {objectRef}={attr.Name}:{decomposedValue}");
					}
					else
					{
						// Use &attr format for non-ANSI attributes
						var plainValue = attr.Value.ToPlainText();
						outputs.Add($"{prefix}&{attr.Name} {objectRef}={plainValue}");
					}

					// Output attribute flags if not in TF mode and not skipdefaults
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

		// Send output to player
		foreach (var output in outputs)
		{
			await NotifyService!.Notify(executor, output, executor);
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Checks if an MString contains ANSI markup
	/// </summary>
	private static bool ContainsAnsiMarkup(MString str)
	{
		var hasAnsi = false;
		MModule.evaluateWith((markupType, innerText) =>
		{
			if (markupType is { Value: Ansi })
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
	private static string DecomposeAttributeValue(MString input)
	{
		// Use same logic as decompose() function from StringFunctions
		var reconstructed = MModule.evaluateWith((markupType, innerText) =>
		{
			return markupType switch
			{
				{ Value: Ansi ansiMarkup }
					=> Functions.Functions.ReconstructAnsiCall(ansiMarkup.Details, innerText),
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
	private static bool IsDefaultFlag(string type, string flagName)
	{
		// Get default flags from configuration
		var defaultFlags = type.ToUpperInvariant() switch
		{
			"PLAYER" => Configuration?.CurrentValue.Flag.PlayerFlags ?? [],
			"ROOM" => Configuration?.CurrentValue.Flag.RoomFlags ?? [],
			"THING" => Configuration?.CurrentValue.Flag.ThingFlags ?? [],
			"EXIT" => Configuration?.CurrentValue.Flag.ExitFlags ?? [],
			_ => Array.Empty<string>()
		};

		// Check if the flag is in the default list (case-insensitive comparison)
		return defaultFlags.Any(f => f.Equals(flagName, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Checks if attribute flags are the default for that attribute
	/// </summary>
	private static async ValueTask<bool> AreDefaultAttrFlagsAsync(string attrName, IEnumerable<SharpAttributeFlag> flags)
	{
		// Query the attribute entry to check for custom default flags
		var entry = await Mediator!.Send(new GetAttributeEntryQuery(attrName.ToUpper()));

		if (entry == null)
		{
			// No entry means no custom defaults; empty flags are considered default
			return !flags.Any();
		}

		// Convert current flags to names for comparison
		var currentFlagNames = flags.Select(f => f.Name.ToUpper()).OrderBy(n => n).ToList();
		var defaultFlagNames = entry.DefaultFlags.Select(f => f.ToUpper()).OrderBy(n => n).ToList();

		// Compare flag lists
		return currentFlagNames.SequenceEqual(defaultFlagNames);
	}

	[SharpCommand(Name = "@EMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.RSNoParse | CB.NoGagged,
		MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["message"])]
	public static async ValueTask<Option<CallState>> Emit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var executorLocation = await executor.Where();
		var isSpoof = parser.CurrentState.Switches.Contains("SPOOF");
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MModule.empty());

		if (isSpoof)
		{
			var canSpoof = await executor.HasPower("CAN_SPOOF");
			var controlsExecutor = await PermissionService!.Controls(executor, enactor);

			if (!canSpoof && !controlsExecutor)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouDoNotHavePermissionToSpoofEmitsDetail));
				return new CallState(Errors.ErrorPerm);
			}
		}

		// Enforce Speech lock on the room (PennMUSH src/speech.c).
		if (!LockService!.Evaluate(LockType.Speech, executorLocation.WithExitOption(), executor))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MayNotSpeakHere));
			return CallState.Empty;
		}

		await CommunicationService!.SendToRoomAsync(
			executor,
			executorLocation,
			_ => message,
			INotifyService.NotificationType.Emit,
			sender: isSpoof ? enactor : executor);

		return new CallState(message);
	}

	[SharpCommand(Name = "@LISTMOTD", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> ListMessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		// Check if executor is wizard/royalty to see wizard MOTD
		var isWizard = await executor.IsWizard();

		// Get MOTD file paths from configuration
		var motdFile = Configuration!.CurrentValue.Message.MessageOfTheDayFile;
		var motdHtmlFile = Configuration.CurrentValue.Message.MessageOfTheDayHtmlFile;

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdCurrentSettingsHeader));
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdConnectFileFormat), motdFile ?? "(not set)");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdConnectHtmlFormat), motdHtmlFile ?? "(not set)");

		if (isWizard)
		{
			var wizmotdFile = Configuration.CurrentValue.Message.WizMessageOfTheDayFile;
			var wizmotdHtmlFile = Configuration.CurrentValue.Message.WizMessageOfTheDayHtmlFile;

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdWizardFileFormat), wizmotdFile ?? "(not set)");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdWizardHtmlFormat), wizmotdHtmlFile ?? "(not set)");
		}

		// Get temporary MOTD data from ExpandedServerData
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		if (motdData != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EmptyLine));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdTemporaryHeader));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdConnectMotdFormat), string.IsNullOrEmpty(motdData.ConnectMotd) ? "(not set)" : motdData.ConnectMotd);

			if (isWizard)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdWizardMotdFormat), string.IsNullOrEmpty(motdData.WizardMotd) ? "(not set)" : motdData.WizardMotd);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdDownMotdFormat), string.IsNullOrEmpty(motdData.DownMotd) ? "(not set)" : motdData.DownMotd);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListMotdFullMotdFormat), string.IsNullOrEmpty(motdData.FullMotd) ? "(not set)" : motdData.FullMotd);
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NSOEMIT", Switches = ["NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSNoParse, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["message"])]
	public static async ValueTask<Option<CallState>> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(Mediator!);
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(executor, obj, InteractType.Hear));

		var canSpoof = await executor.HasPower("CAN_SPOOF");
		var controlsExecutor = await PermissionService!.Controls(executor, enactor);

		if (!canSpoof && !controlsExecutor)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouDoNotHavePermissionToSpoofEmitsDetail));
			return new CallState(Errors.ErrorPerm);
		}

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				enactor,
				INotifyService.NotificationType.Emit);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "@OEMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public static async ValueTask<Option<CallState>> OmitEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail));
			return new CallState("#-1 Don't you have anything to say?");
		}

		var objects = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		// Support room/obj format like PennMUSH (e.g., @remit #123/obj1 obj2=message)
		// This allows emitting to a specific room while excluding specific objects.
		AnySharpContainer targetRoom;
		string objectsToExclude;

		// Check if format is "room/objects"
		if (objects.Contains('/'))
		{
			var parts = objects.Split('/', 2);
			var roomName = parts[0].Trim();
			objectsToExclude = parts[1].Trim();

			// Locate the target room
			var roomResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				roomName,
				LocateFlags.All);

			if (!roomResult.IsValid() || (!roomResult.IsRoom && !roomResult.IsThing))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidRoomSpecifiedDetail));
				return new CallState("#-1 INVALID ROOM");
			}

			targetRoom = roomResult.IsRoom
				? roomResult.WithoutError().WithoutNone().MinusExit()
				: await roomResult.WithoutError().WithoutNone().Where();
		}
		else
		{
			// Original behavior: emit to executor's location
			targetRoom = await executor.Where();
			objectsToExclude = objects;
		}

		var objectList = ArgHelpers.NameList(objectsToExclude);
		var excludeObjects = new List<AnySharpObject>();

		// Resolve all objects to exclude
		_ = await objectList
			.ToAsyncEnumerable()
			.Select(obj => obj.IsT0 ? obj.AsT0.ToString() : obj.AsT1)
			.Select(objName =>
				LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser,
					executor,
					executor,
					objName,
					LocateFlags.All,
					target =>
					{
						excludeObjects.Add(target);
						return CallState.Empty;
					}))
			.ToArrayAsync();

		await CommunicationService!.SendToRoomAsync(
			executor,
			targetRoom,
			_ => message,
			INotifyService.NotificationType.Emit,
			excludeObjects: excludeObjects);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@REMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["room", "message"])]
	public static async ValueTask<Option<CallState>> RoomEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail));
			return new CallState("#-1 Don't you have anything to say?");
		}

		var objects = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		// Send message to contents of all specified objects
		var objectList = ArgHelpers.NameListString(objects);

		foreach (var obj in objectList)
		{
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				obj,
				LocateFlags.All,
				async target =>
				{
					if (!target.IsContainer)
					{
						return CallState.Empty;
					}

					await CommunicationService!.SendToRoomAsync(
						executor,
						target.AsContainer,
						_ => message,
						INotifyService.NotificationType.Emit);

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@STATS", Switches = ["CHUNKS", "FREESPACE", "PAGING", "REGIONS", "TABLES", "FLAGS"],
		Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["player"])]
	public static async ValueTask<Option<CallState>> Stats(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		// Check for specialized switches
		if (switches.Contains("TABLES"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsTablesNotImplemented));
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		if (switches.Contains("FLAGS"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsFlagsNotImplemented));
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		if (switches.Contains("CHUNKS") || switches.Contains("FREESPACE") ||
				switches.Contains("PAGING") || switches.Contains("REGIONS"))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsMemorySwitchesNotImplemented));
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		// Basic @stats - show object counts
		string? playerName = null;
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			playerName = args["0"].Message?.ToPlainText();
		}

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsDatabaseStatisticsHeader));

		if (playerName != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsForPlayerFormat), playerName);
		}

		// Query actual database statistics
		var allObjects = await Mediator!.CreateStream(new GetAllObjectsQuery()).ToListAsync();
		var roomCount = allObjects.Count(o => o.Type == "ROOM");
		var exitCount = allObjects.Count(o => o.Type == "EXIT");
		var thingCount = allObjects.Count(o => o.Type == "THING");
		var playerCount = allObjects.Count(o => o.Type == "PLAYER");
		var totalCount = roomCount + exitCount + thingCount + playerCount;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsRoomsFormat), roomCount);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsExitsFormat), exitCount);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsThingsFormat), thingCount);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsPlayersFormat), playerCount);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.StatsTotalFormat), totalCount);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@VERB", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["object", "verb", "target"])]
	public static async ValueTask<Option<CallState>> Verb(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var args = parser.CurrentState.ArgumentsOrdered;

		if (args.Count < 2)
		{
			await NotifyService!.Notify(executor,
				"Usage: @verb <victim>=<actor>,<what>,<whatd>,<owhat>,<owhatd>,<awhat>[,<args>]", executor);
			return new CallState(Errors.ErrorCantSeeThat);
		}

		var victimName = args.ElementAtOrDefault(0).Value.Message!.ToPlainText();
		var actorName = args.ElementAtOrDefault(1).Value.Message!.ToPlainText();
		var what = args.ElementAtOrDefault(2).Value.Message!.ToPlainText();
		var whatd = args.ElementAtOrDefault(3).Value.Message!.ToPlainText();
		var owhat = args.ElementAtOrDefault(4).Value.Message!.ToPlainText();
		var owhatd = args.ElementAtOrDefault(5).Value.Message!.ToPlainText();
		var awhat = args.ElementAtOrDefault(6).Value.Message!.ToPlainText();

		const int RequiredArgsBeforeStack = 7;
		var stackArgs = args.Skip(RequiredArgsBeforeStack)
			.Select((kvp, idx) => new KeyValuePair<string, CallState>(idx.ToString(), kvp.Value))
			.ToDictionary();

		// Locate victim
		var maybeVictim = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, victimName, LocateFlags.All);

		if (maybeVictim.IsError)
		{
			await NotifyService!.Notify(executor, maybeVictim.AsError.Message!, executor);
			return maybeVictim.AsError;
		}

		var victim = maybeVictim.AsSharpObject;

		// Locate actor
		var maybeActor = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, actorName, LocateFlags.All);

		if (maybeActor.IsError)
		{
			await NotifyService!.Notify(executor, maybeActor.AsError.Message!, executor);
			return maybeActor.AsError;
		}

		var actor = maybeActor.AsSharpObject;

		var isWizard = await executor.IsWizard();
		var controlsBoth = await PermissionService!.Controls(executor, actor) &&
											 await PermissionService!.Controls(executor, victim);
		var enactorIsActor = enactor.Object().DBRef == actor.Object().DBRef;
		var executorPrivileged = await executor.IsRoyalty();
		var executorControlsVictim = await PermissionService!.Controls(executor, victim);

		var hasPermission = isWizard || controlsBoth ||
												(enactorIsActor && (executorPrivileged || executorControlsVictim));

		if (!hasPermission)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
			return new CallState(Errors.ErrorPerm);
		}

		var actorMessage = await GetAttributeOrDefault(
			parser, AttributeService!, executor, victim, actor, what, whatd, stackArgs);

		await NotifyService!.Notify(actor, actorMessage);

		var actorLocation = await actor.Where();
		var othersMessage = await GetAttributeOrDefault(
			parser, AttributeService!, executor, victim, actor, owhat, owhatd, stackArgs);

		var prependedMessage = MModule.single($"{actor.Object().Name} {othersMessage.ToPlainText()}");

		await CommunicationService!.SendToRoomAsync(
			actor, actorLocation, _ => prependedMessage,
			INotifyService.NotificationType.Emit,
			excludeObjects: [actor]);

		if (!string.IsNullOrWhiteSpace(awhat))
		{
			var maybeAwhatAttr = await AttributeService!.GetAttributeAsync(
				executor, victim, awhat, IAttributeService.AttributeMode.Execute);

			if (!maybeAwhatAttr.IsError)
			{
				var attribute = maybeAwhatAttr.AsAttribute.Last();
				await parser.With(
					state => state with
					{
						Executor = victim.Object().DBRef,
						Enactor = actor.Object().DBRef,
						Caller = state.Executor,
						Arguments = stackArgs
					},
					newParser => newParser.WithAttributeDebug(attribute,
						p => p.CommandListParse(attribute.Value)));
			}
		}

		return CallState.Empty;
	}

	private static async ValueTask<MString> GetAttributeOrDefault(
		IMUSHCodeParser parser,
		IAttributeService attributeService,
		AnySharpObject executor,
		AnySharpObject victim,
		AnySharpObject actor,
		string attrName,
		string defaultValue,
		Dictionary<string, CallState> stackArgs)
	{
		if (string.IsNullOrWhiteSpace(attrName))
		{
			return MModule.single(defaultValue);
		}

		var maybeAttr = await attributeService.GetAttributeAsync(
			executor, victim, attrName, IAttributeService.AttributeMode.Execute);

		if (maybeAttr.IsError || maybeAttr.IsNone)
		{
			return MModule.single(defaultValue);
		}

		var result = await parser.With(
			state => state with
			{
				Executor = victim.Object().DBRef,
				Enactor = actor.Object().DBRef,
				Caller = state.Executor,
				Arguments = stackArgs
			},
			newParser => attributeService.EvaluateAttributeFunctionAsync(
				newParser, victim, victim, attrName, stackArgs));

		return result ?? MModule.single(defaultValue);
	}

	[SharpCommand(Name = "@ENTRANCES", Switches = ["EXITS", "THINGS", "PLAYERS", "ROOMS"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 3, ParameterNames = ["object", "flags"])]
	public static async ValueTask<Option<CallState>> Entrances(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		// @entrances[/<switch>] [<object>][=<begin>[, <end>]]
		// Shows all objects linked to <object>

		AnySharpObject targetObject;

		// Get target object (defaults to current location if not specified)
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			var targetName = args["0"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(targetName))
			{
				var maybeTarget = await LocateService!.LocateAndNotifyIfInvalid(
					parser, executor, executor, targetName, LocateFlags.All);

				if (!maybeTarget.IsValid())
				{
					return new CallState("#-1 NOT FOUND");
				}

				targetObject = maybeTarget.WithoutError().WithoutNone();
			}
			else
			{
				var location = await executor.AsContent.Location();
				targetObject = location.WithRoomOption();
			}
		}
		else
		{
			var location = await executor.AsContent.Location();
			targetObject = location.WithRoomOption();
		}

		// Parse range if specified
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
		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesToFormat), targetObj.Name);

		// Filter by switch type
		var filterTypes = new List<string>();
		if (switches.Contains("EXITS")) filterTypes.Add("exits");
		if (switches.Contains("THINGS")) filterTypes.Add("things");
		if (switches.Contains("PLAYERS")) filterTypes.Add("players");
		if (switches.Contains("ROOMS")) filterTypes.Add("rooms");

		if (filterTypes.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesFilteringForFormat), string.Join(", ", filterTypes));
		}

		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesRangeFormat), beginDbref ?? 0, endDbref?.ToString() ?? "end");
		}

		// Query database for exits linked to target
		var entrances = await Mediator!.CreateStream(new GetEntrancesQuery(targetObj.DBRef)).ToListAsync();

		// Apply type filters if specified
		if (filterTypes.Count > 0 && !filterTypes.Contains("exits"))
		{
			entrances.Clear(); // GetEntrancesQuery only returns exits, so if exits not requested, clear
		}

		// Apply range filters if specified
		if (beginDbref.HasValue || endDbref.HasValue)
		{
			entrances = entrances.Where(e =>
			{
				var key = e.Object.Key;
				return (!beginDbref.HasValue || key >= beginDbref.Value) &&
							 (!endDbref.HasValue || key <= endDbref.Value);
			}).ToList();
		}

		// Display results
		if (entrances.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesZeroFound));
		}
		else
		{
			foreach (var entrance in entrances)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesObjectEntryFormat), entrance.Object.Key, entrance.Object.Name);
			}
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesCountFormat), entrances.Count);
		}

		return new CallState(entrances.Count.ToString());
	}

	[SharpCommand(Name = "@GREP", Switches = ["LIST", "PRINT", "ILIST", "IPRINT", "REGEXP", "WILD", "NOCASE", "PARENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "pattern"])]
	public static async ValueTask<Option<CallState>> Grep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (!args.TryGetValue("0", out var objAttrArg) || !args.TryGetValue("1", out var patternArg))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidArguments));
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		// Parse object/attribute pattern
		var objAttrText = MModule.plainText(objAttrArg.Message!);
		var pattern = MModule.plainText(patternArg.Message!);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText);

		if (!split.TryPickT0(out var details, out _))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere));
			return new CallState("#-1 INVALID OBJECT");
		}

		var (dbref, maybeAttributePattern) = details;
		var attributePattern = string.IsNullOrEmpty(maybeAttributePattern) ? "*" : maybeAttributePattern;

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

		// Determine pattern matching mode for attribute values
		var isWild = switches.Contains("WILD");
		var isRegexp = switches.Contains("REGEXP");
		var isNoCase = switches.Contains("NOCASE") || switches.Contains("ILIST") || switches.Contains("IPRINT");
		var isPrint = switches.Contains("PRINT") || switches.Contains("IPRINT");
		var checkParents = switches.Contains("PARENT");

		// Get attributes matching the attribute pattern
		var attributePatternMode = attributePattern == "**"
			? IAttributeService.AttributePatternMode.Wildcard
			: IAttributeService.AttributePatternMode.Wildcard;

		var attributes = await AttributeService!.GetAttributePatternAsync(
			executor,
			targetObject,
			attributePattern,
			checkParents,
			attributePatternMode);

		if (attributes.IsError)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepErrorReadingAttributesFormat), attributes.AsError.Value);
			return new CallState($"#-1 {attributes.AsError.Value}");
		}

		// Filter attributes by pattern match in their values
		var matchingAttributes = new List<SharpAttribute>();

		foreach (var attr in attributes.AsAttributes)
		{
			var attrValue = MModule.plainText(attr.Value);
			bool matches = false;

			if (isRegexp)
			{
				// Regex match
				try
				{
					var regexOptions = isNoCase ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None;
					matches = System.Text.RegularExpressions.Regex.IsMatch(attrValue, pattern, regexOptions, TimeSpan.FromSeconds(1));
				}
				catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepRegexpTimedOutFormat), pattern);
					return new CallState("#-1 REGEXP TIMEOUT");
				}
				catch
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidRegexpFormat), pattern);
					return new CallState("#-1 INVALID REGEXP");
				}
			}
			else if (isWild)
			{
				// Wildcard match
				try
				{
					var regexPattern = MModule.getWildcardMatchAsRegex2(pattern);
					var regexOptions = isNoCase ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None;
					matches = System.Text.RegularExpressions.Regex.IsMatch(attrValue, regexPattern, regexOptions, TimeSpan.FromSeconds(1));
				}
				catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepWildcardTimedOutFormat), pattern);
					return new CallState("#-1 PATTERN TIMEOUT");
				}
				catch
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidWildcardFormat), pattern);
					return new CallState("#-1 INVALID PATTERN");
				}
			}
			else
			{
				// Substring match
				var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
				matches = attrValue.Contains(pattern, comparison);
			}

			if (matches)
			{
				matchingAttributes.Add(attr);
			}
		}

		// Display results
		if (matchingAttributes.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound));
			return new CallState(string.Empty);
		}

		if (isPrint)
		{
			// Print attribute names and values with highlighting
			foreach (var attr in matchingAttributes)
			{
				var attrValue = attr.Value;

				// Highlight matching substrings in the value
				MString displayValue;
				if (isRegexp || isWild)
				{
					// For regex/wildcard, just show the value as-is
					displayValue = attr.Value;
				}
				else
				{
					// For substring match, highlight the matching parts using Span to avoid allocations
					var plainValue = MModule.plainText(attr.Value);
					var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
					var index = plainValue.IndexOf(pattern, comparison);

					if (index >= 0)
					{
						var valueSpan = plainValue.AsSpan();
						var before = valueSpan.Slice(0, index).ToString();
						var match = valueSpan.Slice(index, pattern.Length).ToString();
						var after = valueSpan.Slice(index + pattern.Length).ToString();

						displayValue = MModule.concat(
							MModule.concat(
								MModule.single(before),
								MModule.single(match).Hilight()
							),
							MModule.single(after)
						);
					}
					else
					{
						displayValue = attr.Value;
					}
				}

				await NotifyService!.Notify(executor,
					MModule.concat(
						MModule.single($"{attr.Name}: ").Hilight(),
						displayValue), executor);
			}
		}
		else
		{
			// List mode - just show attribute names
			var attrNames = string.Join(" ", matchingAttributes.Select(a => a.Name));
			await NotifyService!.Notify(executor, attrNames, executor);
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@INCLUDE", Switches = ["LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 1, MaxArgs = 31, ParameterNames = ["file"])]
	public static async ValueTask<Option<CallState>> Include(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @include[/<switches>] <object>/<attribute>[=<arg1>,<arg2>,...]
		// Inserts attribute contents in-place without adding a new queue entry

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeMustSpecifyAttributePath));
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}

		// Parse object/attribute
		var parts = attributePath.Split('/', 2);
		if (parts.Length < 2)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeMustSpecifyObjectAttributePath));
			return new CallState("#-1 INVALID PATH");
		}

		var objectName = parts[0];
		var attributeName = parts[1];

		// Locate the target object
		var maybeObject = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, enactor, objectName, LocateFlags.All);

		if (maybeObject.IsError)
		{
			return maybeObject.AsError;
		}

		var targetObject = maybeObject.AsSharpObject;

		// Get the attribute - must be visible to enactor
		var attributeResult = await AttributeService!.GetAttributeAsync(
			enactor, targetObject, attributeName, IAttributeService.AttributeMode.Read, false);

		if (attributeResult.IsError)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeNoSuchAttributeFormat), attributeName);
			return new CallState("#-1 NO SUCH ATTRIBUTE");
		}

		if (attributeResult.IsNone)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeAttributeIsEmptyFormat), attributeName);
			return CallState.Empty;
		}

		var attribute = attributeResult.AsAttribute.Last();
		var attributeText = attribute.Value.ToPlainText();
		var attributeLongName = attribute.LongName!.ToUpper();

		// Strip ^...: or $...: prefixes for listen/command patterns using Span
		if (attributeText.StartsWith("^") || attributeText.StartsWith("$"))
		{
			var colonIndex = attributeText.IndexOf(':');
			if (colonIndex > 0)
			{
				attributeText = attributeText.AsSpan(colonIndex + 1).TrimStart().ToString();
			}
		}

		// Build EnvironmentRegisters from provided arguments so %0, %1, ... are substituted.
		// args["0"] is the attribute path; args["1"], args["2"], ... map to %0, %1, ...
		var envArgs = new Dictionary<string, CallState>(parser.CurrentState.EnvironmentRegisters);
		for (var i = 1; i < args.Count; i++)
		{
			if (args.TryGetValue(i.ToString(), out var argVal) && argVal.Message != null)
			{
				envArgs[(i - 1).ToString()] = argVal;
			}
		}

		// Implement /localize: save Q-registers so the included code cannot permanently change
		// the caller's Q-registers. /clearregs: start the included code with empty Q-registers.
		// NOTE: Save must happen before Clear (both use a single TryPeek for safety).
		var hasClearRegs = switches.Contains("CLEARREGS");
		var hasLocalize = switches.Contains("LOCALIZE");

		Dictionary<string, MString>? savedRegisters = null;
		if ((hasLocalize || hasClearRegs) && parser.CurrentState.Registers.TryPeek(out var includeTopRegs))
		{
			if (hasLocalize)
			{
				savedRegisters = new Dictionary<string, MString>(includeTopRegs);
			}

			if (hasClearRegs)
			{
				includeTopRegs.Clear();
			}
		}

		// Execute the attribute content in-place with recursion tracking and DEBUG/VERBOSE support
		// This evaluates the command list without creating a queue entry
		try
		{
			var hasNoBreak = switches.Contains("NOBREAK");

			var result = await ExecuteAttributeWithTracking(parser, attributeLongName, async () =>
			{
				var execResult = await parser.With(
					state => state with { EnvironmentRegisters = envArgs, Caller = state.Executor },
					p => p.WithAttributeDebug(attribute, pp => pp.CommandListParse(MModule.single(attributeText))));

				// Handle NOBREAK switch to prevent @break/@assert propagation.
				// When set, @break/@assert from included code shouldn't propagate to calling list.
				if (hasNoBreak && parser.CurrentState.ExecutionStack.TryPeek(out var execution) && execution.CommandListBreak)
				{
					// Clear the break flag so it doesn't propagate to the calling context
					parser.CurrentState.ExecutionStack.TryPop(out _);
				}

				return execResult ?? CallState.Empty;
			});

			return result;
		}
		catch (Exception ex)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeErrorExecutingFormat), ex.Message);
			return new CallState($"#-1 ERROR: {ex.Message}");
		}
		finally
		{
			// Restore Q-registers if /localize was set
			if (hasLocalize && savedRegisters != null && parser.CurrentState.Registers.TryPeek(out var regsToRestore))
			{
				regsToRestore.Clear();
				foreach (var (key, value) in savedRegisters)
				{
					regsToRestore[key] = value;
				}
			}
		}
	}

	[SharpCommand(Name = "@MAIL",
		Switches =
		[
			"NOEVAL", "NOSIG", "STATS", "CSTATS", "DSTATS", "FSTATS", "DEBUG", "NUKE", "FOLDERS", "UNFOLDER", "LIST", "READ",
			"UNREAD", "CLEAR", "UNCLEAR", "STATUS", "PURGE", "FILE", "TAG", "UNTAG", "FWD", "FORWARD", "SEND", "SILENT",
			"URGENT", "REVIEW", "RETRACT"
		], Behavior = CB.Default | CB.EqSplit | CB.NoParse, MinArgs = 0, MaxArgs = 2, ParameterNames = ["player", "subject"])]
	public static async ValueTask<Option<CallState>> Mail(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		MString? arg0, arg1;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var caller = (await parser.CurrentState.CallerObject(Mediator!)).Known();
		string[] sendSwitches = ["SEND", "URGENT", "NOSIG", "SILENT", "NOEVAL"];

		if (switches.Except(sendSwitches).Any() && switches.Length > 1)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MailTooManySwitches));
			return new CallState(Errors.ErrorTooManySwitches);
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
			[.., "FOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, switches),
			[.., "UNFOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, switches),
			[.., "FILE"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, switches),
			[.., "CLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "CLEAR"),
			[.., "UNCLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "UNCLEAR"),
			[.., "TAG"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "TAG"),
			[.., "UNTAG"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "UNTAG"),
			[.., "UNREAD"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "UNREAD"),
			[.., "STATUS"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "STATUS"),
			[.., "CSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService!, LocateService!,
				Mediator!, NotifyService!, arg0, switches),
			[.., "STATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService!, LocateService!,
				Mediator!, NotifyService!, arg0, switches),
			[.., "DSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService!, LocateService!,
				Mediator!, NotifyService!, arg0, switches),
			[.., "FSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService!, LocateService!,
				Mediator!, NotifyService!, arg0, switches),
			[.., "DEBUG"] => await AdminMail.Handle(parser, Mediator!, NotifyService!, switches),
			[.., "NUKE"] => await AdminMail.Handle(parser, Mediator!, NotifyService!, switches),
			[.., "REVIEW"] when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await ReviewMail.Handle(parser, LocateService!, ObjectDataService!, Mediator!, NotifyService!, arg0, arg1,
					switches),
			[.., "RETRACT"] when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await RetractMail.Handle(parser, ObjectDataService!, LocateService!, Mediator!, NotifyService!,
					arg0!.ToPlainText(), arg1!.ToPlainText()),
			[.., "FWD"] when executor.IsPlayer && int.TryParse(arg0?.ToPlainText(), out var number) &&
											 (arg1?.Length ?? 0) != 0
				=> await ForwardMail.Handle(parser, ObjectDataService!, LocateService!, PermissionService!, Mediator!, number,
					arg1!.ToPlainText()),
			[.., "SEND"] or [.., "URGENT"] or [.., "SILENT"] or [.., "NOSIG"] or []
				when arg0?.Length != 0 && arg1?.Length != 0
				=> await SendMail.Handle(parser, PermissionService!, ObjectDataService!, Mediator!, NotifyService!, AttributeService!, Configuration!, arg0!,
					arg1!, switches),
			[.., "READ"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0 &&
															int.TryParse(arg0?.ToPlainText(), out var number)
				=> await ReadMail.Handle(parser, ObjectDataService!, Mediator!, NotifyService!, Math.Max(0, number - 1),
					switches),
			[.., "LIST"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0
				=> await ListMail.Handle(parser, ObjectDataService!, Mediator!, NotifyService!, arg0, arg1, switches),
			_ => MModule.single("#-1 BAD ARGUMENTS TO MAIL COMMAND")
		};

		return new CallState(response);
	}

	[SharpCommand(Name = "@NSPEMIT", Switches = ["LIST", "SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["target", "message"])]
	public static async ValueTask<Option<CallState>> NoSpoofPrivateEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail));
			return new CallState("#-1 Don't you have anything to say?");
		}

		var recipients = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		// Determine notification type based on nospoof permissions
		var notificationType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSAnnounce
			: INotifyService.NotificationType.Announce;

		// Check if first argument is an integer list (port list)
		if (IsIntegerList(recipients))
		{
			// Handle port-based messaging using CommunicationService
			var ports = recipients.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(long.Parse)
				.ToArray();

			await CommunicationService!.SendToPortsAsync(executor, ports, _ => message, notificationType);
			return CallState.Empty;
		}

		// Handle object/player-based messaging
		var recipientList = ArgHelpers.NameList(recipients);

		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString() : recipient.AsT1;

			// Use LocateAndNotifyIfInvalidWithCallStateFunction for proper error handling
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService.CanInteract(executor, target, InteractType.Hear))
					{
						await NotifyService!.Notify(target, message, executor, notificationType);
					}

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	private static bool IsIntegerList(string input)
	{
		if (string.IsNullOrWhiteSpace(input)) return false;

		var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return tokens.Length > 0 && tokens.All(token => long.TryParse(token, out _));
	}

	[SharpCommand(Name = "@PASSWORD", Switches = [],
		Behavior = CB.Player | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGuest, MinArgs = 0, MaxArgs = 0, ParameterNames = ["old", "new"])]
	public static async ValueTask<Option<CallState>> Password(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var oldPassword = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var newPassword = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (!executor.IsPlayer)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PasswordOnlyPlayersHavePasswords));
			return new CallState("#-1 INVALID OBJECT TYPE.");
		}

		var isValidPassword = PasswordService!.PasswordIsValid(executor.Object().DBRef.ToString(), oldPassword,
			executor.AsPlayer.PasswordHash);
		if (!isValidPassword)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PasswordInvalid));
			return new CallState("#-1 INVALID PASSWORD.");
		}

		var hashedPassword = PasswordService.HashPassword(executor.Object().DBRef.ToString(), newPassword);
		await PasswordService.SetPassword(executor.AsPlayer, hashedPassword);

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@RESTART", Switches = ["ALL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Restart(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();

		// Check for /all switch - wizard only
		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			// Halt all objects, then trigger @STARTUP on all objects that have it
			await foreach (var obj in Mediator!.CreateStream(new GetAllObjectsQuery()))
			{
				// Halt the object's queue
				await Mediator.Send(new HaltObjectQueueRequest(obj.DBRef));

				// Trigger @STARTUP attribute if it exists (non-inherited)
				try
				{
					var objNode = await Mediator.Send(new GetObjectNodeQuery(obj.DBRef));
					if (!objNode.IsNone)
					{
						await AttributeService!.EvaluateAttributeFunctionAsync(
							parser, executor, objNode.Known, "STARTUP",
							new Dictionary<string, CallState>(),
							evalParent: false);
					}
				}
				catch
				{
					// Ignore errors from @STARTUP - they're non-fatal
				}
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllObjectsRestarted));
			return CallState.Empty;
		}

		// Get target object
		if (args.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartMustSpecifyObject));
			return new CallState("#-1 NO OBJECT SPECIFIED");
		}

		var targetName = args["0"].Message!.ToPlainText();

		// Locate the target object
		var maybeTarget = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (!maybeTarget.IsValid())
		{
			return new CallState("#-1 NOT FOUND");
		}

		var target = maybeTarget.WithoutError().WithoutNone();

		// Check control permissions
		if (!await PermissionService!.Controls(executor, target))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
			return new CallState(Errors.ErrorPerm);
		}

		var targetObject = target.Object();

		// Halt the object's queue first
		await Mediator!.Send(new HaltObjectQueueRequest(targetObject.DBRef));

		// For players, restart all owned objects too
		if (target.IsPlayer)
		{
			// Halt and restart all objects owned by the player
			await foreach (var obj in Mediator.CreateStream(new GetAllObjectsQuery()))
			{
				var owner = await obj.Owner.WithCancellation(CancellationToken.None);
				if (owner.Object.DBRef == targetObject.DBRef)
				{
					await Mediator.Send(new HaltObjectQueueRequest(obj.DBRef));

					// Trigger @STARTUP if it exists
					try
					{
						var objNode = await Mediator.Send(new GetObjectNodeQuery(obj.DBRef));
						if (!objNode.IsNone)
						{
							await AttributeService!.EvaluateAttributeFunctionAsync(
								parser, executor, objNode.Known, "STARTUP",
								new Dictionary<string, CallState>(),
								evalParent: false);
						}
					}
					catch
					{
						// Ignore @STARTUP errors - they're non-fatal
					}
				}
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartedPlayerAndObjectsFormat), targetObject.Name);
		}
		else
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RestartedObjectFormat), targetObject.Name);
		}

		// Trigger @STARTUP attribute if it exists (never inherited per PennMUSH spec)
		try
		{
			await AttributeService!.EvaluateAttributeFunctionAsync(
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
	public static async ValueTask<Option<CallState>> Sweep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Parse switches
		var switches = parser.CurrentState.Switches.ToHashSet();
		var connectFlag = switches.Contains("CONNECTED");
		var hereFlag = switches.Contains("HERE");
		var inventoryFlag = switches.Contains("INVENTORY");
		var exitsFlag = switches.Contains("EXITS");

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var location = await executor.Where();
		var locationObj = location.Object();
		var locationAnyObject = location.WithRoomOption();
		var locationOwner = await locationObj.Owner.WithCancellation(CancellationToken.None);

		// ROOM sweep
		if (!inventoryFlag && !exitsFlag)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningInRoom));

			if (connectFlag)
			{
				if (await IsConnectedOrPuppetConnected(locationAnyObject))
				{
					if (location.IsPlayer)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), locationObj.Name);
					}
					else
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), locationObj.Name, locationOwner.Object.Name);
					}
				}
			}
			else
			{
				if (await locationAnyObject.IsHearer(ConnectionService!, AttributeService!) ||
						await locationAnyObject.IsListener())
				{
					if (await ConnectionService!.IsConnected(locationAnyObject))
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomSpeechConnectedFormat), locationObj.Name);
					else
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomSpeechFormat), locationObj.Name);
				}

				if (await locationAnyObject.HasActiveCommands(AttributeService!))
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomCommandsFormat), locationObj.Name);
				if (await locationAnyObject.IsAudible())
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomBroadcastingFormat), locationObj.Name);
			}

			// Contents of the room
			var contents = location.Content(Mediator!);
			await foreach (var obj in contents)
			{
				var fullObj = obj.WithRoomOption();
				var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (connectFlag)
				{
					if (await IsConnectedOrPuppetConnected(fullObj))
					{
						if (obj.IsPlayer)
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), obj.Object().Name);
						}
						else
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), obj.Object().Name, objOwner.Object.Name);
						}
					}
				}
				else
				{
					if (await fullObj.IsHearer(ConnectionService!, AttributeService!) || await fullObj.IsListener())
					{
						if (await ConnectionService!.IsConnected(fullObj))
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechConnectedFormat), obj.Object().Name);
						else
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechFormat), obj.Object().Name);
					}

					if (await fullObj.HasActiveCommands(AttributeService!))
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectCommandsFormat), obj.Object().Name);
				}
			}
		}

		// EXITS sweep (only if not connectFlag and not inventoryFlag and location is a room)
		if (!connectFlag && !inventoryFlag && location.IsRoom && exitsFlag)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningExits));
			if (await locationAnyObject.IsAudible())
			{
				var exits = (location.Content(Mediator!)).Where(x => x.IsExit);
				await foreach (var exit in exits)
				{
					if (await exit.WithRoomOption().IsAudible())
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepExitBroadcastingFormat), exit.Object().Name);
					}
				}
			}
		}

		// INVENTORY sweep (if not hereFlag and not exitFlag)
		if (!hereFlag && !exitsFlag && inventoryFlag)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningInInventory));
			await foreach (var obj in executor.AsContainer.Content(Mediator!))
			{
				var fullObj = obj.WithRoomOption();
				var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (connectFlag)
				{
					if (await IsConnectedOrPuppetConnected(fullObj))
					{
						if (obj.IsPlayer)
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), obj.Object().Name);
						}
						else
						{
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), obj.Object().Name, objOwner.Object.Name);
						}
					}
				}
				else
				{
					if (await fullObj.IsHearer(ConnectionService!, AttributeService!) || await fullObj.IsListener())
					{
						if (await ConnectionService!.IsConnected(fullObj))
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechConnectedFormat), obj.Object().Name);
						else
							await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectSpeechFormat), obj.Object().Name);
					}

					if (await fullObj.HasActiveCommands(AttributeService!))
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectCommandsFormat), obj.Object().Name);
				}
			}
		}

		return CallState.Empty;

		static async Task<bool> IsConnectedOrPuppetConnected(AnySharpObject obj)
		{
			if (await ConnectionService!.IsConnected(obj)) return true;

			return await obj.IsPuppet()
						 && await ConnectionService!.IsConnected(await obj.Object().Owner.WithCancellation(CancellationToken.None));
		}
	}

	[SharpCommand(Name = "@VERSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Version(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var uptimeData = await ObjectDataService!.GetExpandedServerDataAsync<UptimeData>();

		var lines = new List<MString>
		{
			MModule.concat(MModule.single("You are connected to "),
				MModule.single(Configuration!.CurrentValue.Net.MudName ?? "Unknown")),
			MModule.concat(MModule.single("Address: "), MModule.single(Configuration.CurrentValue.Net.MudUrl ?? "Unknown")),
			MModule.single("SharpMUSH version 0")
		};

		if (uptimeData != null)
		{
			lines.Add(MModule.single($"Last restarted: {uptimeData.LastRebootTime:ddd MMM dd HH:mm:ss yyyy}"));
		}

		var result = MModule.multipleWithDelimiter(MModule.single("\n"), lines.ToArray());

		await NotifyService!.Notify(executor, result, executor);

		return new CallState(result);
	}

	[SharpCommand(Name = "@RETRY", Switches = [],
		Behavior = CB.Default | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Retry(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (!args.TryGetValue("0", out var predicate))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RetryUsage));
			return new CallState("#-1 RETRY: NO CONDITION PROVIDED.");
		}

		var commandHistory = parser.CurrentState.CommandHistory;
		if (commandHistory == null || commandHistory.Count < 2)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RetryNothingToRetry));
			return new CallState("#-1 RETRY: NO COMMAND TO RETRY.");
		}

		// History top = @retry itself; entry below it = the command to re-run.
		var historyArray = commandHistory.ToArray();
		var (previousCommandInvoker, _) = historyArray[1];

		// Use the parent state's arguments as the initial evaluation context (%0, %1, …).
		var parentArgs = parser.State.Skip(1).FirstOrDefault()?.Arguments
			?? new Dictionary<string, CallState>();

		// Retry arg texts (everything after the condition, i.e. args["1"], args["2"], …).
		var retryArgTexts = args
			.Where(kvp => kvp.Key != "0")
			.OrderBy(kvp => int.Parse(kvp.Key))
			.Select(kvp => kvp.Value.Message!)
			.ToList();

		var conditionText = predicate.Message!;
		var currentArgs = parentArgs;
		var limit = 1000;

		while (limit > 0)
		{
			// Evaluate condition in the current argument context.
			var condResult = await parser.With(
				state => state with { Arguments = currentArgs },
				innerParser => innerParser.FunctionParse(conditionText));

			if (!(condResult?.Message.Truthy() ?? false))
				break;

			// Evaluate each retry arg in the current context to produce the new %0, %1, …
			var newArgValues = new Dictionary<string, CallState>();
			for (var i = 0; i < retryArgTexts.Count; i++)
			{
				var capturedIndex = i;
				var capturedText = retryArgTexts[i];
				var evaluated = await parser.With(
					state => state with { Arguments = currentArgs },
					innerParser => innerParser.FunctionParse(capturedText));
				newArgValues[capturedIndex.ToString()] = evaluated!;
			}

			// Execute the previous command with the freshly evaluated arguments.
			await parser.With(
				state => state with { Arguments = newArgValues },
				async innerParser => await previousCommandInvoker(innerParser));

			// The new arg values become the context for the next condition check.
			currentArgs = newArgValues;
			limit--;
		}

		return new CallState(1000 - limit);
	}

	[SharpCommand(Name = "@ASSERT", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 2, ParameterNames = ["condition"])]
	public static async ValueTask<Option<CallState>> Assert(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var nargs = args.Count;

		// Note: INLINE is default behavior (immediate execution)
		// QUEUED switch queues the command for later execution via task scheduler
		var useQueue = switches.Contains("QUEUED");

		switch (nargs)
		{
			case 0:
				// No condition provided — treat as falsy (@assert 0 = assertion fails = break).
				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				break;
			case 1:
				if (args["0"].Message.Falsy())
				{
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}

				return args["0"];
			case 2 when args["0"].Message.Falsy():
				var command = await args["1"].ParsedMessage();

				if (useQueue)
				{
					// Queue the command for later execution
					var executor = parser.CurrentState.Executor ?? throw new InvalidOperationException("Executor cannot be null");
					await Mediator!.Send(new QueueCommandListRequest(
						command!,
						parser.CurrentState,
						new DbRefAttribute(executor, ["ASSERT"]),
						-1));
				}
				else
				{
					// Execute inline (default)
					var commandList = parser.CommandListParseVisitor(command!);
					await commandList();
				}

				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));

				return args["0"];
			case 2:
				return args["0"];
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@ATTRIBUTE",
		Switches = ["ACCESS", "DELETE", "RENAME", "RETROACTIVE", "LIMIT", "ENUM", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2, ParameterNames = ["attribute", "options..."])]
	public static async ValueTask<Option<CallState>> Attribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @attribute <attrib> - Display attribute information
		// @attribute/access[/retroactive] <attrib>=<flag list> - Add/modify standard attribute
		// @attribute/delete <attrib> - Remove standard attribute
		// @attribute/rename <attrib>=<new name> - Rename standard attribute
		// @attribute/limit <attrib>=<regexp pattern> - Restrict values to pattern
		// @attribute/enum [<delim>] <attrib>=<list> - Restrict values to list
		// @attribute/decompile[/retroactive] [<pattern>] - Decompile attribute table

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		// Check for decompile switch first (can work with 0 or 1 arg)
		if (switches.Contains("DECOMPILE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			var pattern = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "*";
			var retroactive = switches.Contains("RETROACTIVE");

			// Get all attribute entries from the database
			var allEntries = await Mediator!.CreateStream(new GetAllAttributeEntriesQuery()).ToArrayAsync();

			// Filter by pattern (simple wildcard matching)
			var matchingEntries = allEntries.Where(entry =>
				pattern == "*" ||
				entry.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
				(pattern.Contains('*') && MatchesWildcard(entry.Name, pattern))
			).ToArray();

			if (matchingEntries.Length == 0)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNoMatchPatternFormat), pattern);
				return CallState.Empty;
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileHeaderFormat), matchingEntries.Length, pattern);

			// Output @attribute/access command for each matching attribute
			foreach (var entry in matchingEntries.OrderBy(e => e.Name))
			{
				var flagList = string.Join(" ", entry.DefaultFlags);
				var retroFlag = retroactive ? "/retroactive" : "";
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileAccessFormat), retroFlag, entry.Name, flagList);

				// Include limit pattern if present
				if (!string.IsNullOrEmpty(entry.Limit))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileLimitFormat), entry.Name, entry.Limit);
				}

				// Include enum values if present
				if (entry.Enum != null && entry.Enum.Length > 0)
				{
					var enumList = string.Join(" ", entry.Enum);
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileEnumFormat), entry.Name, enumList);
				}
			}

			return CallState.Empty;
		}

		// All other operations require at least one argument
		if (args.Count == 0)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAttribute));
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}

		var attrName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attrName))
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAttribute));
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}

		// Check for various switches
		if (switches.Contains("ACCESS"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			if (args.Count < 2)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyFlags));
				return new CallState("#-1 NO FLAGS SPECIFIED");
			}

			var flagList = args["1"].Message?.ToPlainText() ?? "none";
			var retroactive = switches.Contains("RETROACTIVE");

			// Parse the flag list - space-separated flag names
			var flagNames = flagList.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(f => f.ToUpper())
				.ToArray();

			// Validate that all flags exist
			var allFlags = await Mediator!.CreateStream(new GetAttributeFlagsQuery()).ToArrayAsync();
			foreach (var flagName in flagNames)
			{
				if (!allFlags.Any(f => f.Name.Equals(flagName, StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandUnknownFlagFormat), flagName);
					return new CallState("#-1 UNKNOWN FLAG");
				}
			}

			// Create or update the attribute entry
			var entry = await Mediator!.Send(new CreateAttributeEntryCommand(attrName.ToUpper(), flagNames));
			if (entry == null)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandFailedToCreate));
				return new CallState("#-1 CREATE FAILED");
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandPermissionsNowFormat), attrName.ToUpperInvariant(), string.Join(" ", flagNames.Select(f => f.ToLowerInvariant())));

			// TODO: Retroactive flag updates to existing attribute instances.
			// When /retroactive is set, should update flags on all existing copies of this attribute
			// across all objects in the database. Requires bulk update operation.
			if (retroactive)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRetroactiveNotImplemented));
			}

			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			// Remove attribute from standard attribute table
			var deleted = await Mediator!.Send(new DeleteAttributeEntryCommand(attrName.ToUpper()));

			if (deleted)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRemovedFromTableFormat), attrName);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandExistingCopiesRemain));
			}
			else
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundInTableFormat), attrName);
				return new CallState("#-1 NOT FOUND");
			}

			return CallState.Empty;
		}

		if (switches.Contains("RENAME"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			if (args.Count < 2)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyNewName));
				return new CallState("#-1 NO NEW NAME SPECIFIED");
			}

			var newName = args["1"].Message?.ToPlainText();
			if (string.IsNullOrEmpty(newName))
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyNewName));
				return new CallState("#-1 NO NEW NAME SPECIFIED");
			}

			// Rename attribute in standard attribute table
			var renamed = await Mediator!.Send(new RenameAttributeEntryCommand(attrName.ToUpper(), newName.ToUpper()));

			if (renamed != null)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRenamedFormat), attrName, newName);
				// Note: Existing attribute instances keep their original names - this only affects new instances
			}
			else
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundInTableFormat), attrName);
				return new CallState("#-1 NOT FOUND");
			}

			return CallState.Empty;
		}

		if (switches.Contains("LIMIT"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			if (args.Count < 2)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyPattern));
				return new CallState("#-1 NO PATTERN SPECIFIED");
			}

			var pattern = args["1"].Message?.ToPlainText();
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitSettingPatternFormat), attrName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitPatternFormat), pattern ?? string.Empty);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitNewValuesMustMatch));

			// TODO: Attribute validation via regex patterns.
			// Requirements:
			// - Store regexp pattern with attribute in table
			// - Validate all new attribute values against pattern
			// - Pattern is case insensitive unless (?-i) is used
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandValidationNotImplemented));

			return new CallState("#-1 NOT IMPLEMENTED");
		}

		if (switches.Contains("ENUM"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied));
				return new CallState(Errors.ErrorPerm);
			}

			if (args.Count < 2)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyChoices));
				return new CallState("#-1 NO CHOICES SPECIFIED");
			}

			var choices = args["1"].Message?.ToPlainText();
			var choiceArray = choices?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

			// Validate that after parsing, we have actual choices (not just whitespace)
			if (choiceArray.Length == 0)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAtLeastOneChoice));
				return new CallState("#-1 NO CHOICES SPECIFIED");
			}

			// Get existing entry to preserve flags and limit
			var existingEntry = await Mediator!.Send(new GetAttributeEntryQuery(attrName.ToUpper()));
			var defaultFlags = existingEntry?.DefaultFlags ?? [];
			var limit = existingEntry?.Limit;

			// Create or update the attribute entry with enum values
			// Note: Command parameter is EnumValues, model property is Enum
			var enumAttrEntry = await Mediator!.Send(new CreateAttributeEntryCommand(
				attrName.ToUpper(),
				defaultFlags,
				Limit: limit,
				EnumValues: choiceArray));

			if (enumAttrEntry == null)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandFailedToUpdate));
				return new CallState("#-1 UPDATE FAILED");
			}

			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumSetChoicesFormat), attrName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumChoicesFormat), string.Join(" ", choiceArray));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumNewValuesMustMatch));

			return CallState.Empty;
		}

		// No switches - display attribute information
		var attrEntry = await Mediator!.Send(new GetAttributeEntryQuery(attrName.ToUpper()));

		if (attrEntry == null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundNotErrorFormat), attrName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundNotError2));
			return CallState.Empty;
		}

		await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandInfoFormat), attrEntry.Name);

		// Display default flags if any
		if (attrEntry.DefaultFlags.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDefaultFlagsFormat), string.Join(" ", attrEntry.DefaultFlags));
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDefaultFlagsNone));
		}

		// Show validation rules if set
		if (!string.IsNullOrEmpty(attrEntry.Limit))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitPatternValueFormat), attrEntry.Limit);
		}

		if (attrEntry.Enum != null && attrEntry.Enum.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumValuesFormat), string.Join(" ", attrEntry.Enum));
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@SKIP", Switches = ["IFELSE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public static async ValueTask<Option<CallState>> Skip(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var falsey = Predicates.Falsy(parsedIfElse!);

		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			await parser.CommandListParse(arg1.Message!);
		}

		return new CallState(!falsey);
	}

	[SharpCommand(Name = "@MESSAGE", Switches = ["NOEVAL", "SPOOF", "NOSPOOF", "REMIT", "OEMIT", "SILENT", "NOISY"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 3, MaxArgs = 0, ParameterNames = ["object", "type", "message"])]
	public static async ValueTask<Option<CallState>> Message(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;

		var recipientsArg = args.ElementAtOrDefault(0).Value.Message!;
		var defmsg = args.ElementAtOrDefault(1).Value.Message!;
		var objectAttrArg = args.ElementAtOrDefault(2).Value.Message!.ToPlainText();
		var otherArgs = args
			.Skip(3)
			.Select(x =>
			{
				if (int.TryParse(x.Key, out var numKey))
				{
					return new KeyValuePair<string, CallState>((numKey - 3).ToString(), x.Value);
				}

				return x;
			})
			.ToList();

		var isRemit = switches.Contains("REMIT");
		var isOemit = switches.Contains("OEMIT");
		var isNospoof = switches.Contains("NOSPOOF");
		var isSpoof = switches.Contains("SPOOF");
		var isSilent = switches.Contains("SILENT") || !switches.Contains("NOISY");

		var result = await MessageHelpers.ProcessMessageAsync(
			parser, Mediator!, LocateService!, AttributeService!, NotifyService!,
			PermissionService!, CommunicationService!, executor,
			recipientsArg, defmsg, objectAttrArg, otherArgs,
			isRemit, isOemit, isNospoof, isSpoof, isSilent);

		return result;
	}

	/// <summary>
	/// Simple wildcard matching helper for attribute name patterns.
	/// Supports * as wildcard character.
	/// </summary>
	private static bool MatchesWildcard(string text, string pattern)
	{
		// Convert wildcard pattern to regex
		var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
		.Replace("\\*", ".*") + "$";

		return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern,
		System.Text.RegularExpressions.RegexOptions.IgnoreCase);
	}

	[GeneratedRegex(@"\s{2,}")]
	private static partial Regex MultipleWhitespaceRegex();
}
