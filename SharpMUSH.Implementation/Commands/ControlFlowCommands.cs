using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// Handles delimiter/pid extraction for @dolist and @map commands when /DELIMIT or /PID is used.
	/// Format: @dolist/delimit <delimiter> <list>=<action>
	/// Returns the delimiter/pid value and the remaining list text.
	/// </summary>
	private (string paramValue, MString listText) ExtractFirstParameter(MString originalListText, bool extractParameter)
	{
		if (!extractParameter)
		{
			return (" ", originalListText);
		}

		var plainListText = originalListText.ToPlainText();
		if (plainListText.Length == 0)
		{
			return (" ", originalListText);
		}

		var spaceIndex = plainListText.IndexOf(' ');
		if (spaceIndex <= 0)
		{
			return (plainListText, MarkupText.Empty);
		}

		var textSpan = plainListText.AsSpan();
		var paramValue = textSpan.Slice(0, spaceIndex).ToString();
		var remainingText = plainListText.Length > spaceIndex + 1
			? MarkupText.Plain(textSpan.Slice(spaceIndex + 1).ToString())
			: MarkupText.Empty;

		return (paramValue, remainingText);
	}

	[SharpCommand(Name = "@MAP", Behavior = CB.EqSplit | CB.RSNoParse, MinArgs = 1, MaxArgs = 2,
		Switches = ["CLEARREGS", "DELIMIT", "INLINE", "INPLACE", "LOCALIZE", "NOBREAK", "NOTIFY"], ParameterNames = ["object", "code"])]
	public async ValueTask<Option<CallState>> Map(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapMustSpecifyAttribute), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapMustSpecifyAttribute), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		if (HelperFunctions.SplitDbRefAndOptionalAttr(attributePath) is not { Object: var objSpec, Attribute: var attrName })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapInvalidObjectAttributePath), executor);
			return new CallState(ErrorMessages.Returns.InvalidPath);
		}

		if (string.IsNullOrEmpty(attrName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapMustSpecifyAttribute), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		var originalListText = args.Count >= 2 ? args["1"].Message! : MarkupText.Empty;
		var (delimiter, listText) = ExtractFirstParameter(originalListText, switches.Contains("DELIMIT"));
		var list = listText.Split(delimiter);

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapWouldIterateFormat), executor, list.Length, objSpec, attrName);

		if (switches.Contains("INLINE"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapModeInline), executor);
		}

		if (switches.Contains("NOTIFY"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapWillQueueNotify), executor);
		}

		if (switches.Contains("CLEARREGS"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapWillClearRegisters), executor);
		}

		if (switches.Contains("LOCALIZE"))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapWillLocalizeRegisters), executor);
		}

		var targetObject = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, objSpec, LocateFlags.All);

		if (targetObject is not AnySharpObject target)
		{
			return new CallState(ErrorMessages.Returns.ObjectNotFound);
		}

		var attributeResult = await AttributeService.GetAttributeAsync(
			executor, target, attrName, IAttributeService.AttributeMode.Read, false);

		if (attributeResult is not SharpAttribute[] attributeChain)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MapAttributeNotFoundOnObjectFormat), executor, attrName, target.Object().Name);
			return new CallState(ErrorMessages.Returns.NoSuchAttribute);
		}

		var attribute = attributeChain.Last();
		var attributeText = attribute.Value.ToPlainText();

		if (string.IsNullOrWhiteSpace(attributeText))
		{
			return CallState.Empty;
		}

		var isInline = switches.Contains("INLINE");
		var results = new List<string>();
		var hadErrors = false;

		if (isInline)
		{
			foreach (var element in list)
			{
				var registerDict = new Dictionary<string, MString> { ["0"] = element! };
				var registerStack = new ConcurrentStack<Dictionary<string, MString>>();
				registerStack.Push(registerDict);

				var stateForElement = parser.CurrentState with
				{
					Registers = registerStack,
					Executor = target.Object().DBRef,
					Caller = parser.CurrentState.Executor
				};

				var result = await parser.With(state => stateForElement, async newParser =>
				{
					return await newParser.WithAttributeDebug(attribute,
						async p => await p.CommandListParse(attribute.Value));
				});

				hadErrors |= result?.HadErrors == true;
				if (result != null && result.Message != null)
				{
					results.Add(result.Message.ToPlainText() ?? string.Empty);
				}
			}

			if (switches.Contains("NOTIFY"))
			{
				await Mediator.Send(new AdmitCommandListRequest(
					MarkupText.Plain("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(executor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}

			return new CallState(string.Join(" ", results)) { HadErrors = hadErrors };
		}
		else
		{
			foreach (var element in list)
			{
				var registerDict = new Dictionary<string, MString> { ["0"] = element! };
				var registerStack = new ConcurrentStack<Dictionary<string, MString>>();
				registerStack.Push(registerDict);

				var stateForElement = parser.CurrentState with
				{
					Registers = registerStack,
					Executor = target.Object().DBRef,
					Caller = parser.CurrentState.Executor
				};

				await Mediator.Send(new AdmitCommandListRequest(
					attribute.Value,
					stateForElement,
					new DbRefAttribute(target.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}

			if (switches.Contains("NOTIFY"))
			{
				await Mediator.Send(new AdmitCommandListRequest(
					MarkupText.Plain("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(executor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}

			return CallState.Empty;
		}
	}

	[SharpCommand(Name = "@DOLIST", Behavior = CB.EqSplit | CB.RSNoParse, MinArgs = 1, MaxArgs = 2,
		Switches = ["CLEARREGS", "DELIMIT", "INLINE", "INPLACE", "LOCALIZE", "NOBREAK", "NOTIFY", "PID"], ParameterNames = ["list", "command"])]
	public async ValueTask<Option<CallState>> DoList(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		if (parser.CurrentState.Arguments.Count < 2)
		{
			await NotifyService.NotifyLocalized(enactor, nameof(ErrorMessages.Notifications.DoListWhatToDoWithList), enactor);
			return new None();
		}

		var hasDelimit = switches.Contains("DELIMIT");
		var hasPid = switches.Contains("PID");

		string delimiter = " ";
		string? notifyPid = null;
		MString listText;

		if (hasDelimit)
		{
			var (delimiterParam, extractedList) = ExtractFirstParameter(
				parser.CurrentState.Arguments["0"].Message!,
				true);
			delimiter = delimiterParam;
			listText = extractedList;
		}
		else if (hasPid)
		{
			var (pidParam, extractedList) = ExtractFirstParameter(
				parser.CurrentState.Arguments["0"].Message!,
				true);
			notifyPid = pidParam;
			listText = extractedList;
		}
		else
		{
			listText = parser.CurrentState.Arguments["0"].Message!;
		}

		var list = listText.Split(delimiter);
		var command = parser.CurrentState.Arguments["1"].Message!;

		// Replace ## with %iL in the command for PennMUSH backward compatibility
		var commandParts = command.Split("##");
		if (commandParts.Length > 1)
		{
			command = MarkupText.Join(MarkupText.Plain("%iL"), commandParts);
		}

		var isInline = switches.Contains("INLINE") || switches.Contains("INPLACE");

		if (isInline)
		{
			var noBreak = switches.Contains("NOBREAK") || switches.Contains("INPLACE");
			var wrappedIteration = new IterationWrapper<MString>
			{ Value = MarkupText.Empty, Break = false, NoBreak = noBreak, Iteration = 0 };
			parser.CurrentState.IterationRegisters.Push(wrappedIteration);

			var lastCallState = CallState.Empty;
			var hadErrors = false;
			var visitorFunc = parser.CommandListParseVisitor(command);
			foreach (var item in list)
			{
				wrappedIteration.Value = item!;
				wrappedIteration.Iteration++;

				// Note: Command is parsed once (line above loop), then the visitor is called
				// multiple times with different iteration register values. This is optimized.
				lastCallState = await visitorFunc();
				hadErrors |= lastCallState?.HadErrors == true;
			}

			parser.CurrentState.IterationRegisters.TryPop(out _);

			if (switches.Contains("NOTIFY"))
			{
				await Mediator.Send(new AdmitCommandListRequest(
					MarkupText.Plain("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}
			else if (hasPid && !string.IsNullOrEmpty(notifyPid))
			{
				await Mediator.Send(new AdmitCommandListRequest(
					MarkupText.Plain($"@notify {notifyPid}"),
					parser.CurrentState,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}

			return (lastCallState ?? CallState.Empty) with { HadErrors = hadErrors };
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

				await Mediator.Send(new AdmitCommandListRequest(
					command,
					stateForIteration,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}

			if (switches.Contains("NOTIFY"))
			{
				await Mediator.Send(new AdmitCommandListRequest(
					MarkupText.Plain("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}
			else if (hasPid && !string.IsNullOrEmpty(notifyPid))
			{
				await Mediator.Send(new AdmitCommandListRequest(
					MarkupText.Plain($"@notify {notifyPid}"),
					parser.CurrentState,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}

			return CallState.Empty;
		}
	}

	[SharpCommand(Name = "@SWITCH",
		Switches = ["NOTIFY", "FIRST", "ALL", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 3, MaxArgs = int.MaxValue, ParameterNames = ["expression", "cases..."])]
	public async ValueTask<Option<CallState>> Switch(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToArray();
		var strArg = args["0"];
		var testString = strArg.Message?.ToPlainText() ?? string.Empty;
		Option<MString> defaultArg = new None();
		var matched = false;
		var hadErrors = false;

		// Separate out the default action (last element when total arg count is even).
		// args["0"] is the test expression; remaining args are (pattern, action) pairs plus optional default.
		// Even total args means: test + pairs + default → odd remaining → default is last.
		var remainingArgs = args.Values.Skip(1).ToList();
		if (args.Count % 2 == 0)
		{
			defaultArg = remainingArgs[^1].Message!;
			remainingArgs.RemoveAt(remainingArgs.Count - 1);
		}

		var isFirst = switches.Contains("FIRST") && !switches.Contains("ALL");
		var isRegexp = switches.Contains("REGEXP");

		// PennMUSH folds the execution switches into one queue_type (src/cmds.c:1510-1521), where
		// QUEUE_RECURSE == QUEUE_INPLACE | QUEUE_NO_BREAKS | QUEUE_PRESERVE_QREG (hdrs/externs.h:150).
		// So /inplace is /inline/nobreak/localize ('help @switch2'), and with neither switch the
		// actions become NEW queue entries -- 'help @switch4' contrasts the two orderings directly.
		var isInplace = switches.Contains("INPLACE");
		var isInline = switches.Contains("INLINE") || isInplace;
		var noBreak = switches.Contains("NOBREAK") || isInplace;

		// /LOCALIZE and /CLEARREGS are register switches on the INLINE action, not on the @switch:
		// cmds.c:1513-1521 only ORs QUEUE_PRESERVE_QREG / QUEUE_CLEAR_QREG in when queue_type is
		// already QUEUE_INPLACE, and do_entry applies them once per inplace entry (cque.c:1183-1195).
		// 'help @switch2' says the same: q-registers are saved/cleared "before each <action> is run".
		var hasLocalize = switches.Contains("LOCALIZE") || isInplace;
		var hasClearRegs = switches.Contains("CLEARREGS");

		// do_switch (src/predicat.c) runs each matched action with a PE_REGS_SWITCH | PE_REGS_CAPTURE
		// frame, so $0-$9 in it read the match; a queued action takes a copy of the frame with it.
		var captures = new RegexpCaptureFrame(parser.CurrentState.CurrentEvaluation);
		parser.CurrentState.RegexRegisters.Push(captures);
		parser.CurrentState.SwitchStack.Push(strArg.Message!);

		try
		{
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
				try
				{
					patternMatched = SwitchPatterns.Matches(strArg.Message!, patternText, isRegexp, captures);
				}
				catch (ArgumentException ex)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SwitchInvalidRegexpFormat), executor, patternText, ex.Message);
					continue;
				}
				catch (RegexMatchTimeoutException)
				{
					continue;
				}

				if (patternMatched)
				{
					matched = true;
					// Substitute #$ with the test string in the action, matching PennMUSH behavior.
					var actionText = actionArg.Message!.ToPlainText().Replace("#$", testString);
					hadErrors |= await RunControlFlowAction(parser, executor, MarkupText.Plain(actionText),
						isInline, noBreak, hasLocalize, hasClearRegs);

					if (isFirst) break;
				}
			}

			if (defaultArg.TryGetValue(out var defaultValue) && !matched)
			{
				captures.Clear();
				var defaultText = defaultValue.ToPlainText().Replace("#$", testString);
				hadErrors |= await RunControlFlowAction(parser, executor, MarkupText.Plain(defaultText),
					isInline, noBreak, hasLocalize, hasClearRegs);
			}

			// PennMUSH gates the notify on the queue type: `if (!(queue_type & QUEUE_INPLACE) && notifyme)`
			// (src/predicat.c:1145), so /notify has no effect alongside /inline or /inplace.
			if (switches.Contains("NOTIFY") && !isInline)
			{
				await Mediator.Send(new AdmitCommandListRequest(
					MarkupText.Plain("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(executor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}

			return new CallState(matched) { HadErrors = hadErrors };
		}
		finally
		{
			parser.CurrentState.SwitchStack.TryPop(out _);
			parser.CurrentState.RegexRegisters.TryPop(out _);
		}
	}

	/// <summary>
	/// Runs one matched <c>@switch</c>/<c>@select</c> action. Default is a NEW queue entry, exactly as
	/// PennMUSH's <c>do_switch</c> does with <c>QUEUE_DEFAULT</c>; <c>/inline</c> (and <c>/inplace</c>)
	/// run the action in the calling action list instead. An <c>@break</c> inside an inline action stops
	/// the caller too unless <c>/nobreak</c> was given ('help @switch2').
	///
	/// <para><c>/localize</c> and <c>/clearregs</c> wrap the INLINE action only, one save/clear per
	/// action: <c>cmd_switch</c> folds <c>QUEUE_PRESERVE_QREG</c>/<c>QUEUE_CLEAR_QREG</c> into
	/// <c>queue_type</c> only once it is already <c>QUEUE_INPLACE</c> (src/cmds.c:1513-1521), and
	/// <c>do_entry</c> localizes around each inplace entry it drains (src/cque.c:1183-1195). A queued
	/// action instead gets its own copy of the registers from <see cref="ParserState.SnapshotForQueuedAction"/>, matching
	/// <c>PE_INFO_CLONE</c>, so neither switch has anything to do there.</para>
	/// </summary>
	private async ValueTask<bool> RunControlFlowAction(IMUSHCodeParser parser, AnySharpObject executor, MString action,
		bool isInline, bool noBreak, bool localizeRegisters, bool clearRegisters)
	{
		if (!isInline)
		{
			await Mediator.Send(new AdmitCommandListRequest(
				action,
				parser.CurrentState.SnapshotForQueuedAction(),
				new DbRefAttribute(executor.Object().DBRef, DefaultSemaphoreAttributeArray),
				-1), ExecutionBudget.CurrentToken);
			return false;
		}

		// Save before Clear: the new Dictionary<> is an independent copy, so clearing the original
		// afterwards does not touch it. /clearregs without /localize deliberately does not restore --
		// that is do_entry's bare QUEUE_CLEAR_QREG case, which calls clear_allq and keeps no snapshot.
		Dictionary<string, MString>? savedRegisters = null;
		if ((localizeRegisters || clearRegisters) && parser.CurrentState.Registers.TryPeek(out var topRegisters))
		{
			if (localizeRegisters)
			{
				savedRegisters = new Dictionary<string, MString>(topRegisters);
			}

			if (clearRegisters)
			{
				topRegisters.Clear();
			}
		}

		try
		{
			var propagation = new BreakPropagation { PreserveNext = true };
			var result = await parser.With(
				state => state with { BreakPropagation = propagation },
				p => p.CommandListParse(action));

			if (propagation.Broke && !noBreak)
			{
				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
			}
			return result?.HadErrors == true;
		}
		finally
		{
			if (savedRegisters is not null && parser.CurrentState.Registers.TryPeek(out var regsToRestore))
			{
				regsToRestore.Clear();
				foreach (var (key, value) in savedRegisters)
				{
					regsToRestore[key] = value;
				}
			}
		}
	}

	[SharpCommand(Name = "@IFELSE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 2, MaxArgs = 3, ParameterNames = ["condition", "true-command", "false-command"])]
	public async ValueTask<Option<CallState>> IfElse(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var truthy = parsedIfElse!.Truthy(parser);
		CallState? nestedResult = null;

		if (truthy)
		{
			nestedResult = await parser.CommandListParse(parser.CurrentState.Arguments["1"].Message!);
		}
		else if (parser.CurrentState.Arguments.TryGetValue("2", out var arg2))
		{
			nestedResult = await parser.CommandListParse(arg2.Message!);
		}

		return new CallState(truthy) { HadErrors = nestedResult?.HadErrors == true };
	}

	[SharpCommand(Name = "@SELECT",
		Switches = ["NOTIFY", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse, MinArgs = 1, MaxArgs = int.MaxValue, ParameterNames = ["expression", "cases..."])]
	public async ValueTask<Option<CallState>> Select(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		var testString = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(testString))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectMustSpecifyTestString), executor);
			return new CallState(ErrorMessages.Returns.NoTestString);
		}

		// Pattern matching flags (declared outside try/finally for /localize restore access).
		// PennMUSH builds one queue_type out of these (src/cmds.c:1390-1403) and
		// QUEUE_RECURSE == QUEUE_INPLACE | QUEUE_NO_BREAKS | QUEUE_PRESERVE_QREG (hdrs/externs.h:150),
		// so /inplace is exactly /inline/nobreak/localize ('help @switch2').
		var isRegexp = switches.Contains("REGEXP");
		var isInplace = switches.Contains("INPLACE");
		var isInline = switches.Contains("INLINE") || isInplace;
		var noBreak = switches.Contains("NOBREAK") || isInplace;
		var localizeRegs = switches.Contains("LOCALIZE") || isInplace;
		var clearRegs = switches.Contains("CLEARREGS");

		// cmd_select builds the same queue_type as cmd_switch (src/cmds.c:1390-1403), so /LOCALIZE and
		// /CLEARREGS only bite on an INLINE action; RunControlFlowAction applies them around it.
		// Like @switch, the matched action runs with the match's captures for $0-$9.
		var captures = new RegexpCaptureFrame(parser.CurrentState.CurrentEvaluation);
		parser.CurrentState.RegexRegisters.Push(captures);
		parser.CurrentState.SwitchStack.Push(args["0"].Message!);

		try
		{
			var pairCount = (args.Count - 1) / 2;
			var hasDefault = (args.Count - 1) % 2 == 1;

			var matchFound = false;
			var hadErrors = false;
			for (int i = 0; i < pairCount; i++)
			{
				var exprIndex = (i * 2) + 1;
				var actionIndex = (i * 2) + 2;

				var pattern = args[exprIndex.ToString()].Message?.ToPlainText() ?? "";
				var action = args[actionIndex.ToString()].Message;

				bool matches;
				try
				{
					matches = SwitchPatterns.Matches(args["0"].Message!, pattern, isRegexp, captures);
				}
				catch (ArgumentException)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SelectInvalidRegexPatternFormat), executor, pattern);
					continue;
				}
				catch (RegexMatchTimeoutException)
				{
					continue;
				}

				if (matches && action != null)
				{
					matchFound = true;

					var actionText = action.ToPlainText().Replace("#$", testString);
					var actionMString = MarkupText.Plain(actionText);

					hadErrors |= await RunControlFlowAction(parser, executor, actionMString,
						isInline, noBreak, localizeRegs, clearRegs);

					break;
				}
			}

			if (!matchFound && hasDefault)
			{
				captures.Clear();
				var defaultIndex = args.Count - 1;
				var defaultAction = args[defaultIndex.ToString()].Message;

				if (defaultAction != null)
				{
					var actionText = defaultAction.ToPlainText().Replace("#$", testString);
					var actionMString = MarkupText.Plain(actionText);

					hadErrors |= await RunControlFlowAction(parser, executor, actionMString,
						isInline, noBreak, localizeRegs, clearRegs);
				}
			}

			// PennMUSH gates the notify on the queue type: `if (!(queue_type & QUEUE_INPLACE) && notifyme)`
			// (src/predicat.c:1145), so /notify has no effect alongside /inline or /inplace.
			if (switches.Contains("NOTIFY") && !isInline)
			{
				await Mediator.Send(new AdmitCommandListRequest(
					MarkupText.Plain("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(executor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1), ExecutionBudget.CurrentToken);
			}

			return new CallState(matchFound) { HadErrors = hadErrors };
		}
		finally
		{
			parser.CurrentState.SwitchStack.TryPop(out _);
			parser.CurrentState.RegexRegisters.TryPop(out _);
		}
	}

	[SharpCommand(Name = "@BREAK", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 2, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Break(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Inline does nothing.
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var nargs = args.Count;

		// Note: INLINE is default behavior (immediate execution)
		// QUEUED switch queues the command for later execution via task scheduler
		var useQueue = switches.Contains("QUEUED");
		var hadErrors = false;

		switch (nargs)
		{
			case 0:
				// No condition provided — treat as falsy (@break 0 = don't break).
				break;
			case 1:
				if (args["0"].Message.Truthy(parser))
				{
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}

				return args["0"] with { HadErrors = args["0"].HadErrors || hadErrors };
			case 2 when args["0"].Message.Truthy(parser):
				var command = await args["1"].ParsedMessage();

				if (useQueue)
				{
					var executor = parser.CurrentState.Executor ?? throw new InvalidOperationException("Executor cannot be null");
					await Mediator.Send(new AdmitCommandListRequest(
						command!,
						parser.CurrentState,
						new DbRefAttribute(executor, ["BREAK"]),
						-1), ExecutionBudget.CurrentToken);
				}
				else
				{
					var commandList = parser.CommandListParseVisitor(command!);
					hadErrors |= (await commandList())?.HadErrors == true;
				}

				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));

				return args["0"] with { HadErrors = args["0"].HadErrors || hadErrors };
			case 2:
				return args["0"] with { HadErrors = args["0"].HadErrors || hadErrors };
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@ASSERT", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 2, ParameterNames = ["condition"])]
	public async ValueTask<Option<CallState>> Assert(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var nargs = args.Count;

		// Note: INLINE is default behavior (immediate execution)
		// QUEUED switch queues the command for later execution via task scheduler
		var useQueue = switches.Contains("QUEUED");
		var hadErrors = false;

		switch (nargs)
		{
			case 0:
				// No condition provided — treat as falsy (@assert 0 = assertion fails = break).
				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				break;
			case 1:
				if (args["0"].Message.Falsy(parser))
				{
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}

				return args["0"] with { HadErrors = args["0"].HadErrors || hadErrors };
			case 2 when args["0"].Message.Falsy(parser):
				var command = await args["1"].ParsedMessage();

				if (useQueue)
				{
					var executor = parser.CurrentState.Executor ?? throw new InvalidOperationException("Executor cannot be null");
					await Mediator.Send(new AdmitCommandListRequest(
						command!,
						parser.CurrentState,
						new DbRefAttribute(executor, ["ASSERT"]),
						-1), ExecutionBudget.CurrentToken);
				}
				else
				{
					var commandList = parser.CommandListParseVisitor(command!);
					hadErrors |= (await commandList())?.HadErrors == true;
				}

				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));

				return args["0"] with { HadErrors = args["0"].HadErrors || hadErrors };
			case 2:
				return args["0"] with { HadErrors = args["0"].HadErrors || hadErrors };
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@SKIP", Switches = ["IFELSE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 1, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Skip(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var falsey = parsedIfElse!.Falsy(parser);
		CallState? nestedResult = null;

		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			nestedResult = await parser.CommandListParse(arg1.Message!);
		}

		return new CallState(!falsey) { HadErrors = nestedResult?.HadErrors == true };
	}

	[SharpCommand(Name = "@RETRY", Switches = [],
		Behavior = CB.Default | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Retry(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!args.TryGetValue("0", out var predicate))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RetryUsage), executor);
			return new CallState(ErrorMessages.Returns.RetryNoConditionProvided);
		}

		var commandHistory = parser.CurrentState.CommandHistory;
		if (commandHistory == null || commandHistory.Count < 2)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RetryNothingToRetry), executor);
			return new CallState(ErrorMessages.Returns.RetryNoCommandToRetry);
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
		var hadErrors = false;

		while (limit > 0)
		{
			// Evaluate condition in the current argument context.
			var condResult = await parser.With(
				state => state with { Arguments = currentArgs },
				innerParser => innerParser.FunctionParse(conditionText));

			hadErrors |= condResult?.HadErrors == true;
			if (!(condResult?.Message.Truthy(parser) ?? false))
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
				hadErrors |= evaluated?.HadErrors == true;
				newArgValues[capturedIndex.ToString()] = evaluated!;
			}

			var retryResult = await parser.With(
				state => state with { Arguments = newArgValues },
				async innerParser => await previousCommandInvoker(innerParser));
			hadErrors |= retryResult.TryGetValue(out var retried) && retried.HadErrors;

			// The new arg values become the context for the next condition check.
			currentArgs = newArgValues;
			limit--;
		}

		return new CallState(1000 - limit) { HadErrors = hadErrors };
	}

	[SharpCommand(Name = "@INCLUDE", Switches = ["LOCALIZE", "CLEARREGS", "NOBREAK", "CHAIN"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 1, MaxArgs = 31, ParameterNames = ["file"])]
	public async ValueTask<Option<CallState>> Include(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeMustSpecifyAttributePath), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		var hasChain = switches.Contains("CHAIN");
		var hasNoBreak = switches.Contains("NOBREAK");
		var hasClearRegs = switches.Contains("CLEARREGS");
		var hasLocalize = switches.Contains("LOCALIZE");

		// With /chain the left side is a space-separated list of <object>/<attribute> targets, run in order
		// and sharing one q-register set (that is how a chain hands off from step to step). Each target's
		// object must be space-free (a dbref, "me", or a single-word name), since spaces separate the targets.
		// Without /chain there is a single target whose object name may contain spaces as usual.
		string[] targets = hasChain
			? attributePath.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			: [attributePath];

		// Build EnvironmentRegisters once so %0, %1, ... are substituted — the same args reach every target
		// in a chain. args["0"] is the target list; args["1"], args["2"], ... map to %0, %1, ...
		var envArgs = new Dictionary<string, CallState>(parser.CurrentState.EnvironmentRegisters);
		for (var i = 1; i < args.Count; i++)
		{
			if (args.TryGetValue(i.ToString(), out var argVal) && argVal.Message != null)
			{
				envArgs[(i - 1).ToString()] = argVal;
			}
		}

		// /localize: save Q-registers so the included code cannot permanently change the caller's registers.
		// /clearregs: start the included code with empty Q-registers. Both apply around the WHOLE chain;
		// within a chain the targets still share registers. (Save must happen before Clear.)
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

		// Run the targets. Default: run a multi-target chain as ONE command list so an @break/@assert in a
		// link short-circuits the remaining links (VisitCommandList contains the break at the list boundary,
		// as it does for a normal @include). /nobreak: run each target on its own so a break is confined to
		// its link and the chain carries on. A single target is just the ordinary @include.
		try
		{
			CallState lastResult = CallState.Empty;

			if (!hasNoBreak && targets.Length > 1)
			{
				var texts = new List<string>(targets.Length);

				foreach (var parts in targets.Select(target => target.Split('/', 2)))
				{
					if (parts.Length < 2)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeMustSpecifyObjectAttributePath), executor);
						return new CallState(ErrorMessages.Returns.InvalidPath);
					}

					var (error, _, text) = await ReadTarget(parts[0], parts[1]);
					if (error is not null)
					{
						return error;
					}

					if (text is null)
					{
						continue; // empty attribute (already notified) contributes nothing
					}

					texts.Add(text);
				}

				if (texts.Count == 0)
				{
					return CallState.Empty;
				}

				var combined = string.Join(" ; ", texts);
				// Key recursion tracking by the chain's own target-list identity (mirroring how single-target
				// @include keys on the attribute LongName): the SAME chain nested within itself shares a
				// bucket, so genuine whole-chain recursion is still caught, while DIFFERENT chains get
				// DIFFERENT buckets, so ordinary non-recursive nesting is not falsely limited. (A constant key
				// would collapse every chain into one bucket; the first link's name would collide unrelated
				// chains that share that link.) The "@INCLUDE`CHAIN`" prefix cannot collide with a real
				// attribute LongName. Recursion originating in a SINGLE link is caught independently of this
				// key: a nested @include of that link runs through RunOne under the link's own LongName.
				var chainRecursionKey = "@INCLUDE`CHAIN`" + string.Join("`", targets).ToUpperInvariant();
				var chainPropagation = new BreakPropagation { PreserveNext = true };
				lastResult = await ExecuteAttributeWithTracking(parser, chainRecursionKey, async () =>
					await parser.With(
						state => state with
						{ EnvironmentRegisters = envArgs, Caller = state.Executor, BreakPropagation = chainPropagation },
						p => p.CommandListParse(MarkupText.Plain(combined))) ?? CallState.Empty);

				RaiseBreakForCaller(chainPropagation);

				return lastResult;
			}

			// Single target, or a /nobreak chain: run each target on its own. RunOne still propagates a
			// break to the calling list unless /nobreak was given — what "on its own" means here is
			// that each target gets its own BreakPropagation, not that its break stops at the boundary.
			foreach (var parts in targets.Select(target => target.Split('/', 2)))
			{
				if (parts.Length < 2)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeMustSpecifyObjectAttributePath), executor);
					return new CallState(ErrorMessages.Returns.InvalidPath) { HadErrors = lastResult.HadErrors };
				}

				var (error, attribute, text) = await ReadTarget(parts[0], parts[1]);
				if (error is not null)
				{
					return error with { HadErrors = error.HadErrors || lastResult.HadErrors };
				}

				if (text is null)
				{
					continue;
				}

				var result = await RunOne(attribute!, text);
				lastResult = result with { HadErrors = result.HadErrors || lastResult.HadErrors };
			}

			return lastResult;
		}
		catch (Exception ex)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeErrorExecutingFormat), executor, ex.Message);
			return new CallState($"#-1 ERROR: {ex.Message}") { HadErrors = true };
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

		// Locate -> read -> strip $/^ prefix. Returns a (notified) error CallState on a hard failure;
		// Text=null for an empty attribute (already notified); otherwise the attribute and its stripped text.
		async ValueTask<(CallState? Error, SharpAttribute? Attribute, string? Text)> ReadTarget(string objectName, string attributeName)
		{
			// Locate + read AS THE EXECUTOR (looker=executor, perm=executor): @include runs as the executor,
			// mirroring u()/$-command dispatch -- NOT the enactor (which broke remote $-command triggers on
			// WIZARD helper objects).
			return await LocateService.LocateAndNotifyIfInvalidWithCallState(
				parser, executor, executor, objectName, LocateFlags.All) switch
			{
				AnySharpObject targetObject => await ReadTargetAttribute(targetObject, attributeName),
				Error<CallState> error => (error.Value, null, null)
			};
		}

		async ValueTask<(CallState? Error, SharpAttribute? Attribute, string? Text)> ReadTargetAttribute(
			AnySharpObject targetObject, string attributeName)
		{
			var attributeResult = await AttributeService.GetAttributeAsync(
				executor, targetObject, attributeName, IAttributeService.AttributeMode.Read, false);

			if (attributeResult is Error<string> attributeError)
			{
				// Surface the real error (e.g. a permission failure) instead of masking it as "no such attribute".
				await NotifyService.Notify(executor, attributeError.Value, executor);
				return (new CallState(attributeError.Value), null, null);
			}

			if (attributeResult is not SharpAttribute[] attributeChain)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.IncludeAttributeIsEmptyFormat), executor, attributeName);
				return (null, null, null);
			}

			var attribute = attributeChain.Last();
			var attributeText = attribute.Value.ToPlainText();

			// Strip ^...: or $...: listen/command prefixes.
			if (attributeText.StartsWith("^") || attributeText.StartsWith("$"))
			{
				var colonIndex = attributeText.IndexOf(':');
				if (colonIndex > 0)
				{
					attributeText = attributeText.AsSpan(colonIndex + 1).TrimStart().ToString();
				}
			}

			return (null, attribute, attributeText);
		}

		// Execute one already-read target in-place with the shared env args, containing an @break when /nobreak.
		async ValueTask<CallState> RunOne(SharpAttribute attribute, string text)
		{
			return await ExecuteAttributeWithTracking(parser, attribute.LongName!.ToUpper(), async () =>
			{
				var propagation = new BreakPropagation { PreserveNext = true };

				var execResult = await parser.With(
					state => state with
					{ EnvironmentRegisters = envArgs, Caller = state.Executor, BreakPropagation = propagation },
					p => p.WithAttributeDebug(attribute, pp => pp.CommandListParse(MarkupText.Plain(text))));

				RaiseBreakForCaller(propagation);

				return execResult ?? CallState.Empty;
			});
		}

		// @include inserts the included actions into the CALLING list, so a guard inside them stops
		// the caller as well -- that is the idiom `help @include` teaches, and /nobreak is the switch
		// that suppresses it. The included list has already popped its own marker by the time we get
		// here (VisitCommandList always does), so re-raise it for the list that ran the @include.
		void RaiseBreakForCaller(BreakPropagation propagation)
		{
			if (propagation.Broke && !hasNoBreak)
			{
				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
			}
		}
	}
}
