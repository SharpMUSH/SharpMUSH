using SharpMUSH.Implementation.Functions;
using System.Reflection;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Commands
{
	public static partial class Commands
	{
		private static readonly Dictionary<string, (SharpCommandAttribute Attribute, Func<Parser, CallState> Function)> _commandLibrary = [];
		private static readonly Dictionary<string, (MethodInfo Method, SharpCommandAttribute Attribute)> _knownBuiltInCommands = typeof(Commands)
			.GetMethods()
			.Select(m => (Method: m, Attribute: m.GetCustomAttribute(typeof(SharpCommandAttribute), false) as SharpCommandAttribute))
			.Where(x => x.Attribute is not null)
			.Select(y => new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(y.Attribute!.Name, (y.Method, y.Attribute!)))
			.ToDictionary();

		/*
				/noparse   : The command does not evaluate the leftside arg(s).
				/eqsplit   : The parser parses leftside and rightside around =
				/lsargs    : Comma-separated arguments on the left side are parsed.
				/rsargs    : When used with /eqsplit, the right-side arguments are comma-separated and are parsed individually
				/rsnoparse : The command does not evaluate the rightside arg(s).
		 */
		public static CallState EvaluateCommands(string name, Parser parser, FunctionContext context, CallState[] args)
		{
			throw new NotImplementedException();
		}
	}
}
