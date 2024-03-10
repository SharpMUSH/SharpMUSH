using Antlr4.Runtime.Tree;
using System.Reflection;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		private const char Space = ' ';
		private const char Slash = '/';
		private static readonly Dictionary<string, (SharpCommandAttribute Attribute, Func<Parser, CallState> Function)> _commandLibrary = [];
		private static readonly Dictionary<string, (MethodInfo Method, SharpCommandAttribute Attribute)> _knownBuiltInCommands = typeof(Commands)
			.GetMethods()
			.Select(m => (Method: m, Attribute: m.GetCustomAttribute(typeof(SharpCommandAttribute), false) as SharpCommandAttribute))
			.Where(x => x.Attribute is not null)
			.Select(y => new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(y.Attribute!.Name, (y.Method, y.Attribute!)))
			.ToDictionary();

		static Commands()
		{
			foreach( var knownCommand in _knownBuiltInCommands)
			{
				_commandLibrary.Add(knownCommand.Key, (knownCommand.Value.Attribute, new Func<Parser, CallState>(p => (CallState)knownCommand.Value.Method.Invoke(null, [p, knownCommand.Value.Attribute])!)));
			}
		}

		public static CallState EvaluateCommands(Parser parser, CommandContext context, Func<IRuleNode, CallState?> visitChildren)
		{

			var firstCommandMatch = context.firstCommandMatch();
			var conText = context.GetText();
			var command = firstCommandMatch.GetText();

			// Step 1: Check if it's a SOCKET command
			// TODO: Optimize
			var socketCommandPattern = _commandLibrary.Where(x => 
				(x.Key == command.ToUpper()) &&
				((x.Value.Attribute.Behavior & Definitions.CommandBehavior.SOCKET) == Definitions.CommandBehavior.SOCKET));

			if(socketCommandPattern.Any())
			{
				// Run as Socket Command.
				throw new NotImplementedException();
			}

			// Step 2: Check for a single-token command
			// TODO: Optimize
			var singleTokenCommandPattern = _commandLibrary.Where(x =>
				(x.Key == command[..1].ToUpper()) &&
				((x.Value.Attribute.Behavior & Definitions.CommandBehavior.SingleToken) == Definitions.CommandBehavior.SingleToken));

			if(singleTokenCommandPattern.Any())
			{
				// Run single token command
				throw new NotImplementedException();
			}

			// Step 3: Check room Aliases
			// Step 4: Check if we are setting an attribute: &...
			// Step 5: Check @COMMAND in command library

			// TODO: Optimize
			// TODO: Evaluate Command
			var evaluatedCallContext = visitChildren(firstCommandMatch)!.Message!;
			var evaluatedCallContextAsString = MModule.plainText(evaluatedCallContext);
			var slashIndex = evaluatedCallContextAsString.IndexOf(Slash);
			var rootCommand = evaluatedCallContextAsString[..(slashIndex > -1 ? slashIndex : evaluatedCallContextAsString.Length)];

			if (_commandLibrary.TryGetValue(rootCommand.ToUpper(), out var libraryCommandDefinition))
			{
				var argument = (libraryCommandDefinition.Attribute.Behavior & Definitions.CommandBehavior.NoParse) == Definitions.CommandBehavior.NoParse
					? new CallState(context.evaluationString().GetText())!
					: visitChildren(context.evaluationString())!;

				return libraryCommandDefinition.Function.Invoke(parser.Push(new Parser.ParserState(
					Registers: parser.State.Peek().Registers,
					CurrentEvaluation: parser.State.Peek().CurrentEvaluation,
					Command: rootCommand,
					// TODO: Comma Separate should be handled by the parser, and we get use GetText if we need to stich it.
					Arguments: [argument],
					Function: null,
					Executor: new Library.Models.DBRef(1), // TODO: Fix
					Enactor: new Library.Models.DBRef(1),  // We need call context
					Caller: new Library.Models.DBRef(1)    // Especially when coming from a connection.
				)));
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
			return new CallState("Huh?");
		}
	}
}
