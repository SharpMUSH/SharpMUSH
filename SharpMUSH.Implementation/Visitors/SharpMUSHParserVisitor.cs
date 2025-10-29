using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// This class implements the SharpMUSHParserBaseVisitor from the Generated code.
/// If additional pieces of the parse-tree are added, the Generated project must be re-generated 
/// and new Visitors may need to be added.
/// </summary>
/// <param name="parser">The Parser, so that inner functions can force a parser-call.</param>
/// <param name="source">The original MarkupString. A plain GetText is not good enough to get the proper value back.</param>
public class SharpMUSHParserVisitor(
	ILogger logger,
	IMUSHCodeParser parser,
	IOptionsWrapper<SharpMUSHOptions> Configuration,
	IMediator Mediator,
	INotifyService NotifyService,
	IConnectionService ConnectionService,
	ILocateService LocateService,
	ICommandDiscoveryService CommandDiscoveryService,
	IAttributeService AttributeService,
	MString source)
	: SharpMUSHParserBaseVisitor<ValueTask<CallState?>>
{
	private int _braceDepthCounter;

	protected override ValueTask<CallState?> DefaultResult => ValueTask.FromResult<CallState?>(null);

	public override async ValueTask<CallState?> Visit(IParseTree tree) => await tree.Accept(this);

	public override async ValueTask<CallState?> VisitChildren(IRuleNode? node)
	{
		if (node is null) return null;
		
		var result = await DefaultResult;

		await foreach (var child in Enumerable
			         .Range(0, node.ChildCount)
			         .ToAsyncEnumerable()
			         .Select(node.GetChild))
		{
			result = child is null 
				? AggregateResult(result, null) 
				: AggregateResult(result, await child.Accept(this));
		}

		return result;
	}

	public async ValueTask<CallState?> VisitChildrenOrBreak(IRuleNode node, Func<bool> haltPredicate)
	{
		var result = await DefaultResult;

		foreach (var child in Enumerable
			         .Range(0, node.ChildCount)
			         .Select(node.GetChild))
		{
			if (haltPredicate())
			{
				break;
			}

			result = AggregateResult(result, await child.Accept(this));
		}

		return result;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
	private static CallState? AggregateResult(CallState? aggregate,
		CallState? nextResult)
		=> (aggregate, nextResult) switch
		{
			(null, null)
				=> null,
			({ Arguments: not null } agg, { Arguments: not null } next)
				=> agg with { Arguments = [.. agg.Arguments, .. next.Arguments] },
			({ Message: not null } agg, { Message: not null } next)
				=> agg with { Message = MModule.concat(agg.Message, next.Message) },
			var (agg, next)
				=> agg ?? next
		};

	public override async ValueTask<CallState?> VisitFunction([NotNull] FunctionContext context)
	{
		if (parser.CurrentState.ParseMode is ParseMode.NoParse or ParseMode.NoEval)
		{
			// var a = await VisitChildren(context);
			return new CallState(context.GetText());
		}

		var functionName = context.FUNCHAR().GetText().TrimEnd()[..^1];
		var arguments = context.evaluationString() ?? Enumerable.Empty<EvaluationStringContext>().ToArray();

		/* await NotifyService!.Notify(parser.CurrentState.Executor!.Value, MModule.single(
			$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} :")); */

		var result = await CallFunction(functionName.ToLower(), source, context, arguments, this);

		/* await NotifyService!.Notify(parser.CurrentState.Caller!.Value, MModule.single(
			$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} => {result.Message}")); */

		return result;
	}

	/// <summary>
	/// TODO: Optimization needed. We should at least grab the in-built ones at startup.
	/// TODO: Move this to a Library Service implementation.
	/// </summary>
	/// <param name="name">Function Name</param>
	/// <param name="src">The source MarkupString</param>
	/// <param name="context">Function Context for Depth</param>
	/// <param name="args">Arguments</param>
	/// <param name="visitor"></param>
	/// <returns>The resulting CallState.</returns>
	public async ValueTask<CallState> CallFunction(string name, MString src,
		FunctionContext context, EvaluationStringContext[] args, SharpMUSHParserVisitor visitor)
	{
		try
		{
			if (!parser.FunctionLibrary.TryGetValue(name, out var libraryMatch))
			{
				var discoveredFunction = DiscoverBuiltInFunction(name);

				if (!discoveredFunction.TryPickT0(out var functionValue, out _))
				{
					return new CallState(string.Format(Errors.ErrorNoSuchFunction, name), context.Depth());
				}

				parser.FunctionLibrary.Add(name, (functionValue, true));
				libraryMatch = parser.FunctionLibrary[name];
			}

			var (attribute, function) = libraryMatch.LibraryInformation;

			var currentStack = parser.State;
			var currentState = parser.CurrentState;
			var contextDepth = context.Depth();
			var stackDepth = currentStack.Count();
			var recursionDepth = currentStack.Count(x => x.Function == name);

			List<CallState> refinedArguments;

			// TODO: Check Permissions here.

			/* Validation, this should probably go into its own function! */
			if (args.Length > attribute.MaxArgs)
			{
				// Better Error Needed.
				return new CallState(Errors.ErrorArgRange, context.Depth());
			}

			if (args.Length < attribute.MinArgs)
			{
				return new CallState(string.Format(Errors.ErrorTooFewArguments, name, attribute.MinArgs, args.Length),
					contextDepth);
			}

			if (((attribute.Flags & FunctionFlags.UnEvenArgsOnly) != 0) && (args.Length % 2 == 0))
			{
				return new CallState(string.Format(Errors.ErrorGotEvenArgs, name), contextDepth);
			}

			if (((attribute.Flags & FunctionFlags.EvenArgsOnly) != 0) && (args.Length % 2 != 0))
			{
				return new CallState(string.Format(Errors.ErrorGotUnEvenArgs, name), contextDepth);
			}

			// TODO: Reconsider where this is. We Push down below, after we have the refined arguments.
			// But each RefinedArguments call will create a new call to this FunctionParser without depth info.
			if (contextDepth > Configuration.CurrentValue.Limit.CallLimit)
			{
				// TODO: Context Depth is not the correct value to use here.
				return new CallState(Errors.ErrorCall, contextDepth);
			}

			if (stackDepth > Configuration.CurrentValue.Limit.MaxDepth)
			{
				return new CallState(Errors.ErrorInvoke, stackDepth);
			}

			if (recursionDepth > Configuration.CurrentValue.Limit.FunctionRecursionLimit)
			{
				return new CallState(Errors.ErrorRecursion, recursionDepth);
			}

			var stripAnsi = attribute.Flags.HasFlag(FunctionFlags.StripAnsi);

			if (!attribute.Flags.HasFlag(FunctionFlags.NoParse))
			{
				refinedArguments = await args
					.ToAsyncEnumerable()
					.Select<EvaluationStringContext, CallState>(async (x, ct) => new CallState(
						stripAnsi
							? MModule.plainText2((await visitor.VisitChildren(x))?.Message ?? MModule.empty())
							: (await visitor.VisitChildren(x))?.Message ?? MModule.empty(), x.Depth()))
					.DefaultIfEmpty(new CallState(MModule.empty(), context.Depth()))
					.ToListAsync();
			}
			else if (attribute.Flags.HasFlag(FunctionFlags.NoParse) && attribute.MaxArgs == 1)
			{
				return new CallState(
					MModule.substring(context.Start.StartIndex, context.Stop.StopIndex - context.Start.StartIndex + 1, src),
					contextDepth,
					null,
					async () => (await visitor.VisitChildren(context) ?? CallState.Empty with { Depth = context.Depth() })
						.Message!);
			}
			else
			{
				refinedArguments = args.Select(x => new CallState(stripAnsi
							? MModule.plainText2(MModule.substring(x.Start.StartIndex,
								context.Stop?.StopIndex is null ? 0 : (x.Stop.StopIndex - x.Start.StartIndex + 1), src))
							: MModule.substring(x.Start.StartIndex,
								context.Stop?.StopIndex is null ? 0 : (x.Stop.StopIndex - x.Start.StartIndex + 1), src),
						x.Depth(), null,
						async () => stripAnsi
							? (await visitor.VisitChildren(x))!.Message
							: MModule.plainText2((await visitor.VisitChildren(x))!.Message)))
					.DefaultIfEmpty(new CallState(MModule.empty(), context.Depth()))
					.ToList();
			}

			// TODO: Consider adding the ParserContexts as Arguments, so that Evaluation can be more optimized.
			var newParser = parser.Push(new ParserState(
				Registers: currentState.Registers,
				IterationRegisters: currentState.IterationRegisters,
				RegexRegisters: currentState.RegexRegisters,
				ExecutionStack: currentState.ExecutionStack,
				CurrentEvaluation: currentState.CurrentEvaluation,
				EnvironmentRegisters: currentState.EnvironmentRegisters,
				ParserFunctionDepth: parser.CurrentState.ParserFunctionDepth + 1,
				Function: name,
				Command: null,
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: refinedArguments.Select((value, i) =>
						new KeyValuePair<string, CallState>(i.ToString(), value))
					.ToDictionary(),
				Executor: currentState.Executor,
				Enactor: currentState.Enactor,
				Caller: currentState.Caller,
				Handle: currentState.Handle
			));

			var result = await function(newParser);

			return result with { Depth = contextDepth };
		}
		catch (Exception ex)
		{
			logger.LogError(ex, nameof(CallFunction));

			var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

			if (executor.IsGod())
			{
				await NotifyService.Notify(executor, $"#-1 INTERNAL SHARPMUSH ERROR:\n{ex}");
			}

			return CallState.Empty;
		}
	}

	private Option<(SharpFunctionAttribute, Func<IMUSHCodeParser, ValueTask<CallState>>)>
		DiscoverBuiltInFunction(string name)
	{
		if (!parser.FunctionLibrary.TryGetValue(name, out var result) || !result.IsSystem)
			return new None();

		return (result.LibraryInformation.Attribute,
			p => (ValueTask<CallState>)result.LibraryInformation.Function.Method.Invoke(null,
				[p, result.LibraryInformation.Attribute])!);
	}


	/// <summary>
	/// Evaluates the command, with the parser info given.
	/// </summary>
	/// <remarks>
	/// Call State is expected to be empty on return. 
	/// But if one wanted to implement a @pipe command that can pass a result from say, a @dig command, 
	/// there would be a need for some way of passing on secondary data.
	/// </remarks>
	/// <param name="src">Original string</param>
	/// <param name="context">Command Context</param>
	/// <param name="isCommandList">Whether this command is part of a command list.</param>
	/// <param name="visitChildren">Parser function to visit children.</param>
	/// <returns>An empty Call State</returns>
	public async ValueTask<Option<CallState>> EvaluateCommands(
		MString src,
		CommandContext context,
		bool isCommandList,
		Func<IRuleNode, ValueTask<CallState?>> visitChildren)
	{
		try
		{
			var firstCommandMatch = context.evaluationString();

			if (firstCommandMatch?.SourceInterval.Length is null or 0)
				return new None();

			var command = firstCommandMatch.GetText();

			var spaceIndex = command.AsSpan().IndexOf(' ');
			if (spaceIndex != -1)
			{
				command = command[..spaceIndex];
			}

			if (parser.CurrentState.Handle is not null && command != "IDLE")
			{
				ConnectionService.Update(parser.CurrentState.Handle.Value, "LastConnectionSignal",
					DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
			}

			// Step 1: Check if it's a SOCKET command
			// TODO: Optimize
			var socketCommandPattern = parser.CommandLibrary.Where(x
				=> parser.CurrentState.Handle is not null
				   && x.Value.IsSystem
				   && x.Key.Equals(command, StringComparison.CurrentCultureIgnoreCase)
				   && x.Value.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SOCKET)).ToList();

			if (socketCommandPattern.Any() &&
			    parser.CommandLibrary.TryGetValue(command.ToUpper(), out var librarySocketCommandDefinition))
			{
				return await HandleSocketCommandPattern(parser, src, context, command, socketCommandPattern,
					librarySocketCommandDefinition.LibraryInformation);
			}

			if (parser.CurrentState.Executor is null && parser.CurrentState.Handle is not null)
			{
				await NotifyService.Notify(parser.CurrentState.Handle.Value, "No such command available at login.");
				return new None();
			}

			// Step 2a: Check for the channel single-token command.

			// TODO: Better channel name matching within the channel helper.
			if (command[..1] == Configuration.CurrentValue.Chat.ChatTokenAlias.ToString())
			{
				var channels = await Mediator.Send(new GetChannelListQuery());
				var check = command[1..];

				var channel = await channels.FirstOrDefaultAsync(x =>
					x.Name.ToPlainText().StartsWith(check, StringComparison.CurrentCultureIgnoreCase));

				if (channel is not null && !context.evaluationString().IsEmpty)
				{
					return await HandleChannelCommand(parser, channel, context, src);
				}
			}

			// Step 2b: Check for a single-token command
			// TODO: Optimize
			var singleTokenCommandPattern = parser.CommandLibrary.Where(x
				=> x.Key.Equals(command[..1], StringComparison.CurrentCultureIgnoreCase)
				   && x.Value.IsSystem
				   && x.Value.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SingleToken)).ToList();

			if (singleTokenCommandPattern.Count != 0)
			{
				return await HandleSingleTokenCommandPattern(parser, src, context, command, singleTokenCommandPattern);
			}

			var executorObject = (await parser.CurrentState.ExecutorObject(Mediator)).WithoutNone();
			// Step 3: Check exit Aliases
			if (executorObject.IsContent)
			{
				var locate = await LocateService.Locate(
					parser,
					executorObject,
					executorObject,
					command,
					LocateFlags.ExitsInTheRoomOfLooker
					| LocateFlags.EnglishStyleMatching
					| LocateFlags.ExitsPreference
					| LocateFlags.OnlyMatchTypePreference
					| LocateFlags.FailIfNotPreferred);

				if (locate.IsExit)
				{
					var exit = locate.AsExit;
					return await HandleGoCommandPattern(parser, exit);
				}
			}

			// Step 4: Check if we are setting an attribute: &... -- we're just treating this as a Single Token Command for now.
			// Who would rely on a room alias being & anyway?
			// Step 5: Check @COMMAND in command library

			// TODO: Optimize
			// TODO: Get the Switches and send them along as a list of items!
			var slashIndex = command.AsSpan().IndexOf('/');
			var rootCommand =
				command[..(slashIndex > -1 ? slashIndex : command.Length)];
			var switchString = command[(slashIndex > -1 ? slashIndex : command.Length)..];
			var switches = switchString.Split('/').Where(s => !string.IsNullOrWhiteSpace(s));

			if (parser.CommandLibrary.TryGetValue(rootCommand.ToUpper(), out var libraryCommandDefinition)
			    && !rootCommand.Equals("HUH_COMMAND", StringComparison.CurrentCultureIgnoreCase))
			{
				return await HandleInternalCommandPattern(parser, src, context, rootCommand, switches,
					libraryCommandDefinition.LibraryInformation);
			}

			// Step 6: Check @attribute setting
			// Step 7: Enter Aliases
			// Step 8: Leave Aliases

			// Step 9: User Defined Commands nearby
			// -- This is going to be a very important place to Cache the commands.
			// A caching strategy is going to be reliant on the Attribute Service.
			// Optimistic that the command still exists, until we try and it no longer does?
			// What's the best way to retrieve the Regex or Wildcard pattern and transform it? 
			// It needs to take an area to search in. So this is definitely its own service.
			var nearbyObjects = await Mediator.Send(new GetNearbyObjectsQuery(executorObject.Object().DBRef));

			var userDefinedCommandMatches = await CommandDiscoveryService.MatchUserDefinedCommand(
				parser,
				await nearbyObjects.ToArrayAsync(),
				src);

			if (userDefinedCommandMatches.IsSome())
			{
				return await HandleUserDefinedCommand(parser, userDefinedCommandMatches.AsValue());
			}

			// Step 10: Zone Exit Name and Aliases
			// Step 11: Zone Master User Defined Commands
			// Step 12: User Defined commands on the location itself.
			if (executorObject.IsContent)
			{
				var userDefinedCommandMatchesOnLocation = await CommandDiscoveryService.MatchUserDefinedCommand(
					parser,
					[(await executorObject.AsContent.Location()).WithExitOption()],
					src);

				if (userDefinedCommandMatchesOnLocation.IsSome())
				{
					return await HandleUserDefinedCommand(parser, userDefinedCommandMatchesOnLocation.AsValue());
				}
			}

			// Step 13: User defined commands on the player's personal zone.
			// Step 14: Global Exits
			// Step 15: Global User-defined commands
			var goConfig = Configuration.CurrentValue.Database.MasterRoom;
			var maybeGlobalObject = await Mediator.Send(new GetObjectNodeQuery(new DBRef(Convert.ToInt32(goConfig))));
			var globalObject = maybeGlobalObject.Known();
			var globalObjectContent = (await globalObject.AsContainer.Content(Mediator))
				.Select(x => x.WithRoomOption());

			var userDefinedCommandMatchesOnGlobal = await CommandDiscoveryService.MatchUserDefinedCommand(
				parser,
				[globalObject, .. await globalObjectContent.ToArrayAsync()],
				src);

			if (userDefinedCommandMatchesOnGlobal.IsSome())
			{
				return await HandleUserDefinedCommand(parser, userDefinedCommandMatchesOnGlobal.AsValue());
			}

			// Step 16: HUH_COMMAND is run
			var newParser = parser.Push(parser.CurrentState with
			{
				Command = "HUH_COMMAND",
				Arguments = [],
				Function = null
			});

			var huhCommand = await parser.CommandLibrary["HUH_COMMAND"].LibraryInformation.Command.Invoke(newParser);

			return huhCommand;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, nameof(EvaluateCommands));
			return CallState.Empty;
		}
	}

	private async Task<Option<CallState>> HandleChannelCommand(IMUSHCodeParser prs, SharpChannel channel,
		CommandContext context, MString src)
	{
		var rest = MModule.substring(
			context.evaluationString().Start.StartIndex,
			context.evaluationString().Stop.StopIndex - context.evaluationString().Start.StartIndex + 1,
			src);

		var chatParser = prs.Push(prs.CurrentState with
		{
			Command = "@CHAT",
			Arguments = new Dictionary<string, CallState>
			{
				{ "0", new CallState(channel.Name) },
				{ "1", new CallState(rest) }
			}
		});

		return await chatParser.CommandLibrary["@CHAT"].LibraryInformation.Command.Invoke(chatParser);
	}

	private async Task<Option<CallState>> HandleUserDefinedCommand(
		IMUSHCodeParser prs,
		IEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Dictionary<string, CallState> Arguments)> matches)
	{
		// Step 1: Validate if the command can be evaluated (locks)
		foreach (var (obj, attr, arguments) in matches)
		{
			var newParser = prs.Push(prs.CurrentState with
			{
				CurrentEvaluation = new DBAttribute(obj.Object().DBRef, attr.Name),
				EnvironmentRegisters = arguments,
				Arguments = arguments,
				Function = null,
				Executor = obj.Object().DBRef
			});

			await newParser.CommandListParse(MModule.substring(
				attr.CommandListIndex!.Value,
				MModule.getLength(attr.Value) - attr.CommandListIndex!.Value,
				attr.Value));
		}

		return CallState.Empty;
	}

	private static async ValueTask<Option<CallState>> HandleGoCommandPattern(IMUSHCodeParser prs, SharpExit exit)
	{
		var newParser = prs.Push(prs.CurrentState with
		{
			Command = "GOTO",
			Arguments = new Dictionary<string, CallState>
			{
				{ "0", new CallState(exit.Object.DBRef.ToString(), 0) }
			},
			Function = null
		});

		return await newParser.CommandLibrary["GOTO"].LibraryInformation.Command.Invoke(newParser);
	}

	private static async ValueTask<Option<CallState>> HandleInternalCommandPattern(IMUSHCodeParser prs, MString src,
		CommandContext context, string rootCommand, IEnumerable<string> switches,
		CommandDefinition libraryCommandDefinition)
	{
		var arguments = await ArgumentSplit(prs, src, context, libraryCommandDefinition);

		return await prs.With(state =>
				state with
				{
					Command = rootCommand,
					Switches = switches.Select(x => x.ToUpper()),
					Arguments = arguments
						.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value))
						.ToDictionary(),
					CommandInvoker = libraryCommandDefinition.Command,
					Function = null
				},
			async newParser => await libraryCommandDefinition.Command.Invoke(newParser));
	}

	private static async ValueTask<Option<CallState>> HandleSocketCommandPattern(IMUSHCodeParser prs, MString src,
		CommandContext context, string command,
		IEnumerable<KeyValuePair<string, (CommandDefinition LibraryInformation, bool IsSystem)>> socketCommandPattern,
		(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)
			librarySocketCommandDefinition)
	{
		var arguments = await ArgumentSplit(prs, src, context, librarySocketCommandDefinition);

		return await prs.With(state => state with
		{
			Command = command,
			Arguments = arguments.Select((value, i) =>
					new KeyValuePair<string, CallState>(i.ToString(), value))
				.ToDictionary(),
			Function = null
		}, async newParser => await socketCommandPattern.First().Value.LibraryInformation.Command.Invoke(newParser));
	}

	private static async ValueTask<Option<CallState>> HandleSingleTokenCommandPattern(IMUSHCodeParser prs,
		MString src, CommandContext context, string command,
		IEnumerable<KeyValuePair<string, (CommandDefinition LibraryInformation, bool IsSystem)>> singleTokenCommandPattern)
	{
		var singleRootCommand = command[..1];
		var rest = command[1..];
		var singleLibraryCommandDefinition = singleTokenCommandPattern.Single().Value;

		// TODO: Should Single Commands split? - Getting errors out of this.
		var arguments = await ArgumentSplit(prs, src, context, singleLibraryCommandDefinition.LibraryInformation);

		return await prs.With(state =>
				state with
				{
					Command = singleRootCommand,
					Arguments = ImmutableDictionary<string, CallState>.Empty
						.Add("0", new CallState(rest))
						.AddRange(arguments.Select((value, i) => new KeyValuePair<string, CallState>((i + 1).ToString(), value)))
						.ToDictionary(),
					Function = null
				},
			async newParser => await singleLibraryCommandDefinition.LibraryInformation.Command.Invoke(newParser)
		);
	}

	private static async ValueTask<List<CallState>> ArgumentSplit(IMUSHCodeParser prs, MString src,
		CommandContext context,
		(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)
			libraryCommandDefinition)
	{
		var argCallState = CallState.EmptyArgument;
		var behavior = libraryCommandDefinition.Attribute.Behavior;

		// Do not parse the argument splitting.
		var newNoParseParser = prs.Push(prs.CurrentState with { ParseMode = ParseMode.NoParse });
		var realSubtext = MModule.substring(
			context.evaluationString().Start.StartIndex,
			context.evaluationString().Stop.StopIndex - context.evaluationString().Start.StartIndex + 1,
			src);
		var spaceInContext = MModule.indexOf(realSubtext, MModule.single(" "));

		// command (space) argument(s)
		if (spaceInContext != -1)
		{
			var remainder =
				MModule.substring(spaceInContext + 1, MModule.getLength(realSubtext) - spaceInContext, realSubtext);

			// command arg0 = arg1,still arg 1 
			if (behavior.HasFlag(CommandBehavior.EqSplit) && behavior.HasFlag(CommandBehavior.RSArgs))
			{
				argCallState = await newNoParseParser.CommandEqSplitArgsParse(remainder);
			}
			// command arg0 = arg1,arg2
			else if (behavior.HasFlag(CommandBehavior.EqSplit))
			{
				argCallState = await newNoParseParser.CommandEqSplitParse(remainder);
			}
			// Command arg0,arg1,arg2,arg
			else if (behavior.HasFlag(CommandBehavior.RSArgs))
			{
				argCallState = await newNoParseParser.CommandCommaArgsParse(remainder);
			}
			else
			{
				argCallState = await newNoParseParser.CommandSingleArgParse(remainder);
			}
		}

		List<CallState> arguments = [];

		var eqSplit = libraryCommandDefinition.Attribute.Behavior.HasFlag(CommandBehavior.EqSplit);
		var noParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(CommandBehavior.NoParse);
		var noRsParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(CommandBehavior.RSNoParse);
		var nArgs = argCallState?.Arguments?.Length;

		// TODO: Implement lsargs - but there are no immediate commands that need it.
		if (argCallState is null)
		{
			return arguments;
		}

		if (eqSplit)
		{
			arguments.Add(noParse
				? new CallState(argCallState.Arguments!.FirstOrDefault() ?? MModule.empty(), argCallState.Depth)
				: (await prs.FunctionParse(argCallState.Arguments!.FirstOrDefault() ?? MModule.empty()))!);

			if (nArgs < 2) return arguments;

			if (noRsParse || noParse)
			{
				arguments.AddRange(argCallState.Arguments!
					.Skip(1)
					// TODO: Implement Parsed Message alt
					.Select(x =>
						new CallState(x,
							argCallState.Depth,
							null,
							async () =>
								(await prs.FunctionParse(x))!.Message!)));
			}
			else
			{
				foreach (var argument in argCallState.Arguments!.Skip(1))
				{
					// This is done to avoid allocation with ValueTask.
					arguments.Add((await prs.FunctionParse(argument))!);
				}
			}
		}
		else
		{
			if (noParse)
			{
				arguments.AddRange(argCallState.Arguments!
					.Select(x => new CallState(x, argCallState.Depth)));
			}
			else
			{
				foreach (var argument in argCallState.Arguments!)
				{
					// This is done to avoid allocation with ValueTask.
					arguments.Add((await prs.FunctionParse(argument))!);
				}
			}
		}

		return arguments;
	}


	public override async ValueTask<CallState?> VisitEvaluationString(
		[NotNull] EvaluationStringContext context) => await VisitChildren(context) ?? new CallState(
		MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1), source),
		context.Depth());

	public override async ValueTask<CallState?> VisitExplicitEvaluationString(
		[NotNull] ExplicitEvaluationStringContext context)
	{
		/* var isGenericText = context.beginGenericText() is not null;

		if (!isGenericText)
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, MModule.single(
				$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} :"));
		} */

		return await VisitChildren(context)
		       ?? new CallState(
			       MModule.substring(context.Start.StartIndex,
				       context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1),
				       source),
			       context.Depth());

		/* if (!isGenericText)
		{
			await NotifyService!.Notify(parser.CurrentState.Executor!.Value, MModule.single(
				$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{context.GetText()} => {result.Message}"));
		} */
	}

	public override async ValueTask<CallState?> VisitBracePattern(
		[NotNull] BracePatternContext context)
	{
		_braceDepthCounter++;

		CallState? result;
		var vc = await VisitChildren(context);

		if (_braceDepthCounter <= 1)
		{
			result = vc ?? new CallState(
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex is null
						? 0
						: context.Stop.StopIndex - context.Start.StartIndex + 1, source),
				context.Depth());
		}
		else
		{
			result = vc is not null
				? vc with
				{
					Message = MModule.multiple([
						MModule.single("{"),
						vc.Message,
						MModule.single("}")
					])
				}
				: new CallState(
					MModule.substring(context.Start.StartIndex,
						context.Stop?.StopIndex is null
							? 0
							: context.Stop.StopIndex - context.Start.StartIndex + 1, source),
					context.Depth());
		}

		_braceDepthCounter--;
		return result;
	}

	public override async ValueTask<CallState?> VisitBracketPattern(
		[NotNull] BracketPatternContext context)
	{
		if (parser.CurrentState.ParseMode is not ParseMode.NoParse and not ParseMode.NoEval)
		{
			/*
			await NotifyService!.Notify(parser.CurrentState.Caller!.Value,
				$"#{parser.CurrentState.Caller!.Value.Number}! {new string(' ', parser.CurrentState.ParserFunctionDepth!.Value)}{text} :");
			*/

			var resultQ = await VisitChildren(context)
			              ?? new CallState(
				              MModule.substring(context.Start.StartIndex,
					              context.Stop?.StopIndex is null ? 0 : (context.Stop.StopIndex - context.Start.StartIndex + 1),
					              source),
				              context.Depth());


			return resultQ;
		}

		var result = await VisitChildren(context);
		if (result is null)
		{
			return new CallState(
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex is null
						? 0
						: context.Stop.StopIndex - context.Start.StartIndex + 1, source),
				context.Depth());
		}

		return result with
		{
			Message = MModule.multiple([
				MModule.single("["),
				result.Message,
				MModule.single("]")
			])
		};
	}

	public override async ValueTask<CallState?> VisitGenericText([NotNull] GenericTextContext context)
		=> await VisitChildren(context)
		   ?? new CallState(
			   MModule.substring(context.Start.StartIndex,
				   context.Stop?.StopIndex is null
					   ? 0
					   : context.Stop.StopIndex - context.Start.StartIndex + 1,
				   source),
			   context.Depth());

	public override async ValueTask<CallState?> VisitBeginGenericText(
		[NotNull] BeginGenericTextContext context)
		=> await VisitChildren(context)
		   ?? new CallState(
			   MModule.substring(context.Start.StartIndex,
				   context.Stop?.StopIndex is null
					   ? 0
					   : context.Stop.StopIndex - context.Start.StartIndex + 1,
				   source),
			   context.Depth());

	public override async ValueTask<CallState?> VisitValidSubstitution(
		[NotNull] ValidSubstitutionContext context)
	{
		if (parser.CurrentState.ParseMode is ParseMode.NoParse or ParseMode.NoEval)
		{
			// TODO: This does not work in the case of a QREG with an evaluationstring in it.
			return new CallState("%" + context.GetText());
		}

		var textContents = MModule.single(context.GetText());
		var complexSubstitutionSymbol = context.complexSubstitutionSymbol();
		var simpleSubstitutionSymbol = context.substitutionSymbol();

		if (complexSubstitutionSymbol is not null)
		{
			var state = await VisitChildren(context);
			return await Substitutions.Substitutions.ParseComplexSubstitution(state, parser, AttributeService, Mediator,
				complexSubstitutionSymbol);
		}

		if (simpleSubstitutionSymbol is not null)
		{
			return await Substitutions.Substitutions.ParseSimpleSubstitution(
				simpleSubstitutionSymbol.GetText(),
				parser,
				Mediator,
				AttributeService,
				Configuration,
				simpleSubstitutionSymbol);
		}

		return await VisitChildren(context) ?? new CallState(textContents, context.Depth());
	}

	public override async ValueTask<CallState?> VisitCommand([NotNull] CommandContext context)
	{
		if (parser.CurrentState.ParseMode == ParseMode.NoParse)
		{
			return await VisitChildren(context) ?? new CallState(context.GetText());
		}

		var isCommandList = context.Parent is CommandListContext;

		var result = await EvaluateCommands(source, context, isCommandList, VisitChildren); 
		return result
			.Match<CallState?>(
				x => x,
				_ => CallState.Empty);
	}

	public override async ValueTask<CallState?> VisitStartCommandString(
		[NotNull] StartCommandStringContext context)
	{
		var result = await VisitChildren(context);
		if (result != null)
		{
			return result;
		}

		var text = MModule.substring(
			context.Start.StartIndex,
			context.Stop?.StopIndex is null
				? 0
				: context.Stop.StopIndex - context.Start.StartIndex + 1,
			source);
		return new CallState(text, context.Depth());
	}

	private bool BreakTriggered()
		=> parser.CurrentState.ExecutionStack.TryPeek(out var result) && result.CommandListBreak;

	public override async ValueTask<CallState?> VisitCommandList([NotNull] CommandListContext context)
	{
		var result = await VisitChildrenOrBreak(context, BreakTriggered);

		if (BreakTriggered())
		{
			parser.CurrentState.ExecutionStack.TryPop(out _);
		}

		if (result is not null)
		{
			return result;
		}

		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null
				? 0
				: context.Stop.StopIndex - context.Start.StartIndex + 1,
			source);
		return new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitStartSingleCommandString(
		[NotNull] StartSingleCommandStringContext context)
	{
		var result = await VisitChildren(context);
		if (result is not null)
		{
			return result;
		}

		var text = MModule.substring(context.Start.StartIndex,
			context.Stop?.StopIndex is null
				? 0
				: context.Stop.StopIndex - context.Start.StartIndex + 1,
			source);
		return new CallState(text, context.Depth());
	}

	public override async ValueTask<CallState?> VisitEscapedText([NotNull] EscapedTextContext context)
		=> await VisitChildren(context)
		   ?? new CallState(
			   MModule.substring(
				   context.Start.StartIndex + 1,
				   context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
				   source), context.Depth());

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.singleCommandArg"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override async ValueTask<CallState?> VisitSingleCommandArg([NotNull] SingleCommandArgContext context)
	{
		var visitedChildren = await VisitChildren(context);

		return new CallState(
			Message: null,
			context.Depth(),
			Arguments:
			[
				visitedChildren?.Message ??
				MModule.substring(context.Start.StartIndex,
					context.Stop?.StopIndex is null
						? 0
						: context.Stop.StopIndex - context.Start.StartIndex + 1, source)
			],
			ParsedMessage: () => ValueTask.FromResult<MString?>(null));
	}

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startEqSplitCommandArgs"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override async ValueTask<CallState?> VisitStartEqSplitCommandArgs(
		[NotNull] StartEqSplitCommandArgsContext context)
	{
		var baseArg = await VisitChildren(context.singleCommandArg());
		var commaArgs = await VisitChildren(context.commaCommandArgs());
		// Log.Logger.Information("VisitEqsplitCommandArgs: C1: {Text} - C2: {Text2}", baseArg?.ToString(), commaArgs?.ToString());
		return new CallState(null,
			context.Depth(),
			[baseArg!.Message!, .. commaArgs?.Arguments ?? []],
			() => ValueTask.FromResult<MString?>(null));
	}

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startEqSplitCommand"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override async ValueTask<CallState?> VisitStartEqSplitCommand(
		[NotNull] StartEqSplitCommandContext context)
	{
		var singleCommandArg = context.singleCommandArg();
		var baseArg = await VisitChildren(singleCommandArg[0]);
		var rsArg = singleCommandArg.Length > 1 ? await VisitChildren(singleCommandArg[1]) : null;
		MString[] args = singleCommandArg.Length > 1
			? [baseArg!.Message!, rsArg!.Message!]
			: [baseArg!.Message!];

		// Log.Logger.Information("VisitEqSplitCommand: C1: {Text} - C2: {Text2}", baseArg?.ToString(), rsArg?.ToString());
		return new CallState(
			null,
			context.Depth(),
			args,
			() => ValueTask.FromResult<MString?>(null));
	}

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.commaCommandArgs"/>.
	/// <para>
	/// The default implementation returns the result of calling <see cref="AbstractParseTreeVisitor{Result}.VisitChildren(IRuleNode)"/>
	/// on <paramref name="context"/>.
	/// </para>
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override async ValueTask<CallState?> VisitCommaCommandArgs(
		[NotNull] CommaCommandArgsContext context) =>
		new(
			null,
			context.Depth(),
			(await VisitChildren(context))!.Arguments,
			() => ValueTask.FromResult<MString?>(null));

	public override async ValueTask<CallState?> VisitComplexSubstitutionSymbol(
		[NotNull] ComplexSubstitutionSymbolContext context)
	{
		if (context.ChildCount > 1)
			return await VisitChildren(context);

		if (context.REG_NUM() is not null || context.ITEXT_NUM() is not null || context.STEXT_NUM() is not null)
			return new CallState(
				MModule.substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1,
					source), context.Depth());

		return new CallState(
			MModule.substring(context.Start.StartIndex,
				context.Stop?.StopIndex is null 
					? 0 
					: context.Stop.StopIndex - context.Start.StartIndex + 1, 
				source),
			context.Depth());
	}
}