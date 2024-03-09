using Microsoft.Win32;
using SharpMUSH.Implementation.Functions;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
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

		public static CallState EvaluateCommands(Parser parser, FunctionContext context, CallState[] args)
		{
			var conText = context.GetText();
			// TODO: Support the first word to be evaluated.
			var firstSeparator = int.Min(conText.IndexOf(Space), conText.Length);

			// There can still be Switches attached to this.
			var command = conText[..firstSeparator];

			// Step 1: Check if it's a SOCKET command
			// TODO: Optimize
			var socketCommandPattern = _commandLibrary.Where(x => 
				(x.Key == command) &&
				((x.Value.Attribute.Behavior & Definitions.CommandBehavior.SOCKET) == Definitions.CommandBehavior.SOCKET));

			if(socketCommandPattern.Any()) 
			{
				// Run as Socket Command.
				throw new NotImplementedException();
			}

			// Step 2: Check for a single-token command
			// TODO: Optimize
			var singleTokenCommandPattern = _commandLibrary.Where(x =>
				(x.Key == conText.Substring(0,1)) &&
				((x.Value.Attribute.Behavior & Definitions.CommandBehavior.SOCKET) == Definitions.CommandBehavior.SOCKET));

			if(singleTokenCommandPattern.Any())
			{
				// Run single token command
				throw new NotImplementedException();
			}

			// Step 3: Check room Aliases
			// Step 4: Check if we are setting an attribute: &...
			// Step 5: Check @COMMAND in command library

			// TODO: Optimize
			var slashIndex = command.IndexOf(Slash);
			command = command[..(slashIndex > -1 ? 0 : slashIndex)];

			if(_commandLibrary.TryGetValue(command, out var libraryCommandDefinition))
			{
				libraryCommandDefinition.Function.Invoke(parser.Push(new Parser.ParserState(
					Registers: parser.State.Peek().Registers,
					CurrentEvaluation: parser.State.Peek().CurrentEvaluation,
					Command: "command",
					// TODO: Evaluate
					// TODO: Comma Separate should be handled by the parser, and we get use GetText if we need to stich it.
					Arguments: [new CallState(conText[firstSeparator..])],
					Function: null,
					Executor: new Library.Models.DBRef(1), // TODO: Fix
					Enactor: new Library.Models.DBRef(1),
					Caller: new Library.Models.DBRef(1)
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

			throw new NotImplementedException();
		}
	}
}
