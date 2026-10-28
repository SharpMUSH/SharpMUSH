using System.Text;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Implementation.Common;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "hasarg", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["name", "scope"])]
	public ValueTask<CallState> HasArgument(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var scope = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message.ToPlainText() ?? "visible";
		if (!scope.Equals("visible", StringComparison.OrdinalIgnoreCase) && !scope.Equals("supplied", StringComparison.OrdinalIgnoreCase))
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidArgument));
		var keys = parser.CurrentState.SuppliedArgumentKeys;
		var present = keys is not null && (scope.Equals("supplied", StringComparison.OrdinalIgnoreCase)
			? keys.Contains(name)
			: parser.CurrentState.EnvironmentRegisters.Keys.Any(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)));
		return ValueTask.FromResult(new CallState(present ? "1" : "0"));
	}

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

	/// <summary>
	/// An iteration or switch level as <c>fun_r</c> reads one: any strict number, truncated by
	/// <c>parse_integer</c> (<c>src/funmisc.c:752-756</c>), so <c>1.9</c> is level 1.
	/// </summary>
	private static bool TryRegisterLevel(string text, out int level)
	{
		level = ArgHelpers.ParseInteger(text);
		return NumericEvaluation.Strict.TryDouble(text, out _);
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
				if (value.Length > 0 && !RegisterLabels.IsLabelKey(name))
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

		if (kinds.HasFlag(RegisterKinds.Switch) && state.SwitchStack.TryPeek(out var switchFrame) && switchFrame.Text.Length > 0)
			yield return ("S0", "0");
	}

	/// <summary>
	/// The one body behind <c>setq</c> and <c>setr</c>. PennMUSH registers both on
	/// <c>fun_setq</c> and tells them apart by <c>called_as</c>, which decides only whether the
	/// first value is echoed back (<c>src/funmisc.c:321-352</c>).
	/// </summary>
	/// <remarks>
	/// Three arguments are RhostMUSH's labelled form, <c>setq(&lt;register&gt;, &lt;value&gt;, &lt;label&gt;)</c>
	/// (<c>process_setqs</c>, <c>Server/src/functions.c</c>). Any other odd count is PennMUSH's
	/// <c>(nargs % 2) != 0</c> refusal (<c>src/funmisc.c:327</c>), checked here rather than by
	/// <c>EvenArgsOnly</c> so the three-argument call gets through; Penn checks it after evaluating
	/// the arguments too. A third argument that cannot be a label keeps PennMUSH's refusal, so a
	/// PennMUSH call such as <c>setr(A,1,B)</c> answers as it does there.
	/// </remarks>
	private static CallState SetRegisters(IMUSHCodeParser parser, bool echoFirstValue, string calledAs)
	{
		var arguments = parser.CurrentState.ArgumentsOrdered;
		if (arguments.Count == 3 && RegisterLabels.IsValidLabel(arguments["2"].Message.ToPlainText()))
		{
			var labelled = SetLabelledRegister(parser.CurrentState,
				arguments["0"].Message.ToPlainText(), arguments["1"].Message, arguments["2"].Message.ToPlainText());
			if (labelled.Length > 0) return new CallState(labelled);
			return echoFirstValue ? new CallState(arguments["1"].Message) : new CallState(string.Empty);
		}

		if (arguments.Count % 2 != 0)
			return new CallState(string.Format(ErrorMessages.Returns.GotUnEvenArgs, calledAs));

		var everythingIsOkay = true;

		for (var i = 0; i < arguments.Count; i += 2)
		{
			everythingIsOkay &= parser.CurrentState.AddRegister(
				arguments[i.ToString()].Message.ToPlainText().ToUpper(),
				arguments[(i + 1).ToString()].Message);
		}

		if (!everythingIsOkay) return new CallState(ErrorMessages.Returns.BadRegName);

		return echoFirstValue ? new CallState(arguments["1"].Message) : new CallState(string.Empty);
	}

	/// <remarks>
	/// Both names share <see cref="SetRegisters"/>, which applies PennMUSH's odd-count guard
	/// (<c>src/funmisc.c:327</c>) to each; it once sat on <c>setr</c> alone, and an odd-argument
	/// <c>setq</c> read past the last argument.
	/// </remarks>
	[SharpFunction(Name = "setq", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public ValueTask<CallState> setq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(SetRegisters(parser, echoFirstValue: false, "SETQ"));

	[SharpFunction(Name = "setr", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public ValueTask<CallState> setr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(SetRegisters(parser, echoFirstValue: true, "SETR"));

	/// <summary>
	/// Sets <paramref name="register"/> to <paramref name="value"/> and gives it <paramref name="label"/>,
	/// RhostMUSH's labelled setq(). The register may be named by a label it already has, or be <c>+</c>
	/// (the register <paramref name="label"/> names, else the first of 0-9 and A-Z with neither label
	/// nor value) or <c>!</c> (the same, but a register holding a value may be taken).
	/// </summary>
	/// <returns>Empty when set; otherwise the error to return.</returns>
	private static string SetLabelledRegister(ParserState state, string register, MString value, string label)
	{
		if (!RegisterLabels.IsValidLabel(label)) return ErrorMessages.Returns.InvalidLabel;
		if (!state.Registers.TryPeek(out var frame)) return ErrorMessages.Returns.BadRegName;

		var name = register.ToUpper();
		if (name is "+" or "!")
		{
			if (!RegisterLabels.TryPick(frame, label, skipSet: name == "+", out name))
				return ErrorMessages.Returns.BadRegName;
		}
		else name = RegisterLabels.Resolve(frame, name);

		if (!state.AddRegister(name, value)) return ErrorMessages.Returns.BadRegName;
		RegisterLabels.Set(frame, name, label);
		return string.Empty;
	}

	/// <summary>
	/// The one body behind <c>setqm</c> and <c>setrm</c>, RhostMUSH's in-order setq()/setr()
	/// (<c>fun_setqm</c>/<c>fun_setrm</c>, <c>Server/src/functions.c</c>). Each pair's name and value are
	/// evaluated only after the pair before it is set, so a value can read the registers set to its left.
	/// </summary>
	/// <remarks>
	/// A name of the form <c>&lt;register&gt;&gt;&lt;label&gt;</c> also gives the register a label, as
	/// setq()'s third argument does. RhostMUSH splits the name at the <c>&gt;</c> before evaluating it;
	/// this splits the evaluated name, since a register name with a <c>&gt;</c> in it was already
	/// refused, and a <c>&gt;</c> written inside a function call in the name keeps its meaning.
	/// </remarks>
	/// <returns>
	/// The values set, in order, the first error a pair gave (empty when none), and whether evaluating
	/// any argument hit an error, which the caller carries on its result as an eager function's
	/// dispatcher would.
	/// </returns>
	private static async ValueTask<OrderedRegisters> SetRegistersInOrder(IMUSHCodeParser parser, int pairArguments)
	{
		var arguments = parser.CurrentState.ArgumentsOrdered;
		var values = new List<MString>(pairArguments / 2);
		var error = string.Empty;
		var hadErrors = false;

		for (var i = 0; i < pairArguments; i += 2)
		{
			var name = await arguments[i.ToString()].GetParsedResultAsync();
			var value = await arguments[(i + 1).ToString()].GetParsedResultAsync();
			hadErrors |= name.HadErrors || value.HadErrors;
			var valueText = value.Message ?? MarkupText.Empty;
			var nameText = (name.Message ?? MarkupText.Empty).ToPlainText();
			var separator = nameText.IndexOf('>');
			var pairError = separator < 0
				? parser.CurrentState.AddRegister(nameText.ToUpper(), valueText) ? string.Empty : ErrorMessages.Returns.BadRegName
				: SetLabelledRegister(parser.CurrentState, nameText[..separator], valueText, nameText[(separator + 1)..]);
			if (error.Length == 0) error = pairError;
			values.Add(valueText);
		}

		return new OrderedRegisters(values, error, hadErrors);
	}

	private sealed record OrderedRegisters(List<MString> Values, string Error, bool HadErrors);

	[SharpFunction(Name = "setqm", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse | FunctionFlags.EvenArgsOnly)]
	public async ValueTask<CallState> SetQM(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var set = await SetRegistersInOrder(parser, parser.CurrentState.ArgumentsOrdered.Count);
		var result = set.Error.Length == 0 ? CallState.Empty : new CallState(set.Error);
		return result with { HadErrors = set.HadErrors };
	}

	/// <remarks>
	/// RhostMUSH's <c>setrm</c> returns every value it set, joined by an optional trailing
	/// <c>&lt;delimiter&gt;</c> (a space by default), which an odd argument count marks. Three
	/// arguments are one pair and a delimiter with nothing to separate, which Rhost refuses.
	/// </remarks>
	[SharpFunction(Name = "setrm", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.NoParse)]
	public async ValueTask<CallState> SetRM(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arguments = parser.CurrentState.ArgumentsOrdered;
		if (arguments.Count == 3)
			return new CallState(string.Format(ErrorMessages.Returns.GotUnEvenArgs, "SETRM"));

		var hasDelimiter = arguments.Count % 2 == 1;
		var delimiter = hasDelimiter
			? await arguments[(arguments.Count - 1).ToString()].GetParsedResultAsync()
			: new CallState(MarkupText.Space);

		var set = await SetRegistersInOrder(parser, hasDelimiter ? arguments.Count - 1 : arguments.Count);
		var result = set.Error.Length == 0
			? JoinWithinOutputLimit(parser, set.Values, delimiter.Message ?? MarkupText.Empty)
			: new CallState(set.Error);
		return result with { HadErrors = set.HadErrors || delimiter.HadErrors };
	}

	private static CallState JoinWithinOutputLimit(IMUSHCodeParser parser, List<MString> values, MString delimiter)
		=> FunctionLimits.ExceedsCombinedOutput(parser.CurrentState, values, delimiter.Length)
			? FunctionLimits.RejectOutput(parser.CurrentState)
			: new CallState(MarkupText.Join(delimiter, values));

	// r(<register>[, <type>]) — read a register. <type> (default "qregisters") selects the store, per
	// `help r`: qregisters (setq/setr), args (the %0-%9 stack + named regexp $-command captures), iter
	// (itext context), switch (stext context), regexp (re*() capture names, %$0 and named). Note: the
	// second argument is the TYPE selector, NOT a fallback default value.
	[SharpFunction(Name = "r", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> R(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var registerName = args["0"].Message.ToPlainText();
		var typeArgStr = args.TryGetValue("1", out var typeArg)
			? typeArg.Message.ToPlainText().Trim()
			: string.Empty;

		// <type> defaults to qregisters and accepts unambiguous PREFIXES (e.g. "a"→args, "q"→qregisters,
		// "sw"→switch). The five type names have distinct first letters, so any prefix matches at most one.
		var canonicalType = string.IsNullOrEmpty(typeArgStr)
			? "qregisters"
			: RegisterTypes.FirstOrDefault(t => t.StartsWith(typeArgStr, StringComparison.OrdinalIgnoreCase));
		if (canonicalType is null)
			return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.InvalidRegisterTypeFormat, typeArgStr)));

		switch (canonicalType)
		{
			// setq()/setr() registers (names are case-insensitive).
			case "qregisters":
				return ValueTask.FromResult(
					parser.CurrentState.Registers.TryPeek(out var qregs)
					&& qregs.TryGetValue(RegisterLabels.Resolve(qregs, registerName.ToUpper()), out var qval)
						? new CallState(qval)
						: CallState.Empty);

			// The argument stack (%0-%9, up to 30) plus named arguments, as %<name> reads them.
			case "args":
				return ValueTask.FromResult(new CallState(parser.CurrentState.Argument(registerName)));

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
					// fun_r takes a level that is_strict_number accepts, truncated by parse_integer, and
					// calls anything else a bad register name (src/funmisc.c:752-756).
					if (!TryRegisterLevel(registerName, out var lvl))
						return ValueTask.FromResult(new CallState(ErrorMessages.Returns.BadRegName));
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
					else if (!TryRegisterLevel(registerName, out depth))
						return ValueTask.FromResult(new CallState(ErrorMessages.Returns.BadRegName));
					else if (depth < 0)
						return ValueTask.FromResult(new CallState(ErrorMessages.Returns.NonNegativeInteger));
					if (stack.Count == 0 || depth < 0 || depth >= stack.Count)
						return ValueTask.FromResult(new CallState(string.Empty));
					return ValueTask.FromResult(new CallState(stack.ElementAtOrDefault(depth)?.Text ?? MarkupText.Empty));
				}

			default:
				// Unreachable: canonicalType is always one of the five valid names (or we returned above).
				return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.InvalidRegisterTypeFormat, typeArgStr)));
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
			return (await ParseRawArgument(parser, numberedArguments.Last().Value.Message))!;
		}

		// fun_letq evaluates every name and value in the caller's scope before the body gets its own.
		var pairs = new List<(string Name, MString Value)>();
		for (var i = 0; i < numberedArguments.Count - 1; i += 2)
		{
			var name = (await ParseRawArgument(parser, numberedArguments[i.ToString()].Message))?.Message?.ToPlainText() ?? string.Empty;
			var value = (await ParseRawArgument(parser, numberedArguments[(i + 1).ToString()].Message))?.Message ?? MarkupText.Empty;
			pairs.Add((name.ToUpper(), value));
		}

		parser.CurrentState.Registers.TryPeek(out var currentRegisters);
		var newRegisters = new LetScope(currentRegisters!);
		parser.CurrentState.Registers.Push(newRegisters);

		var local = new HashSet<string>();
		foreach (var (pairName, value) in pairs)
		{
			var name = RegisterLabels.Resolve(newRegisters, pairName);
			everythingIsOkay &= parser.CurrentState.AddRegister(name, value);
			local.Add(name);
		}

		if (!everythingIsOkay)
		{
			_ = parser.CurrentState.Registers.TryPop(out _);
			return new CallState(ErrorMessages.Returns.BadRegName);
		}

		var parsed = await ParseRawArgument(parser, numberedArguments.Last().Value.Message);
		_ = parser.CurrentState.Registers.TryPop(out _);
		PassUnnamedRegisters(newRegisters, currentRegisters!, local);
		return parsed!;
	}

	/// <summary>
	/// A letq() body's registers. <see cref="Stopped"/> is PE_REGS_QSTOP: an unsetq() with no argument cleared the
	/// scope, which hides the caller's registers while the body runs but leaves them to the caller (clear_allq,
	/// src/funmisc.c:573-603).
	/// </summary>
	private sealed class LetScope(Dictionary<string, MString> caller) : Dictionary<string, MString>(caller)
	{
		public bool Stopped { get; set; }
	}

	/// <summary>
	/// PE_REGS_LET (src/function.c): only the registers letq() named are its own. Every other register the
	/// body set or cleared is the caller's, so the change is copied back.
	/// </summary>
	private static void PassUnnamedRegisters(LetScope scope, Dictionary<string, MString> caller,
		HashSet<string> local)
	{
		foreach (var (name, value) in scope.Where(register => !local.Contains(register.Key)))
		{
			caller[name] = value;
		}

		if (scope.Stopped) return;

		foreach (var name in caller.Keys.Where(name => !local.Contains(name) && !scope.ContainsKey(name)).ToList())
		{
			caller.Remove(name);
		}
	}

	[SharpFunction(Name = "listq", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> ListQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var pattern = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message.ToPlainText();
		return ValueTask.FromResult(new CallState(
			string.Join(" ", VisibleRegisterNames(parser.CurrentState, RegisterKinds.QRegisters, pattern))));
	}

	[SharpFunction(Name = "registers", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> Registers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var pattern = args.GetValueOrDefault("0")?.Message.ToPlainText();
		var kinds = ParseRegisterKinds(args.GetValueOrDefault("1")?.Message.ToPlainText() ?? string.Empty);
		if (kinds == RegisterKinds.None)
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidArgument));

		var separator = args.GetValueOrDefault("2")?.Message.ToPlainText() ?? " ";
		return ValueTask.FromResult(new CallState(
			string.Join(separator, VisibleRegisterNames(parser.CurrentState, kinds, pattern))));
	}

	[SharpFunction(Name = "unsetq", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public ValueTask<CallState> UnSetQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var patterns = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message.ToPlainText()
			.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
		if (parser.CurrentState.Registers.TryPeek(out var registers))
		{
			// No pattern, or a lone "*" among them, clears the whole scope.
			if (patterns.Length == 0 || patterns.Contains("*"))
			{
				registers.Clear();
				if (registers is LetScope let) let.Stopped = true;
			}
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

	/// <summary>
	/// RhostMUSH's <c>nameq()</c> (<c>fun_nameq</c>, <c>Server/src/functions.c</c>): reads, sets or lists
	/// the labels setq()'s third argument gives registers.
	/// <list type="bullet">
	/// <item><c>nameq(&lt;register&gt;)</c>: the register's label; given a label, the label.</item>
	/// <item><c>nameq(&lt;register&gt;, &lt;label&gt;)</c>: sets the label, or clears it when empty. A label
	/// of one character is ignored, and one another register has is refused.</item>
	/// <item><c>nameq(&lt;label&gt;, , &lt;number&gt;)</c>: the register the label names.</item>
	/// <item><c>nameq(&lt;pattern&gt;, , &lt;type&gt;)</c>: the registers whose label (<c>l</c>), name
	/// (<c>n</c>), value (<c>v</c>), label or value (<c>u</c>) match the wildcard pattern, and those
	/// with both label and value whose label or value (<c>b</c>), label or name (<c>f</c>), or any of
	/// the three (<c>a</c>) match. Rhost's capital letters pick its letter-or-number display, which
	/// named registers do not have, so they mean the same here.</item>
	/// </list>
	/// </summary>
	[SharpFunction(Name = "nameq", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> NameQ(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var name = args["0"].Message.ToPlainText();
		var second = args.TryGetValue("1", out var labelArg) ? labelArg.Message.ToPlainText() : string.Empty;
		var mode = args.TryGetValue("2", out var modeArg) ? modeArg.Message.ToPlainText().Trim() : string.Empty;
		if (!parser.CurrentState.Registers.TryPeek(out var frame) || name.Length == 0)
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.BadRegName));

		// Any other text is a number to Rhost's atoi(), and so 0.
		if (mode.Length > 0 && "lnvubfa".Contains(char.ToLowerInvariant(mode[0])))
			return ValueTask.FromResult(new CallState(ListLabelledRegisters(frame, name, char.ToLowerInvariant(mode[0]))));

		var upper = name.ToUpper();
		var register = RegisterLabels.Resolve(frame, upper);
		var isLabel = register != upper;
		if (!isLabel && !IsRegisterName(register))
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.BadRegName));

		if (args.Count == 2)
			return ValueTask.FromResult(new CallState(RenameRegister(frame, register, second)));

		var wantsRegister = ArgHelpers.ParseInteger(mode) != 0;
		RegisterLabels.TryGetLabel(frame, register, out var label);
		return ValueTask.FromResult(new CallState(wantsRegister && isLabel ? register : label));
	}

	/// <summary>
	/// RhostMUSH's nameqm(): nameq()'s two-argument form for each register and label pair in turn. A pair
	/// with either half empty is <c>#-1 ARGUMENTS EXPECTED</c>; nothing is cleared.
	/// </summary>
	[SharpFunction(Name = "nameqm", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.EvenArgsOnly,
		ParameterNames = ["register", "label", "..."])]
	public ValueTask<CallState> NameQM(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (!parser.CurrentState.Registers.TryPeek(out var frame))
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.BadRegName));

		var result = new StringBuilder();
		for (var i = 0; i + 1 < args.Count; i += 2)
		{
			var name = args[i.ToString()].Message.ToPlainText();
			var label = args[(i + 1).ToString()].Message.ToPlainText();
			if (name.Length == 0 || label.Length == 0)
			{
				result.Append(ErrorMessages.Returns.ArgumentsExpected);
				continue;
			}

			var upper = name.ToUpper();
			var register = RegisterLabels.Resolve(frame, upper);
			result.Append(register == upper && !IsRegisterName(register)
				? ErrorMessages.Returns.BadRegName
				: RenameRegister(frame, register, label));
		}

		return ValueTask.FromResult(new CallState(result.ToString()));
	}

	private static bool IsRegisterName(string name)
		=> name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');

	/// <summary>nameq()'s two-argument form: sets, or with an empty label clears, a register's label.</summary>
	private static string RenameRegister(Dictionary<string, MString> frame, string register, string label)
	{
		if (label.Length == 0)
		{
			RegisterLabels.Clear(frame, register);
			return string.Empty;
		}

		if (label.Length < 2) return string.Empty;
		if (!RegisterLabels.IsValidLabel(label)) return ErrorMessages.Returns.InvalidLabel;
		if (RegisterLabels.IsInUse(frame, label)) return string.Format(ErrorMessages.Returns.LabelInUseFormat, label);
		RegisterLabels.Set(frame, register, label);
		return string.Empty;
	}

	/// <summary>nameq()'s listing form: the registers, by name, that <paramref name="type"/> and the pattern pick.</summary>
	private static string ListLabelledRegisters(Dictionary<string, MString> frame, string pattern, char type)
	{
		var matcher = SoftcodeRegex.Wildcard(pattern);
		bool Matches(string text) => text.Length > 0 && matcher.IsMatch(text);

		var picked = RegisterLabels.Registers(frame)
			.OrderBy(register => register, StringComparer.Ordinal)
			.Where(register =>
			{
				RegisterLabels.TryGetLabel(frame, register, out var label);
				var value = frame.TryGetValue(register, out var held) ? held.ToPlainText() : string.Empty;
				var both = label.Length > 0 && value.Length > 0;
				return type switch
				{
					'l' => Matches(label),
					'n' => value.Length > 0 && matcher.IsMatch(register),
					'v' => Matches(value),
					'u' => Matches(label) || Matches(value),
					'b' => both && (Matches(label) || Matches(value)),
					'f' => both && (Matches(label) || matcher.IsMatch(register)),
					'a' => both && (Matches(label) || Matches(value) || matcher.IsMatch(register)),
					_ => false
				};
			});
		return string.Join(" ", picked);
	}

	[SharpFunction(Name = "itext", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var levelArg = args.Count == 0 ? string.Empty : args["0"].Message.ToPlainText();
		var maxCount = parser.CurrentState.IterationRegisters.Count;

		// itext() is TinyMUX's itext(0), and empty outside a loop; PennMUSH refuses an empty level, so
		// this replaces only an error.
		if (levelArg.Length == 0)
		{
			return ValueTask.FromResult(maxCount == 0
				? new CallState(MarkupText.Empty)
				: new CallState(parser.CurrentState.IterationRegisters.First().Value));
		}

		if (levelArg.Equals("L", StringComparison.OrdinalIgnoreCase))
		{
			if (maxCount == 0)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.RegisterRange));
			}
			return ValueTask.FromResult(new CallState(parser.CurrentState.IterationRegisters.Last().Value));
		}

		if (!ArgHelpers.TryStrictInteger(levelArg, out int level))
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
		if (args.TryGetValue("0", out var depthArg))
		{
			var depthStr = depthArg.Message.ToPlainText().Trim();

			// Skip processing if the argument is empty (defaults to 0)
			if (!string.IsNullOrEmpty(depthStr))
			{
				// Handle "L" or "l" for outermost (last) switch
				if (depthStr.Equals("L", StringComparison.OrdinalIgnoreCase))
				{
					depth = stack.Count - 1;
				}
				else if (!ArgHelpers.TryStrictInteger(depthStr, out depth) || depth < 0)
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
		return ValueTask.FromResult(new CallState(item?.Text ?? MarkupText.Empty));
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
		if (!ArgHelpers.TryStrictInteger(text, out long levels))
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
		var levelArg = args["0"].Message.ToPlainText();
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

		if (!ArgHelpers.TryStrictInteger(levelArg, out int level))
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
