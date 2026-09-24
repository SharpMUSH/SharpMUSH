using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Implementation.Common;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>The register stores r() reads from; each is matched by unambiguous prefix.</summary>
	private static readonly string[] RegisterTypes = ["qregisters", "args", "iter", "switch", "regexp"];

	/// <summary>The register stores <c>registers()</c> can list, as its <c>&lt;types&gt;</c> argument names them.</summary>
	[Flags]
	private enum RegisterKinds
	{
		None = 0,
		QRegisters = 1,
		Args = 2,
		Iter = 4,
		Switch = 8,
		Regexp = 16,
		All = QRegisters | Args | Iter | Switch | Regexp
	}

	/// <summary>
	/// Parses <c>registers()</c>' space-separated <c>&lt;types&gt;</c>. Unlike r(), each name must be
	/// spelled in full (any case), and <c>stack</c> is a synonym for <c>args</c>.
	/// </summary>
	/// <returns><see cref="RegisterKinds.None"/> for an unknown name; <see cref="RegisterKinds.All"/> for none given.</returns>
	private static RegisterKinds ParseRegisterKinds(string types)
	{
		var named = types.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ParseRegisterKind).ToList();
		if (named.Contains(RegisterKinds.None)) return RegisterKinds.None;
		return named.Count == 0 ? RegisterKinds.All : named.Aggregate(RegisterKinds.None, (kinds, kind) => kinds | kind);
	}

	private static RegisterKinds ParseRegisterKind(string type) => type.ToLowerInvariant() switch
	{
		"qregisters" => RegisterKinds.QRegisters,
		"args" or "stack" => RegisterKinds.Args,
		"iter" => RegisterKinds.Iter,
		"switch" => RegisterKinds.Switch,
		"regexp" => RegisterKinds.Regexp,
		_ => RegisterKinds.None
	};

	/// <summary>
	/// The names of the visible registers of <paramref name="kinds"/> that hold a non-blank value and
	/// match the wildcard <paramref name="pattern"/> (case-insensitive; empty matches all), as
	/// PennMUSH's <c>fun_listq</c> lists them. Each is keyed by the type letter Penn gives it — A
	/// (args), N and T (iteration count and text), Q, R (regexp), S (switch) — and the list is in
	/// byte order of that key, so it is deterministic and groups by type. Iteration and switch
	/// context are named for their innermost level only, <c>0</c>.
	/// </summary>
	private static IEnumerable<string> VisibleRegisterNames(ParserState state, RegisterKinds kinds, string? pattern)
	{
		var matcher = string.IsNullOrEmpty(pattern) ? null : SoftcodeRegex.Wildcard(pattern);
		return RegisterEntries(state, kinds)
			.Where(entry => matcher is null || matcher.IsMatch(entry.Name))
			.DistinctBy(entry => entry.Key)
			.OrderBy(entry => entry.Key, StringComparer.Ordinal)
			.Select(entry => entry.Name);
	}

	private static IEnumerable<(string Key, string Name)> RegisterEntries(ParserState state, RegisterKinds kinds)
	{
		if (kinds.HasFlag(RegisterKinds.Args))
			foreach (var (name, value) in state.EnvironmentRegisters)
				if (value.Message is { Length: > 0 })
					yield return ($"A{name}", name);

		if (kinds.HasFlag(RegisterKinds.QRegisters) && state.Registers.TryPeek(out var qregs))
			foreach (var (name, value) in qregs)
				if (value.Length > 0)
					yield return ($"Q{name}", name);

		if (kinds.HasFlag(RegisterKinds.Regexp) && state.RegexpCaptures is { } rxregs)
			foreach (var (name, value) in rxregs)
				if (value.Length > 0)
					yield return ($"R{name.ToUpperInvariant()}", name.ToUpperInvariant());

		if (kinds.HasFlag(RegisterKinds.Iter) && state.IterationRegisters.TryPeek(out var iteration))
		{
			yield return ("N0", "0");
			if (iteration.Value.Length > 0)
				yield return ("T0", "0");
		}

		if (kinds.HasFlag(RegisterKinds.Switch) && state.SwitchStack.TryPeek(out var switchText) && switchText.Length > 0)
			yield return ("S0", "0");
	}

	/// <summary>
	/// The one body behind <c>setq</c> and <c>setr</c>. PennMUSH registers both on
	/// <c>fun_setq</c> and tells them apart by <c>called_as</c>, which decides only whether the
	/// first value is echoed back (<c>src/funmisc.c:321-352</c>).
	/// </summary>
	private static CallState SetRegisters(IMUSHCodeParser parser, bool echoFirstValue)
	{
		var arguments = parser.CurrentState.ArgumentsOrdered;
		var everythingIsOkay = true;

		for (var i = 0; i < arguments.Count; i += 2)
		{
			everythingIsOkay &= parser.CurrentState.AddRegister(
				arguments[i.ToString()].Message!.ToPlainText().ToUpper(),
				arguments[(i + 1).ToString()].Message!);
		}

		if (!everythingIsOkay) return new CallState(ErrorMessages.Returns.BadRegName);

		return echoFirstValue ? new CallState(arguments["1"].Message!) : new CallState(string.Empty);
	}

	/// <remarks>
	/// <c>EvenArgsOnly</c> is PennMUSH's <c>(nargs % 2) != 0</c> guard (<c>src/funmisc.c:327</c>),
	/// which <c>fun_setq</c> applies to both names. Declared on <c>setr</c> alone, an odd-argument
	/// <c>setq</c> reached the pairing loop and read past the last argument.
	/// </remarks>
	[SharpFunction(Name = "setq", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.EvenArgsOnly)]
	public ValueTask<CallState> setq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(SetRegisters(parser, echoFirstValue: false));

	[SharpFunction(Name = "setr", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.EvenArgsOnly)]
	public ValueTask<CallState> setr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(SetRegisters(parser, echoFirstValue: true));

	// r(<register>[, <type>]) — read a register. <type> (default "qregisters") selects the store, per
	// `help r`: qregisters (setq/setr), args (the %0-%9 stack + named regexp $-command captures), iter
	// (itext context), switch (stext context), regexp (re*() capture names, %$0 and named). Note: the
	// second argument is the TYPE selector, NOT a fallback default value.
	[SharpFunction(Name = "r", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> R(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var registerName = (args["0"].Message ?? MarkupText.Empty).ToPlainText();
		var typeArgStr = args.TryGetValue("1", out var typeArg) && typeArg.Message is not null
			? typeArg.Message.ToPlainText().Trim()
			: string.Empty;

		// <type> defaults to qregisters and accepts unambiguous PREFIXES (e.g. "a"→args, "q"→qregisters,
		// "sw"→switch). The five type names have distinct first letters, so any prefix matches at most one.
		var canonicalType = string.IsNullOrEmpty(typeArgStr)
			? "qregisters"
			: RegisterTypes.FirstOrDefault(t => t.StartsWith(typeArgStr, StringComparison.OrdinalIgnoreCase));
		if (canonicalType is null)
			return ValueTask.FromResult(new CallState($"#-1 R: INVALID REGISTER TYPE '{typeArgStr}'"));

		switch (canonicalType)
		{
			// setq()/setr() registers (names are case-insensitive).
			case "qregisters":
				return ValueTask.FromResult(
					parser.CurrentState.Registers.TryPeek(out var qregs)
					&& qregs.TryGetValue(registerName.ToUpper(), out var qval)
						? new CallState(qval)
						: CallState.Empty);

			// The argument stack (%0-%9, up to 30) plus named stack registers from regexp $-commands.
			case "args":
				return ValueTask.FromResult(
					parser.CurrentState.EnvironmentRegisters.TryGetValue(registerName, out var aval)
						? new CallState(aval.Message!)
						: CallState.Empty);

			// $0-$9 and named captures from switch(), reswitch() and regedit(). PennMUSH's fun_r has no
			// case for this type and always returns nothing; this returns the capture, as `help r` says.
			case "regexp":
				return ValueTask.FromResult(new CallState(parser.CurrentState.RegexpCapture(registerName)));

			// itext() context — int level, or "L" for the outermost iteration.
			case "iter":
				{
					var maxCount = parser.CurrentState.IterationRegisters.Count;
					if (registerName.Equals("L", StringComparison.OrdinalIgnoreCase))
						return ValueTask.FromResult(maxCount == 0
							? new CallState(ErrorMessages.Returns.RegisterRange)
							: new CallState(parser.CurrentState.IterationRegisters.Last().Value));
					if (!int.TryParse(registerName, out var lvl))
						return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
					if (lvl < 0 || lvl >= maxCount)
						return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
					// The stack enumerates innermost-first, so the level IS the index — the same
					// convention itext() and %i<n> use. r(<n>,iter) indexed it backwards.
					return ValueTask.FromResult(
						new CallState(parser.CurrentState.IterationRegisters.ElementAt(lvl).Value));
				}

			// stext() context — int depth, or "L" for the outermost switch.
			case "switch":
				{
					var stack = parser.CurrentState.SwitchStack;
					var depth = 0;
					if (registerName.Equals("L", StringComparison.OrdinalIgnoreCase))
						depth = stack.Count - 1;
					else if (!string.IsNullOrEmpty(registerName) && (!int.TryParse(registerName, out depth) || depth < 0))
						return ValueTask.FromResult(new CallState(ErrorMessages.Returns.NonNegativeInteger));
					if (stack.Count == 0 || depth < 0 || depth >= stack.Count)
						return ValueTask.FromResult(new CallState(string.Empty));
					return ValueTask.FromResult(new CallState(stack.ElementAtOrDefault(depth) ?? MarkupText.Empty));
				}

			default:
				// Unreachable: canonicalType is always one of the five valid names (or we returned above).
				return ValueTask.FromResult(new CallState($"#-1 R: INVALID REGISTER TYPE '{typeArgStr}'"));
		}
	}

	[SharpFunction(Name = "letq", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse | FunctionFlags.UnEvenArgsOnly)]
	public async ValueTask<CallState> LetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var everythingIsOkay = true;

		var numberedArguments = parser.CurrentState.ArgumentsOrdered;
		var npairs = (numberedArguments.Count - 1) / 2;

		// If no pairs, just evaluate the body in the current scope (PE_REGS_LET with empty scope:
		// all register writes pass up to the caller, nothing is saved/restored).
		if (npairs == 0)
		{
			return (await parser.FunctionParse(numberedArguments.Last().Value.Message!))!;
		}

		// Note: MarkupString should be immutable - verify this if register behavior issues occur
		var validPeek = parser.CurrentState.Registers.TryPeek(out var currentRegisters);
		var newRegisters = currentRegisters!.ToDictionary(k => k.Key, kv => kv.Value);
		parser.CurrentState.Registers.Push(newRegisters);

		for (var i = 0; i < numberedArguments.Count - 1; i += 2)
		{
			everythingIsOkay &= parser.CurrentState.AddRegister(
				numberedArguments[i.ToString()].Message!.ToPlainText().ToUpper(),
				numberedArguments[(i + 1).ToString()].Message!);
		}

		if (everythingIsOkay)
		{
			var parsed = await parser.FunctionParse(numberedArguments.Last().Value.Message!);
			_ = parser.CurrentState.Registers.TryPop(out _);
			return parsed!;
		}

		_ = parser.CurrentState.Registers.TryPop(out _);
		return new CallState(ErrorMessages.Returns.BadRegName);
	}

	[SharpFunction(Name = "listq", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> ListQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var pattern = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText();
		return ValueTask.FromResult(new CallState(
			string.Join(" ", VisibleRegisterNames(parser.CurrentState, RegisterKinds.QRegisters, pattern))));
	}

	[SharpFunction(Name = "registers", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> Registers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var pattern = args.GetValueOrDefault("0")?.Message?.ToPlainText();
		var kinds = ParseRegisterKinds(args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? string.Empty);
		if (kinds == RegisterKinds.None)
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidArgument));

		var separator = args.GetValueOrDefault("2")?.Message?.ToPlainText() ?? " ";
		return ValueTask.FromResult(new CallState(
			string.Join(separator, VisibleRegisterNames(parser.CurrentState, kinds, pattern))));
	}

	[SharpFunction(Name = "unsetq", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public ValueTask<CallState> UnSetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var patterns = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText()
			.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
		if (parser.CurrentState.Registers.TryPeek(out var registers))
		{
			// No pattern, or a lone "*" among them, clears the whole scope.
			if (patterns.Length == 0 || patterns.Contains("*")) registers.Clear();
			else
			{
				var matching = patterns
					.SelectMany(pattern => VisibleRegisterNames(parser.CurrentState, RegisterKinds.QRegisters, pattern))
					.ToList();
				foreach (var name in matching)
					registers.Remove(name);
			}
		}

		return ValueTask.FromResult<CallState>(new(string.Empty));
	}

	[SharpFunction(Name = "itext", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var levelArg = args["0"].Message!.ToPlainText();
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (levelArg.Equals("L", StringComparison.OrdinalIgnoreCase))
		{
			if (maxCount == 0)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
			}
			return ValueTask.FromResult(new CallState(parser.CurrentState.IterationRegisters.Last().Value));
		}

		if (!int.TryParse(levelArg, out var level))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		if (level < 0 || level >= maxCount)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
		}

		// The stack enumerates innermost-first, so the level IS the index: 0 = current, 1 = parent.
		// The "L" branch above takes the other end, which is what itext(ilev()) resolves to.
		var value = parser.CurrentState.IterationRegisters.ElementAt(level).Value;
		return ValueTask.FromResult(new CallState(value));
	}

	[SharpFunction(Name = "stext", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> SText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// stext([<n>]) returns the string being matched in the current or nth nested switch.
		// stext(L) returns the outermost switch string.
		// n=0 is current switch, n=1 is the switch the current is nested in, etc.

		var args = parser.CurrentState.Arguments;
		var stack = parser.CurrentState.SwitchStack;

		int depth = 0;

		// Validate arguments first, before checking stack count
		if (args.TryGetValue("0", out var depthArg) && depthArg.Message != null)
		{
			var depthStr = depthArg.Message!.ToPlainText().Trim();

			// Skip processing if the argument is empty (defaults to 0)
			if (!string.IsNullOrEmpty(depthStr))
			{
				// Handle "L" or "l" for outermost (last) switch
				if (depthStr.Equals("L", StringComparison.OrdinalIgnoreCase))
				{
					depth = stack.Count - 1;
				}
				else if (!int.TryParse(depthStr, out depth) || depth < 0)
				{
					return ValueTask.FromResult(new CallState(ErrorMessages.Returns.NonNegativeInteger));
				}
			}
		}

		if (stack.Count == 0)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}

		// Convert depth to index from top of stack
		// depth 0 = current (top), depth 1 = parent, etc.
		if (depth >= stack.Count)
		{
			return ValueTask.FromResult(new CallState(string.Empty));
		}

		var item = stack.ElementAtOrDefault(depth);
		return ValueTask.FromResult(new CallState(item ?? MarkupText.Empty));
	}

	[SharpFunction(Name = "slev", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public ValueTask<CallState> SLev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return ValueTask.FromResult(new CallState(parser.CurrentState.SwitchStack.Count));
	}

	[SharpFunction(Name = "ibreak", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["levels"])]
	public ValueTask<CallState> IterationBreak(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, "1").ToPlainText();
		if (!long.TryParse(text, out var levels))
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		if (levels < 0 || levels > parser.CurrentState.IterationRegisters.Count)
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.OutOfRange));
		foreach (var iteration in parser.CurrentState.IterationRegisters.Take((int)levels))
			iteration.Break = true;
		return ValueTask.FromResult(CallState.Empty);
	}

	[SharpFunction(Name = "ilev", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public ValueTask<CallState> IterationLevel(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var depth = parser.CurrentState.IterationRegisters.Count;
		return ValueTask.FromResult(new CallState(depth > 0 ? depth - 1 : -1));
	}

	[SharpFunction(Name = "inum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = [])]
	public ValueTask<CallState> IterationNumber(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var levelArg = args["0"].Message!.ToPlainText();
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		if (levelArg.Equals("L", StringComparison.OrdinalIgnoreCase))
		{
			// "L" refers to the outermost iteration
			if (maxCount == 0)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
			}
			return ValueTask.FromResult(new CallState(parser.CurrentState.IterationRegisters.Last().Iteration));
		}

		if (!int.TryParse(levelArg, out var level))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integer));
		}

		if (level < 0 || level >= maxCount)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
		}

		// The stack enumerates innermost-first, so the level IS the index: 0 = current, 1 = parent.
		var iteration = parser.CurrentState.IterationRegisters.ElementAt(level).Iteration;
		return ValueTask.FromResult(new CallState(iteration));
	}
}
