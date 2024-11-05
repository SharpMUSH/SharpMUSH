using Antlr4.Runtime.Tree;
using DotNext.Collections.Generic;
using SharpMUSH.Library.ParserInterfaces;
using System.Reflection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using static SharpMUSHParser;
using SharpMUSH.Library.DiscriminatedUnions;
using OneOf.Types;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{
	private const char SLASH = '/';

	private static readonly
		Dictionary<string, (SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)>
		_commandLibrary = [];

	private static readonly Dictionary<string, (MethodInfo Method, SharpCommandAttribute Attribute)>
		_knownBuiltInCommands =
			typeof(Commands)
				.GetMethods()
				.Select(m => (Method: m,
					Attribute: m.GetCustomAttribute(typeof(SharpCommandAttribute), false) as SharpCommandAttribute))
				.Where(x => x.Attribute is not null)
				.Select(y =>
					new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(y.Attribute!.Name,
						(y.Method, y.Attribute!)))
				.ToDictionary();

	static Commands()
		=> _commandLibrary.AddAll(_knownBuiltInCommands.Select(knownCommand =>
			new KeyValuePair<string, (SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>>
				Function)>(
				key: knownCommand.Key,
				value: (knownCommand.Value.Attribute,
					p => (ValueTask<Option<CallState>>)knownCommand.Value.Method.Invoke(null,
						[p, knownCommand.Value.Attribute])!))));

	/// <summary>
	/// Evaluates the command, with the parser info given.
	/// </summary>
	/// <remarks>
	/// Call State is expected to be empty on return. 
	/// But if one wanted to implement an @pipe command that can pass a result from say, an @dig command, 
	/// there would be a need for some way of passing on secondary data.
	/// </remarks>
	/// <param name="parser">Parser with state.</param>
	/// <param name="context">Command Context</param>
	/// <param name="visitChildren">Parser function to visit children.</param>
	/// <returns>An empty Call State</returns>
	public static async ValueTask<Option<CallState>> EvaluateCommands(IMUSHCodeParser parser, MString source,
		CommandContext context,
		Func<IRuleNode, ValueTask<CallState?>> visitChildren)
	{
		var firstCommandMatch = context.firstCommandMatch();

		if (firstCommandMatch?.SourceInterval.Length is null or 0)
			return new None();

		var command = firstCommandMatch.GetText();

		if (parser.CurrentState.Handle is not null && command != "IDLE")
		{
			parser.ConnectionService.Update(parser.CurrentState.Handle, "LastConnectionSignal",
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
		}

		// Step 1: Check if it's a SOCKET command
		// TODO: Optimize
		var socketCommandPattern = _commandLibrary.Where(x
			=> parser.CurrentState.Handle is not null
				 && x.Key.Equals(command, StringComparison.CurrentCultureIgnoreCase)
				 && x.Value.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.SOCKET));

		if (socketCommandPattern.Any() &&
				_commandLibrary.TryGetValue(command.ToUpper(), out var librarySocketCommandDefinition))
		{
			return await HandleSocketCommandPattern(parser, source, context, command, socketCommandPattern,
				librarySocketCommandDefinition);
		}

		if (parser.CurrentState.Executor is null && parser.CurrentState.Handle is not null)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Handle, "No such command available at login.");
			return new None();
		}

		// Step 2: Check for a single-token command
		// TODO: Optimize
		var singleTokenCommandPattern = _commandLibrary.Where(x
			=> x.Key.Equals(command[..1], StringComparison.CurrentCultureIgnoreCase) &&
				 x.Value.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.SingleToken));

		if (singleTokenCommandPattern.Any())
		{
			return await HandleSingleTokenCommandPattern(parser, source, context, command, singleTokenCommandPattern);
		}

		var executorObject = parser.CurrentState.ExecutorObject(parser.Database).WithoutNone();
		// Step 3: Check exit Aliases
		if (executorObject.IsContent)
		{
			var what = executorObject.AsContent;
			var locate = await parser.LocateService.Locate(
				parser,
				executorObject,
				executorObject,
				command,
				LocateFlags.ExitsInTheRoomOfLooker);
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
		var slashIndex = command.IndexOf(SLASH);
		var rootCommand =
			command[..(slashIndex > -1 ? slashIndex : command.Length)];
		var swtch = command[(slashIndex > -1 ? slashIndex : command.Length)..];
		var switches = swtch.Split(SLASH).Where(s => !string.IsNullOrWhiteSpace(s));

		if (_commandLibrary.TryGetValue(rootCommand.ToUpper(), out var libraryCommandDefinition)
				&& !rootCommand.Equals("HUH_COMMAND", StringComparison.CurrentCultureIgnoreCase))
		{
			return await HandleInternalCommandPattern(parser, source, context, rootCommand, switches,
				libraryCommandDefinition);
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
		var nearbyObjects = await parser.Database.GetNearbyObjectsAsync(executorObject.Object().DBRef);

		Stopwatch sw = Stopwatch.StartNew();
		var userDefinedCommandMatches = parser.CommandDiscoveryService.MatchUserDefinedCommand(
			parser,
			nearbyObjects,
			source);
		sw.Stop();
		
		await parser.NotifyService.Notify(parser.CurrentState.Handle!, string.Format("Time taken: {0}ms", sw.Elapsed.TotalMilliseconds));


		if (userDefinedCommandMatches.IsSome())
		{
			sw = Stopwatch.StartNew();
			var res = await HandleUserDefinedCommand(parser, userDefinedCommandMatches.AsValue());
			await parser.NotifyService.Notify(parser.CurrentState.Handle!, string.Format("Time taken: {0}ms", sw.Elapsed.TotalMilliseconds));
			return res;
		}


		// Step 10: Zone Exit Name and Aliases
		// Step 11: Zone Master User Defined Commands
		// Step 12: User Defined commands on the location itself.
		// Step 13: User defined commands on the player's personal zone.
		// Step 14: Global Exits
		// Step 15: Global User-defined commands
		// Step 16: HUH_COMMAND is run

		var newParser = parser.Push(parser.CurrentState with
		{
			Command = "HUH_COMMAND",
			Arguments = [],
			Function = null
		});

		var huhCommand = await _commandLibrary["HUH_COMMAND"].Function.Invoke(newParser);

		parser.Pop();
		return huhCommand;
	}

	private static async Task<Option<CallState>> HandleUserDefinedCommand(
		IMUSHCodeParser parser,
		IEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Dictionary<string, CallState> Arguments)> matches)
	{
		// Step 1: Validate if the command can be evaluated (locks)
		foreach (var (Obj, Attr, Arguments) in matches)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				CurrentEvaluation = new DBAttribute(Obj.Object().DBRef, Attr.Name),
				Arguments = Arguments,
				Function = null
			});

			await newParser.CommandListParse(MModule.substring(
				Attr.CommandListIndex!.Value, 
				MModule.getLength(Attr.Value) - Attr.CommandListIndex!.Value, 
				Attr.Value));

			parser.Pop();
		}

		return CallState.Empty;
	}

	private static async ValueTask<Option<CallState>> HandleGoCommandPattern(IMUSHCodeParser parser, SharpExit exit)
	{
		var parseState = parser.Push(parser.CurrentState with
		{
			Command = "GOTO",
			Arguments = new() { { "0", new CallState(exit.Object.DBRef.ToString(), 0) } },
			Function = null
		});
		var result = await _commandLibrary.Single(x => x.Key == "GOTO").Value.Function.Invoke(parseState);
		parser.Pop();
		return result;
	}

	private static async ValueTask<Option<CallState>> HandleInternalCommandPattern(IMUSHCodeParser parser, MString source,
		CommandContext context, string rootCommand, IEnumerable<string> switches,
		(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)
			libraryCommandDefinition)
	{
		var arguments = await ArgumentSplit(parser, source, context, libraryCommandDefinition);

		var parseState = parser.Push(parser.CurrentState with
		{
			Command = rootCommand,
			Switches = switches,
			Arguments = arguments.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value)).ToDictionary(),
			Function = null
		});
		var result = await libraryCommandDefinition.Function.Invoke(parseState);

		parser.Pop();
		return result;
	}

	private static async ValueTask<Option<CallState>> HandleSocketCommandPattern(IMUSHCodeParser parser, MString source,
		CommandContext context, string command,
		IEnumerable<KeyValuePair<string, (SharpCommandAttribute Attribute,
			Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)>> socketCommandPattern,
		(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)
			librarySocketCommandDefinition)
	{
		var arguments = await ArgumentSplit(parser, source, context, librarySocketCommandDefinition);

		var newParser = parser.Push(parser.CurrentState with
		{
			Command = command,
			Arguments = arguments.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value)).ToDictionary(),
			Function = null
		});

		// Run as Socket Command. 
		var result = await socketCommandPattern.First().Value.Function.Invoke(newParser);

		parser.Pop();
		return result;
	}

	private static async ValueTask<Option<CallState>> HandleSingleTokenCommandPattern(IMUSHCodeParser parser,
		MString source, CommandContext context, string command,
		IEnumerable<KeyValuePair<string, (SharpCommandAttribute Attribute,
			Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)>> singleTokenCommandPattern)
	{
		var singleRootCommand = command[..1];
		var rest = command[1..];
		var singleLibraryCommandDefinition = singleTokenCommandPattern.Single().Value;
		// TODO: Should Single Commands split? - Getting errors out of this.
		var arguments = await ArgumentSplit(parser, source, context, singleLibraryCommandDefinition);

		var newParser = parser.Push(
			parser.CurrentState with
			{
				Command = singleRootCommand,
				Arguments = ImmutableDictionary<string, CallState>.Empty
					.Add("0", new CallState(rest))
					.AddRange(arguments.Select((value, i) => new KeyValuePair<string, CallState>((i + 1).ToString(), value)))
					.ToDictionary(),
				Function = null
			}
		);

		var result = await singleLibraryCommandDefinition.Function.Invoke(newParser);

		parser.Pop();
		return result;
	}

	private static async ValueTask<List<CallState>> ArgumentSplit(IMUSHCodeParser parser, MString source,
		CommandContext context,
		(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)
			libraryCommandDefinition)
	{
		var argCallState = CallState.EmptyArgument;
		var behavior = libraryCommandDefinition.Attribute.Behavior;

		// Do not parse the argument splitting.
		parser.Push(parser.CurrentState with { ParseMode = ParseMode.NoParse });

		// command (space) argument(s)
		if (context.children.Count > 1)
		{
			var start = context.commandRemainder().Start.StartIndex;
			var len = context.commandRemainder().Stop.StopIndex - context.commandRemainder().Start.StartIndex + 1;

			// command arg0 = arg1,still arg 1 
			if (behavior.HasFlag(Definitions.CommandBehavior.EqSplit) && behavior.HasFlag(Definitions.CommandBehavior.RSArgs))
			{
				argCallState = await parser.CommandEqSplitArgsParse(MModule.substring(start, len, source));
			}
			// command arg0 = arg1,arg2
			else if (behavior.HasFlag(Definitions.CommandBehavior.EqSplit))
			{
				argCallState = await parser.CommandEqSplitParse(MModule.substring(start, len, source));
			}
			// Command arg0,arg1,arg2,arg
			else if (behavior.HasFlag(Definitions.CommandBehavior.RSArgs))
			{
				argCallState = await parser.CommandCommaArgsParse(MModule.substring(start, len, source));
			}
			else
			{
				argCallState = await parser.CommandSingleArgParse(MModule.substring(start, len, source));
			}
		}

		// Pop the NoParse state.
		parser.Pop();

		List<CallState> arguments = [];

		var eqSplit = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.EqSplit);
		var noParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.NoParse);
		var noRsParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.RSNoParse);
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
				: (await parser.FunctionParse(argCallState.Arguments!.FirstOrDefault() ?? MModule.empty()))!);

			if (nArgs < 2) return arguments;
			
			if (noRsParse || noParse)
			{
				arguments.AddRange(argCallState.Arguments!
					.Skip(1)
					.Select(x => new CallState(x, argCallState.Depth)));
			}
			else
			{
				foreach (var argument in argCallState.Arguments!.Skip(1))
				{
					// This is done to avoid allocation with ValueTask.
					arguments.Add((await parser.FunctionParse(argument))!);
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
					arguments.Add((await parser.FunctionParse(argument))!);
				}
			}
		}

		return arguments;
	}
}