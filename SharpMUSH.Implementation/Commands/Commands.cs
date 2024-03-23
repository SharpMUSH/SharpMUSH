using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using OneOf.Monads;
using OneOf.Types;
using System.Reflection;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		private const char Space = ' ';
		private const char Slash = '/';
		private static readonly Dictionary<string, (SharpCommandAttribute Attribute, Func<Parser, Option<CallState>> Function)> _commandLibrary = [];
		private static readonly Dictionary<string, (MethodInfo Method, SharpCommandAttribute Attribute)> _knownBuiltInCommands = typeof(Commands)
			.GetMethods()
			.Select(m => (Method: m, Attribute: m.GetCustomAttribute(typeof(SharpCommandAttribute), false) as SharpCommandAttribute))
			.Where(x => x.Attribute is not null)
			.Select(y => new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(y.Attribute!.Name, (y.Method, y.Attribute!)))
			.ToDictionary();

		static Commands()
		{
			foreach (var knownCommand in _knownBuiltInCommands)
			{
				_commandLibrary.Add(knownCommand.Key, (knownCommand.Value.Attribute, new Func<Parser, Option<CallState>>(p => (Option<CallState>)knownCommand.Value.Method.Invoke(null, [p, knownCommand.Value.Attribute])!)));
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
		public static Option<CallState> EvaluateCommands(Parser parser, CommandContext context, Func<IRuleNode, CallState?> visitChildren)
		{
			var firstCommandMatch = context.firstCommandMatch();
			var conText = context.GetText();

			if (firstCommandMatch == null)
			{
				return new OneOf.Monads.None();
			}

			var command = firstCommandMatch.GetText();

			if (parser.CurrentState.Handle != null && command != "IDLE")
			{
				parser.ConnectionService.Update(parser.CurrentState.Handle, "LastConnectionSignal", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
			}

			// Step 1: Check if it's a SOCKET command
			// TODO: Optimize
			var socketCommandPattern = _commandLibrary.Where(x =>
				parser.CurrentState.Handle != null &&
				(x.Key.Equals(command, StringComparison.CurrentCultureIgnoreCase)) &&
				((x.Value.Attribute.Behavior & Definitions.CommandBehavior.SOCKET) == Definitions.CommandBehavior.SOCKET));

			if (_commandLibrary.TryGetValue(command.ToUpper(), out var librarySocketCommandDefinition))
			{
				var arguments = ArgumentSplit(parser, context, librarySocketCommandDefinition);

				// Run as Socket Command.
				var result = socketCommandPattern.First().Value.Function.Invoke(parser.Push(new Parser.ParserState(
					Registers: parser.CurrentState.Registers,
					CurrentEvaluation: parser.CurrentState.CurrentEvaluation,
					Command: command,
					Arguments: arguments,
					Function: null,
					Executor: parser.CurrentState.Executor,
					Enactor: parser.CurrentState.Enactor,
					Caller: parser.CurrentState.Caller,
					Handle: parser.CurrentState.Handle
				)));

				parser.Pop();
				return result;
			}

			if (parser.CurrentState.Executor == null && parser.CurrentState.Handle != null)
			{
				parser.NotifyService.Notify(parser.CurrentState.Handle, "No such command available at login.");
			}

			// Step 2: Check for a single-token command
			// TODO: Optimize
			var singleTokenCommandPattern = _commandLibrary.Where(x =>
				(x.Key.Equals(command[..1], StringComparison.CurrentCultureIgnoreCase)) &&
				((x.Value.Attribute.Behavior & Definitions.CommandBehavior.SingleToken) == Definitions.CommandBehavior.SingleToken));

			if (singleTokenCommandPattern.Any())
			{
				var rest = command[1..];
				// Run single token command
				throw new NotImplementedException();
			}

			// Step 3: Check room Aliases
			// Step 4: Check if we are setting an attribute: &...
			// Step 5: Check @COMMAND in command library

			// TODO: Optimize
			// TODO: Evaluate Command -- commands that run more commands are always NoParse for the arguments it's relevant for.
			// TODO: Get the Switches and send them along as a list of items!
			var evaluatedCallContextAsString = MModule.plainText(visitChildren(firstCommandMatch)!.Message!);
			var slashIndex = evaluatedCallContextAsString.IndexOf(Slash);
			var rootCommand = evaluatedCallContextAsString[..(slashIndex > -1 ? slashIndex : evaluatedCallContextAsString.Length)];

			// TODO: Too many ifs. This needs to be split out.
			if (_commandLibrary.TryGetValue(rootCommand.ToUpper(), out var libraryCommandDefinition))
			{
				var arguments = ArgumentSplit(parser, context, libraryCommandDefinition);

				var result = libraryCommandDefinition.Function.Invoke(parser.Push(new Parser.ParserState(
					Registers: parser.CurrentState.Registers,
					CurrentEvaluation: parser.CurrentState.CurrentEvaluation,
					Command: rootCommand,
					Arguments: arguments,
					Function: null,
					Executor: parser.CurrentState.Executor,
					Enactor: parser.CurrentState.Enactor,
					Caller: parser.CurrentState.Caller,
					Handle: parser.CurrentState.Handle
				)));

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

			// TODO: Create a HUH_COMMAND
			throw new NotImplementedException();
		}

		private static List<CallState> ArgumentSplit(Parser parser, CommandContext context, (SharpCommandAttribute Attribute, Func<Parser, Option<CallState>> Function) libraryCommandDefinition)
		{

			// command (space) argument(s)
			if (context.children.Count > 1)
			{
				// command arg0 = arg1 ,still arg 1 
				if ((libraryCommandDefinition.Attribute.Behavior & (Definitions.CommandBehavior.EqSplit | Definitions.CommandBehavior.RSArgs)) != 0)
				{
					_ = parser.CommandEqSplitArgsParse(context.children[2].GetText());
				}
				// command arg0 = arg1,arg2
				else if ((libraryCommandDefinition.Attribute.Behavior & Definitions.CommandBehavior.EqSplit) != 0)
				{
					_ = parser.CommandEqSplitParse(context.children[2].GetText());
				}
				// Command arg0,arg1,arg2,arg
				else if ((libraryCommandDefinition.Attribute.Behavior & Definitions.CommandBehavior.RSArgs) != 0)
				{
					_ = parser.CommandCommaArgsParse(context.children[2].GetText());
				}
				else
				{
					_ = parser.CommandSingleArgParse(context.children[2].GetText());
				}
			}

			List<CallState> arguments = []; // = parser.CurrentState().Arguments;
			bool eqSplit = (libraryCommandDefinition.Attribute.Behavior & Definitions.CommandBehavior.EqSplit) != 0;
			bool noParse = (libraryCommandDefinition.Attribute.Behavior & Definitions.CommandBehavior.NoParse) != 0;
			bool noRSParse = (libraryCommandDefinition.Attribute.Behavior & Definitions.CommandBehavior.RSNoParse) != 0;
			var nArgs = parser.CurrentState.Arguments.Count;

			// TODO: Implement lsargs - but there are no immediate commands that need it.

			if (eqSplit)
			{
				if (noParse)
				{
					arguments.Add(parser.CurrentState.Arguments[0]);
				}
				else
				{
					arguments.Add(parser.FunctionParse(parser.CurrentState.Arguments[0].Message!.ToString())!);
				}

				if (noRSParse && nArgs > 1)
				{
					arguments.AddRange(parser.CurrentState.Arguments[1..]);
				}
				else if (nArgs > 1)
				{
					arguments.AddRange(parser.CurrentState.Arguments[1..].Select(x => parser.FunctionParse(x.Message!.ToString())!));
				}
			}
			else if (!eqSplit && noParse)
			{
				arguments = parser.CurrentState.Arguments;
			}
			else if (!eqSplit && !noParse)
			{
				arguments.AddRange(parser.CurrentState.Arguments.Select(x => parser.FunctionParse(x.Message!.ToString())!));
			}

			return arguments;
		}
	}
}
