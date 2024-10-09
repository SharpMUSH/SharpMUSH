using System.Diagnostics;
using Antlr4.Runtime.Tree;
using DotNext.Collections.Generic;
using OneOf.Monads;
using SharpMUSH.Library.ParserInterfaces;
using System.Reflection;
using static SharpMUSHParser;

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
			return new OneOf.Monads.None();

		var command = firstCommandMatch.GetText();

		if (parser.CurrentState.Handle != null && command != "IDLE")
		{
			parser.ConnectionService.Update(parser.CurrentState.Handle, "LastConnectionSignal",
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
		}

		// Step 1: Check if it's a SOCKET command
		// TODO: Optimize
		var socketCommandPattern = _commandLibrary.Where(x
			=> parser.CurrentState.Handle != null
			   && x.Key.Equals(command, StringComparison.CurrentCultureIgnoreCase)
			   && x.Value.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.SOCKET));

		if (socketCommandPattern.Any() &&
		    _commandLibrary.TryGetValue(command.ToUpper(), out var librarySocketCommandDefinition))
		{
			return await HandleSocketCommandPattern(parser, source, context, command, socketCommandPattern,
				librarySocketCommandDefinition);
		}

		if (parser.CurrentState.Executor == null && parser.CurrentState.Handle != null)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Handle, "No such command available at login.");
			return new OneOf.Monads.None();
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

		// Step 3: Check room Aliases
		// Step 4: Check if we are setting an attribute: &... -- we're just treating this as a Single Token Command for now.
		// Who would rely on a room alias being & anyway?
		// Step 5: Check @COMMAND in command library

		// TODO: Optimize
		// TODO: Evaluate Command -- commands that run more commands are always NoParse for the arguments it's relevant for.
		// TODO: Get the Switches and send them along as a list of items!
		var evaluatedCallContextAsString = MModule.plainText((await visitChildren(firstCommandMatch))!.Message!);
		var slashIndex = evaluatedCallContextAsString.IndexOf(SLASH);
		var rootCommand =
			evaluatedCallContextAsString[..(slashIndex > -1 ? slashIndex : evaluatedCallContextAsString.Length)];
		var swtch = evaluatedCallContextAsString[(slashIndex > -1 ? slashIndex : evaluatedCallContextAsString.Length) ..];
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
		// Step 10: Zone Exit Name and Aliases
		// Step 11: Zone Master User Defined Commands
		// Step 12: User Defined commands on the location itself.
		// Step 13: User defined commands on the player's personal zone.
		// Step 14: Global Exits
		// Step 15: Global User-defined commands
		// Step 16: HUH_COMMAND is run

		// HUH_COMMAND!
		var huhCommand = await _commandLibrary["HUH_COMMAND"].Function.Invoke(
			parser.Push(parser.CurrentState with
			{
				Command = "HUH_COMMAND",
				Arguments = [],
				Function = null
			}));

		parser.Pop();
		return huhCommand;
	}

	private static async ValueTask<Option<CallState>> HandleInternalCommandPattern(IMUSHCodeParser parser, MString source,
		CommandContext context, string rootCommand, IEnumerable<string> switches,
		(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)
			libraryCommandDefinition)
	{
		var arguments = await ArgumentSplit(parser, source, context, libraryCommandDefinition);

		var result = await libraryCommandDefinition.Function.Invoke(
			parser.Push(parser.CurrentState with
			{
				Command = rootCommand,
				Switches = switches,
				Arguments = arguments,
				Function = null
			}));

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

		// Run as Socket Command. 
		var result = await socketCommandPattern.First().Value.Function.Invoke(parser.Push(
			parser.CurrentState with { Command = command, Arguments = arguments, Function = null }));

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
		var arguments = await ArgumentSplit(parser, source, context, singleLibraryCommandDefinition);

		var result = await singleLibraryCommandDefinition.Function.Invoke(
			parser.Push(
				parser.CurrentState with
				{
					Command = singleRootCommand,
					Arguments = [new CallState(rest), .. arguments],
					Function = null
				}
			)
		);

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
		var isNoParse = behavior.HasFlag(Definitions.CommandBehavior.NoParse);

		if (isNoParse)
		{
			parser.Push(parser.CurrentState with { ParseMode = ParseMode.NoParse });
		}

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

		List<CallState> arguments = [];

		var eqSplit = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.EqSplit);
		var noParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.NoParse);
		var noRsParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.RSNoParse);
		var nArgs = argCallState?.Arguments?.Length;

		// TODO: Implement lsargs - but there are no immediate commands that need it.

		if (argCallState == null)
		{
			if (isNoParse)
			{
				parser.Pop();
			}

			return arguments;
		}

		if (eqSplit)
		{
			arguments.Add(noParse
				? new CallState(argCallState.Arguments!.FirstOrDefault() ?? MModule.empty(), argCallState.Depth)
				: (await parser.FunctionParse(argCallState.Arguments!.FirstOrDefault() ?? MModule.empty()))!);

			if (nArgs > 1)
			{
				arguments.AddRange(noRsParse
					? argCallState.Arguments![1..].Select(x => new CallState(x, argCallState.Depth))
					: argCallState.Arguments![1..].Select(x => parser.FunctionParse(x).AsTask().Result).Select(x => x!));
			}
		}

		arguments.AddRange(noParse
			? argCallState.Arguments!.Select(x => new CallState(x, argCallState.Depth))
			: argCallState.Arguments!.Select(x => parser.FunctionParse(x).AsTask().Result).Select(x => x!));

		if (isNoParse)
		{
			parser.Pop();
		}

		return arguments;
	}
}