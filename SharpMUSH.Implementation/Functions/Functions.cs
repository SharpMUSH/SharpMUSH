using OneOf.Types;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using System.Reflection;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Functions;

public static partial class Functions
{
	private static readonly
		Dictionary<string, (SharpFunctionAttribute Attribute, Func<IMUSHCodeParser, ValueTask<CallState>> Function)>
		_functionLibrary = [];

	private static readonly Dictionary<string, (MethodInfo Method, SharpFunctionAttribute Attribute)>
		_knownBuiltInMethods = typeof(Functions)
			.GetMethods()
			.Select(m => (Method: m,
				Attribute: m.GetCustomAttribute(typeof(SharpFunctionAttribute), false) as SharpFunctionAttribute))
			.Where(x => x.Attribute is not null)
			.SelectMany(y =>
				(Configurable.FunctionAliases.TryGetValue(y.Attribute!.Name, out var aliases)
					? aliases.Select(alias =>
						new KeyValuePair<string, (MethodInfo Method, SharpFunctionAttribute Attribute)>(alias,
							(y.Method, y.Attribute!)))
					: [])
				.Append(new KeyValuePair<string, (MethodInfo Method, SharpFunctionAttribute Attribute)>(y.Attribute.Name,
					(y.Method, y.Attribute!))))
			.ToDictionary();

	static Functions()
	{
		foreach (var knownMethod in _knownBuiltInMethods)
		{
			_functionLibrary.Add(knownMethod.Key,
				(knownMethod.Value.Attribute,
					new Func<IMUSHCodeParser, ValueTask<CallState>>(p =>
						(ValueTask<CallState>)knownMethod.Value.Method.Invoke(null, [p, knownMethod.Value.Attribute])!)));
		}
	}

	/// <summary>
	/// TODO: Optimization needed. We should at least grab the in-built ones at startup.
	/// </summary>
	/// <param name="name">Function Name</param>
	/// <param name="source">The source MarkupString</param>
	/// <param name="parser">Parser for evaluation</param>
	/// <param name="context">Function Context for Depth</param>
	/// <param name="args">Arguments</param>
	/// <returns>The resulting CallState.</returns>
	public static async ValueTask<CallState> CallFunction(string name, MString source, IMUSHCodeParser parser,
		FunctionContext context, FunArgumentContext[] args, SharpMUSHParserVisitor visitor)
	{
		if (!_functionLibrary.TryGetValue(name, out var libraryMatch))
		{
			var discoveredFunction = DiscoverBuiltInFunction(name);

			if (!discoveredFunction.TryPickT0(out var functionValue, out _))
			{
				return new CallState(string.Format(Errors.ErrorNoSuchFunction, name), context.Depth());
			}

			_functionLibrary.Add(name, functionValue);
			libraryMatch = _functionLibrary[name];
		}

		var (attribute, function) = libraryMatch;

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
		if (contextDepth > Configurable.MaxCallDepth)
		{
			return new CallState(Errors.ErrorCall, contextDepth);
		}

		if (stackDepth > Configurable.MaxFunctionDepth)
		{
			return new CallState(Errors.ErrorInvoke, stackDepth);
		}

		if (recursionDepth > Configurable.MaxRecursionDepth)
		{
			return new CallState(Errors.ErrorRecursion, recursionDepth);
		}

		var stripAnsi = attribute.Flags.HasFlag(FunctionFlags.StripAnsi);

		if (!attribute.Flags.HasFlag(FunctionFlags.NoParse))
		{
			refinedArguments = (await Task.WhenAll(args
					.Select(async x => new CallState(
						stripAnsi
							? MModule.plainText2((await visitor.VisitChildren(x))?.Message ?? MModule.empty())
							: (await visitor.VisitChildren(x))?.Message ?? MModule.empty(), x.Depth()))))
				.DefaultIfEmpty(new CallState(MModule.empty(), context.Depth()))
				.ToList();
		}
		else if (attribute.Flags.HasFlag(FunctionFlags.NoParse) && attribute.MaxArgs == 1)
		{
			return new CallState(
				MModule.substring(context.Start.StartIndex, context.Stop.StopIndex - context.Start.StartIndex + 1, source),
				contextDepth,
				null,
				async () => (await visitor.VisitChildren(context) ?? CallState.Empty with { Depth = context.Depth() })
					.Message!);
		}
		else
		{
			refinedArguments = args.Select(x => new CallState(stripAnsi
						? MModule.plainText2(MModule.substring(x.Start.StartIndex,
							context.Stop?.StopIndex is null ? 0 : (x.Stop.StopIndex - x.Start.StartIndex + 1), source))
						: MModule.substring(x.Start.StartIndex,
							context.Stop?.StopIndex is null ? 0 : (x.Stop.StopIndex - x.Start.StartIndex + 1), source),
					x.Depth(), null,
					async () => stripAnsi
						? (await visitor.VisitChildren(x))!.Message
						: MModule.plainText2((await visitor.VisitChildren(x))!.Message)))
				.DefaultIfEmpty(new CallState(MModule.empty(), context.Depth()))
				.ToList();
		}

		if (attribute.Flags.HasFlag(FunctionFlags.DecimalsOnly) && refinedArguments.Any(a =>
			    !decimal.TryParse(MModule.plainText(a?.Message ?? MModule.empty()), out _)))
		{
			return new CallState(attribute.MaxArgs > 1 ? Errors.ErrorNumbers : Errors.ErrorNumber);
		}

		if (attribute.Flags.HasFlag(FunctionFlags.IntegersOnly) && refinedArguments.Any(a =>
			    !int.TryParse(MModule.plainText(a?.Message ?? MModule.empty()), out _)))
		{
			return new CallState(attribute.MaxArgs > 1 ? Errors.ErrorIntegers : Errors.ErrorInteger);
		}

		// TODO: Consider adding the ParserContexts as Arguments, so that Evaluation can be more optimized.
		var newParser = parser.Push(new ParserState(
			Registers: currentState.Registers,
			IterationRegisters: currentState.IterationRegisters,
			RegexRegisters: currentState.RegexRegisters,
			CurrentEvaluation: currentState.CurrentEvaluation,
			ParserFunctionDepth: parser.CurrentState.ParserFunctionDepth + 1,
			Function: name,
			Command: null,
			Switches: [],
			Arguments: new(refinedArguments.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value))
				.ToDictionary()),
			Executor: currentState.Executor,
			Enactor: currentState.Enactor,
			Caller: currentState.Caller,
			Handle: currentState.Handle
		));

		var result = await function(newParser) with { Depth = contextDepth };

		return result;
	}

	private static Option<(SharpFunctionAttribute, Func<IMUSHCodeParser, ValueTask<CallState>>)>
		DiscoverBuiltInFunction(string name)
	{
		if (!_knownBuiltInMethods.TryGetValue(name, out var result))
			return new None();

		return (result.Attribute, new Func<IMUSHCodeParser, ValueTask<CallState>>
			(p => (ValueTask<CallState>)result.Method.Invoke(null, [p, result.Attribute])!));
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
	public static bool AddFunction(string name, SharpFunctionAttribute attr,
		Func<IMUSHCodeParser, ValueTask<CallState>> func) =>
		_functionLibrary.TryAdd(name.ToLower(), (attr, func));
}