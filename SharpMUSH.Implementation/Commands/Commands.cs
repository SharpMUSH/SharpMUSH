using Antlr4.Runtime.Tree;
using OneOf.Monads;
using SharpMUSH.Library.ParserInterfaces;
using System.Reflection;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		private const char SLASH = '/';

		private static readonly Dictionary<string, (SharpCommandAttribute Attribute, Func<IMUSHCodeParser, Option<CallState>> Function)> _commandLibrary = [];

		private static readonly Dictionary<string, (MethodInfo Method, SharpCommandAttribute Attribute)> _knownBuiltInCommands =
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
		{
			foreach (var knownCommand in _knownBuiltInCommands)
			{
				_commandLibrary.Add(knownCommand.Key,
					(knownCommand.Value.Attribute,
						p =>
							(Option<CallState>)knownCommand.Value.Method.Invoke(null, [p, knownCommand.Value.Attribute])!));
			}
		}

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
		public static Option<CallState> EvaluateCommands(IMUSHCodeParser parser, CommandContext context,
			Func<IRuleNode, CallState?> visitChildren)
		{
			var firstCommandMatch = context.firstCommandMatch();

			if (firstCommandMatch == null) return new OneOf.Monads.None();

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
				var arguments = ArgumentSplit(parser, context, librarySocketCommandDefinition);

				// Run as Socket Command. 
				var result = socketCommandPattern.First().Value.Function.Invoke(parser.Push(
					parser.CurrentState with { Command = command, Arguments = arguments, Function = null }));

				parser.Pop();
				return result;
			}

			if (parser.CurrentState.Executor == null && parser.CurrentState.Handle != null)
			{
				parser.NotifyService.Notify(parser.CurrentState.Handle, "No such command available at login.");
				return new OneOf.Monads.None();
			}

			// Step 2: Check for a single-token command
			// TODO: Optimize
			var singleTokenCommandPattern = _commandLibrary.Where(x
				=> x.Key.Equals(command[..1], StringComparison.CurrentCultureIgnoreCase) &&
					x.Value.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.SingleToken));

			if (singleTokenCommandPattern.Any())
			{
				// Run single token command
				var singleRootCommand = command[..1];
				var rest = command[1..];
				var singleLibraryCommandDefinition = singleTokenCommandPattern.Single().Value;
				var arguments = ArgumentSplit(parser, context, singleLibraryCommandDefinition);

				var result = singleLibraryCommandDefinition.Function.Invoke(parser.Push(
					parser.CurrentState with { Command = singleRootCommand, Arguments = [new CallState(rest), .. arguments], Function = null }));

				parser.Pop();
				return result;
			}

			// Step 3: Check room Aliases
			// Step 4: Check if we are setting an attribute: &... -- we're just treating this as a Single Token Command for now.
			// Who would rely on a room alias being & anyway?
			// Step 5: Check @COMMAND in command library

			// TODO: Optimize
			// TODO: Evaluate Command -- commands that run more commands are always NoParse for the arguments it's relevant for.
			// TODO: Get the Switches and send them along as a list of items!
			var evaluatedCallContextAsString = MModule.plainText(visitChildren(firstCommandMatch)!.Message!);
			var slashIndex = evaluatedCallContextAsString.IndexOf(SLASH);
			var rootCommand = evaluatedCallContextAsString[..(slashIndex > -1 ? slashIndex : evaluatedCallContextAsString.Length)];

			// TODO: Too many ifs. This needs to be split out.
			if (_commandLibrary.TryGetValue(rootCommand.ToUpper(), out var libraryCommandDefinition)
				&& rootCommand.ToUpper() != "HUH_COMMAND")
			{
				var arguments = ArgumentSplit(parser, context, libraryCommandDefinition);

				var result = libraryCommandDefinition.Function.Invoke(
					parser.Push(parser.CurrentState with
					{
						Command = rootCommand,
						Arguments = arguments,
						Function = null
					}));

				parser.Pop();
				return result;
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
			var huhCommand = _commandLibrary["HUH_COMMAND"].Function.Invoke(
				parser.Push(parser.CurrentState with
				{
					Command = "HUH_COMMAND",
					Arguments = [],
					Function = null
				}));

			parser.Pop();
			return huhCommand;
		}

		private static List<CallState> ArgumentSplit(IMUSHCodeParser parser, CommandContext context,
			(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, Option<CallState>> Function) libraryCommandDefinition)
		{
			var argCallState = CallState.EmptyArgument;

			// command (space) argument(s)
			if (context.children.Count > 1)
			{
				argCallState = libraryCommandDefinition.Attribute.Behavior switch
				{
					// command arg0 = arg1,still arg 1 
					Definitions.CommandBehavior.EqSplit | Definitions.CommandBehavior.RSArgs 
						=> parser.CommandEqSplitArgsParse(context.children[2].GetText()),
					// command arg0 = arg1,arg2
					Definitions.CommandBehavior.EqSplit 
						=> parser.CommandEqSplitParse(context.children[2].GetText())!,
					// Command arg0,arg1,arg2,arg
					Definitions.CommandBehavior.RSArgs 
						=> parser.CommandCommaArgsParse(context.children[2].GetText())!,
					_ => parser.CommandSingleArgParse(context.children[2].GetText())!
				};
			}

			List<CallState> arguments = [];

			var eqSplit = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.EqSplit);
			var noParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.NoParse);
			var noRSParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.RSNoParse);
			var nArgs = argCallState?.Arguments?.Length;

			// TODO: Implement lsargs - but there are no immediate commands that need it.

			if (argCallState == null) return arguments;

			if (eqSplit)
			{
				arguments.Add(noParse
					? new CallState(argCallState.Arguments!.FirstOrDefault() ?? string.Empty, argCallState.Depth)
					: parser.FunctionParse(argCallState.Arguments!.FirstOrDefault() ?? string.Empty)!);

				if (nArgs > 1)
				{
					arguments.AddRange(noRSParse
						? argCallState.Arguments![1..].Select(x => new CallState(x, argCallState.Depth))
						: argCallState.Arguments![1..].Select(parser.FunctionParse).Select(x => x!));
				}
			}

			arguments.AddRange(noParse
				? argCallState.Arguments!.Select(x => new CallState(x, argCallState.Depth))
				: argCallState.Arguments!.Select(parser.FunctionParse).Select(x => x!));

			return arguments;
		}
	}
}