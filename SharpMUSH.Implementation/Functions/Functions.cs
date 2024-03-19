using SharpMUSH.Implementation.Definitions;
using OneOf;
using System.Reflection;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Functions
{
	public static partial class Functions
	{
		private static readonly Dictionary<string, (SharpFunctionAttribute Attribute, Func<Parser, CallState> Function)> _functionLibrary = [];
		private static readonly Dictionary<string, (MethodInfo Method, SharpFunctionAttribute Attribute)> _knownBuiltInMethods = typeof(Functions)
			.GetMethods()
			.Select(m => (Method: m, Attribute: m.GetCustomAttribute(typeof(SharpFunctionAttribute), false) as SharpFunctionAttribute))
			.Where(x => x.Attribute is not null)
			.Select(y => new KeyValuePair<string, (MethodInfo Method, SharpFunctionAttribute Attribute)>(y.Attribute!.Name, (y.Method, y.Attribute!)))
			.ToDictionary();

		static Functions()
		{
			foreach (var knownMethod in _knownBuiltInMethods)
			{
				_functionLibrary.Add(knownMethod.Key, (knownMethod.Value.Attribute, new Func<Parser, CallState>(p => (CallState)knownMethod.Value.Method.Invoke(null, [p, knownMethod.Value.Attribute])!)));
			}
		}
		/// <summary>
		/// TODO: Optimization needed. We should at least grab the in-built ones at startup.
		/// </summary>
		/// <param name="name">Function Name</param>
		/// <param name="parser">Parser for evaluation</param>
		/// <param name="context">Function Context for Depth</param>
		/// <param name="args">Arguments</param>
		/// <returns>The resulting CallState.</returns>
		public static CallState CallFunction(string name, Parser parser, FunctionContext context, List<CallState> args)
		{
			if (!_functionLibrary.TryGetValue(name, out var libraryMatch))
			{
				var DiscoveredFunction = DiscoverBuiltInFunction(name);

				if (DiscoveredFunction.TryPickT1(out var functionValue, out var didNotFindValue) == false)
				{
					return new CallState(string.Format(Errors.ErrorNoSuchFunction, name), context.Depth());
				}

				_functionLibrary.Add(name, functionValue);
				libraryMatch = _functionLibrary[name];
			}

			(var attribute, var function) = libraryMatch;

			var currentStack = parser.State;
			var currentState = parser.CurrentState();
			var contextDepth = context.Depth();
			var stackDepth = currentStack.Count();
			var recursionDepth = currentStack.Count(x => x.Function == name);

			List<CallState> refinedArguments;

			/* Validation, this should probably go into its own function! */
			if (args.Count > attribute.MaxArgs)
			{
				parser.Pop();
				// Better Error Needed.
				return new CallState(Errors.ErrorArgRange, context.Depth());
			}

			if (args.Count < attribute.MinArgs)
			{
				parser.Pop();
				return new CallState(string.Format(Errors.ErrorTooFewArguments, name, attribute.MinArgs, args.Count), context.Depth());
			}

			if (contextDepth > Configurable.MaxCallDepth)
			{
				parser.Pop();
				return new CallState(Errors.ErrorCall, contextDepth);
			}

			if (stackDepth > Configurable.MaxFunctionDepth)
			{
				parser.Pop();
				return new CallState(Errors.ErrorInvoke, stackDepth);
			}

			if (recursionDepth > Configurable.MaxRecursionDepth)
			{
				parser.Pop();
				return new CallState(Errors.ErrorRecursion, recursionDepth);
			}

			if ((attribute.Flags & FunctionFlags.NoParse) != FunctionFlags.NoParse)
			{
				// TODO: Should we increase the Depth of the response by adding our context.Depth here?
				// This is also where we need to do a DEPTH CHECK.
				refinedArguments = args.Select(a => parser.FunctionParse(a?.Message?.ToString() ?? string.Empty)).ToList()!;
			}
			else if ((attribute.Flags & FunctionFlags.NoParse) == FunctionFlags.NoParse && attribute.MaxArgs == 1)
			{
				return new CallState(context.GetText(), context.Depth());
			}
			else
			{
				refinedArguments = args;
			}

			parser.Push(new Parser.ParserState(
				Registers: currentState.Registers,
				CurrentEvaluation: currentState.CurrentEvaluation,
				Function: name,
				Command: null,
				Arguments: refinedArguments,
				Executor: currentState.Executor,
				Enactor: currentState.Enactor,
				Caller: currentState.Caller
			));

			var result = function(parser) with { Depth = context.Depth() };
			parser.Pop();
			return result;
		}

		private static OneOf<bool, (SharpFunctionAttribute, Func<Parser, CallState>)> DiscoverBuiltInFunction(string name)
		{
			if (!_knownBuiltInMethods.TryGetValue(name, out var result))
				return false;

			return (result.Attribute, new Func<Parser, CallState>
				(p => (CallState)result.Method.Invoke(null, [p, result.Attribute])!));
		}

		/// <summary>
		/// Removes a function name from the Library.
		/// </summary>
		/// <param name="name">Function to remove.</param>
		/// <returns>True if it was removed, false if it was not found.</returns>
		public static bool RemoveFunction(string name) =>
			_functionLibrary.Remove(name.ToLower());

		/// <summary>
		/// Adds the function to the function library.
		/// </summary>
		/// <param name="name">Name of the function</param>
		/// <param name="attr">Function Attributes that describe behavior</param>
		/// <param name="func">Function to run when this is called</param>
		/// <returns>True if could be added. False if the name already existed.</returns>
		public static bool AddFunction(string name, SharpFunctionAttribute attr, Func<Parser, CallState> func) =>
			_functionLibrary.TryAdd(name.ToLower(), (attr, func));
	}
}
