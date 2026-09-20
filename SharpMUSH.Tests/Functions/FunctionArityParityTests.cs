using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The declared argument counts, checked against PennMUSH's <c>flist[]</c> (src/function.c), a copy
/// of which lives in <c>PennMUSH/function-arity.tsv</c>.
///
/// <para>A wrong <c>MinArgs</c> is invisible at every other level: the help file says one thing, the
/// inlay hints say another and the engine quietly accepts a call PennMUSH rejects. The whole boolean
/// and bitwise families shipped with <c>MinArgs</c> left at its zero default, so <c>and()</c>
/// answered <c>0</c> where PennMUSH answers
/// <c>#-1 FUNCTION (AND) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1</c>; <c>max()</c> and <c>min()</c>
/// had the opposite fault and demanded two where PennMUSH folds over one.</para>
/// </summary>
public class FunctionArityParityTests : ServerTestBase
{
	/// <summary>
	/// Every registered arity that deliberately differs from PennMUSH's, with the reason. The table is
	/// exhaustive: <see cref="EveryFunctionDeclaresPennsArity"/> fails on anything not listed here, and
	/// equally on an entry here that no longer differs, so a fix cannot leave a stale excuse behind.
	/// </summary>
	private static readonly Dictionary<string, string> DeliberateDivergences = new(StringComparer.OrdinalIgnoreCase)
	{
		// docs/superpowers/specs/2026-09-08-millisecond-precision-design.md: every time function takes
		// one more trailing argument than PennMUSH's, naming the unit it answers in. Omitting it keeps
		// PennMUSH's unit, so the extra slot is additive.
		["convtime"] = "trailing <precision> argument (millisecond-precision design)",
		["convutctime"] = "trailing <precision> argument (millisecond-precision design)",
		["csecs"] = "trailing <precision> argument (millisecond-precision design)",
		["etime"] = "trailing <precision> argument (millisecond-precision design)",
		["etimefmt"] = "trailing <precision> argument (millisecond-precision design)",
		["idle"] = "trailing <precision> argument (millisecond-precision design)",
		["msecs"] = "trailing <precision> argument (millisecond-precision design)",
		["secs"] = "trailing <precision> argument (millisecond-precision design)",
		["stringsecs"] = "trailing <precision> argument (millisecond-precision design)",
		["timestring"] = "trailing <precision> argument (millisecond-precision design)",
		["uptime"] = "trailing <precision> argument (millisecond-precision design)",

		// The vector family takes an output separator PennMUSH does not, defaulting to <delimiter>,
		// which is how every other SharpMUSH list function spells the same idea.
		["vadd"] = "trailing <osep> argument, defaulting to <delimiter>",
		["vcross"] = "trailing <osep> argument, defaulting to <delimiter>",
		["vdot"] = "trailing <osep> argument, defaulting to <delimiter>",
		["vmax"] = "trailing <osep> argument, defaulting to <delimiter>",
		["vmin"] = "trailing <osep> argument, defaulting to <delimiter>",
		["vmul"] = "trailing <osep> argument, defaulting to <delimiter>",
		["vsub"] = "trailing <osep> argument, defaulting to <delimiter>",

		// PennMUSH spells "the last argument swallows the rest" as a negative maxargs, which this
		// table records as its absolute value. SharpMUSH splits every call on commas and counts the
		// pieces before it decides what to do with them, so the declared maximum has to admit as many
		// commas as the text contains; the swallowing happens after, and the number is not comparable.
		["ansi"] = "PennMUSH's -2: the text past <codes> is rejoined rather than left unsplit",
		["lit"] = "PennMUSH's -1: FunctionFlags.Literal collapses the split back into one raw argument",

		// Arguments past PennMUSH's fourth are bound as prepared-statement parameters instead of being
		// rejected, which is what keeps softcode from concatenating values into the query text.
		["sql"] = "arguments past <register> are prepared-statement parameters",
		["mapsql"] = "arguments past <fieldnames> are prepared-statement parameters",

		// Argument-less forms PennMUSH does not offer, each answering a listing rather than erroring.
		["config"] = "config() with no argument lists every option name",
		["stext"] = "stext() with no argument is the current switch, matching stext(0)",

		// PennMUSH declares LWHOID 0..1 and LWHO 0..2 although one C function serves both; the
		// <status> argument works in both. SharpMUSH declares the pair alike.
		["lwhoid"] = "accepts the <status> argument LWHO takes, which fun_lwho honours either way",

		// Tracked gaps, not decisions — SharpMUSH is narrower than PennMUSH here and the fix is
		// implementation work rather than a number in the attribute. See #974.
		["hasattr"] = "GAP (#974): PennMUSH also accepts the single-argument <object>/<attribute> form",
		["hasattrp"] = "GAP (#974): PennMUSH also accepts the single-argument <object>/<attribute> form",
		["hasattrval"] = "GAP (#974): PennMUSH also accepts the single-argument <object>/<attribute> form",
		["hasattrpval"] = "GAP (#974): PennMUSH also accepts the single-argument <object>/<attribute> form",
		["pcreate"] = "GAP (#974): PennMUSH takes an optional third <dbref> to reuse",
		["textentries"] = "GAP (#974): PennMUSH is textentries(<type>, <pattern>[, <osep>]); SharpMUSH has no <pattern> and lists every entry"
	};

	/// <summary>
	/// PennMUSH counts <c>fn()</c> as one empty argument unless the function's minimum is zero
	/// (parse.c:2974), so these expect "BUT GOT 1" rather than "BUT GOT 0". Only the functions whose
	/// minimum exceeds one have an observable empty-call difference; the rest are pinned by
	/// <see cref="EveryFunctionDeclaresPennsArity"/>.
	/// </summary>
	[Test]
	[Arguments("and", 2)]
	[Arguments("cand", 2)]
	[Arguments("cor", 2)]
	[Arguments("or", 2)]
	[Arguments("xor", 2)]
	public async Task AnEmptyCallIsRejectedWithPennsMinimum(string function, int minArgs)
	{
		var result = await Eval($"{function}()");

		await Assert.That(result).IsEqualTo(
			$"#-1 FUNCTION ({function.ToUpperInvariant()}) EXPECTS AT LEAST {minArgs} ARGUMENTS BUT GOT 1");
	}

	/// <summary>
	/// <c>attrib_set#()</c> is registered but unreachable: the lexer's function-name token is
	/// <c>FUNCHAR: [0-9a-zA-Z_~@`]+ '('</c> (SharpMUSHLexer.g4:24) and does not admit <c>#</c>, so the
	/// call never lexes as a call and the text passes through verbatim. The name is in the registry,
	/// the inlay hints and now the helpfile, and none of those can see that it does nothing.
	///
	/// <para>Fixing the token is a grammar change; this pins what a caller sees until then, and fails
	/// the moment the name starts working so that the helpfile's caveat gets removed with it.</para>
	/// </summary>
	[Test]
	public async Task AttribSetSharpIsRegisteredButTheLexerCannotReachIt()
		=> await Assert.That(await Eval("attrib_set#(me/parity, value)"))
			.IsEqualTo("attrib_set#(me/parity, value)");

	/// <summary>
	/// <c>{"MAX", fun_max, 1, INT_MAX, …}</c> — one argument is a fold over a one-element list, and
	/// PennMUSH returns it rather than erroring.
	/// </summary>
	[Test]
	[Arguments("max")]
	[Arguments("min")]
	public async Task OneArgumentFoldsToItself(string function)
		=> await Assert.That(await Eval($"{function}(5)")).IsEqualTo("5");

	[Test]
	public async Task EveryFunctionDeclaresPennsArity()
	{
		var penn = PennArity();
		var offenders = new List<string>();

		foreach (var entry in RegistryInventory.Functions.Where(f => !DeliberateDivergences.ContainsKey(f.Name)))
		{
			if (!penn.TryGetValue(entry.Name, out var expected)) continue;
			if (Agrees(entry.Attribute, expected)) continue;

			offenders.Add(
				$"{entry.Name}: declared {entry.Attribute.MinArgs}..{entry.Attribute.MaxArgs}, "
				+ $"PennMUSH {expected.Min}..{Describe(expected.Max)} ({entry.Origin})");
		}

		await Assert.That(offenders).IsEmpty();
	}

	/// <summary>
	/// A divergence that stopped diverging keeps an explanation alive for behaviour that no longer
	/// exists, which is exactly how the compatibility notes went stale before (#1134).
	/// </summary>
	[Test]
	public async Task NoDeliberateDivergenceStillAgreesWithPenn()
	{
		var penn = PennArity();
		var byName = RegistryInventory.Functions.ToDictionary(f => f.Name, f => f.Attribute, StringComparer.OrdinalIgnoreCase);

		var stale = DeliberateDivergences.Keys
			.Where(name => byName.TryGetValue(name, out var attribute)
				&& penn.TryGetValue(name, out var expected)
				&& Agrees(attribute, expected))
			.ToList();

		await Assert.That(stale).IsEmpty();
	}

	/// <summary>Every name in the divergence table must still be a registered function.</summary>
	[Test]
	public async Task NoDeliberateDivergenceNamesAFunctionThatIsGone()
	{
		var registered = RegistryInventory.Functions.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

		await Assert.That(DeliberateDivergences.Keys.Where(name => !registered.Contains(name))).IsEmpty();
	}

	/// <summary>
	/// PennMUSH registers <c>{"ADD", fun_add, 2, INT_MAX, …}</c> and enforces nothing beyond it.
	/// SharpMUSH spelled "unbounded" as the <c>[SharpFunction]</c> default of 32, so a valid
	/// <c>add()</c> of 33 ones was refused before it was ever evaluated (#1102).
	/// </summary>
	[Test]
	[Arguments("add", "1", "33")]
	[Arguments("strcat", "x", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
	[Arguments("max", "7", "7")]
	public async Task AnUnboundedFunctionTakesThirtyThreeArguments(string function, string argument, string expected)
		=> await Assert.That(await Eval($"{function}({string.Join(',', Enumerable.Repeat(argument, 33))})"))
			.IsEqualTo(expected);

	/// <summary>
	/// The ceiling was not only the attribute default: <c>localfun</c> hardcoded 33 — a name plus the
	/// same 32 — so the count was checked before the registry was ever consulted. An unknown name
	/// answers "not found" however many arguments follow it.
	/// </summary>
	[Test]
	public async Task LocalFunResolvesTheNameRatherThanCountingToThirtyTwo()
		=> await Assert.That(await Eval($"localfun(nosuchlocalfun,{string.Join(',', Enumerable.Repeat("x", 33))})"))
			.IsEqualTo("#-1 FUNCTION (NOSUCHLOCALFUN) NOT FOUND");

	/// <summary>
	/// An omitted <c>MaxArgs</c> is the common case — 32 of the registrations relied on it — so the
	/// default has to mean PennMUSH's <c>INT_MAX</c>. A default that caps instead fails closed, and
	/// silently, on every function nobody thought to bound.
	/// </summary>
	[Test]
	public async Task AnOmittedMaximumIsUnbounded()
		=> await Assert.That(new SharpFunctionAttribute { Name = "unregistered", Flags = FunctionFlags.Regular }.MaxArgs)
			.IsEqualTo(int.MaxValue);

	/// <summary>
	/// 32 is not a PennMUSH number anywhere — it was this engine's accidental default. A registration
	/// that spells it out is a copy of that mistake.
	/// </summary>
	[Test]
	public async Task NoRegistrationDeclaresTheOldThirtyTwoArgumentCeiling()
		=> await Assert.That(RegistryInventory.Functions
				.Where(entry => entry.Attribute.MaxArgs == 32)
				.Select(entry => $"{entry.Name} ({entry.Origin})"))
			.IsEmpty();

	/// <summary>
	/// <c>{"ANSI", fun_ansi, 2, -2, …}</c> (function.c:365). PennMUSH's negative maximum means the
	/// last argument swallows the rest, so <c>ansi(h,a,b)</c> colours the text <c>a,b</c>. SharpMUSH
	/// read <c>args["1"]</c> alone and dropped everything past the first comma on the floor.
	/// </summary>
	[Test]
	public async Task AnsiColoursEverythingPastTheCodes()
		=> await Assert.That(await Eval("ansi(h,a,b,c)")).IsEqualTo("a,b,c");

	/// <summary>
	/// <c>@function &lt;name&gt;=&lt;obj&gt;,&lt;attr&gt;</c> with no bounds gets PennMUSH's
	/// <c>DEF_FUNCTION_ARGS</c> of 10 (function.c:1720, hdrs/function.h:133) — not the engine's own
	/// attribute default, which is what the code claimed it was taking.
	/// </summary>
	[Test]
	public async Task AUserFunctionWithoutBoundsTakesPennsTenArguments()
	{
		var (fn, attr) = await DefineUserFunction(string.Empty);

		await Assert.That(await Eval($"{fn}({string.Join(',', Enumerable.Repeat("x", 10))})")).IsEqualTo("ok");
		await Assert.That(await Eval($"{fn}({string.Join(',', Enumerable.Repeat("x", 11))})"))
			.IsEqualTo($"#-1 FUNCTION ({fn.ToUpperInvariant()}) EXPECTS AT MOST 10 ARGUMENTS BUT GOT 11");
	}

	/// <summary>
	/// An explicit maximum is clamped to <c>MAX_STACK_ARGS</c> (function.c:1717, hdrs/conf.h:29) —
	/// the engine only carries 30 positional arguments, so a larger number is not a promise it can keep.
	/// </summary>
	[Test]
	public async Task AUserFunctionMaximumIsClampedToPennsStackLimit()
	{
		var (fn, _) = await DefineUserFunction(",0,99");

		await Assert.That(await Eval($"{fn}({string.Join(',', Enumerable.Repeat("x", 30))})")).IsEqualTo("ok");
		await Assert.That(await Eval($"{fn}({string.Join(',', Enumerable.Repeat("x", 31))})"))
			.IsEqualTo($"#-1 FUNCTION ({fn.ToUpperInvariant()}) EXPECTS AT MOST 30 ARGUMENTS BUT GOT 31");
	}

	/// <summary>
	/// A negative maximum is PennMUSH's "the last argument is not split" marker, and
	/// <c>fp-&gt;maxargs *= -1</c> (function.c:1716) keeps the count it carries. A user function has
	/// no such marker, so only the count survives.
	/// </summary>
	[Test]
	public async Task AUserFunctionNegativeMaximumKeepsItsMagnitude()
	{
		var (fn, _) = await DefineUserFunction(",0,-2");

		await Assert.That(await Eval($"{fn}(x,y)")).IsEqualTo("ok");
		await Assert.That(await Eval($"{fn}(x,y,z)"))
			.IsEqualTo($"#-1 FUNCTION ({fn.ToUpperInvariant()}) EXPECTS AT MOST 2 ARGUMENTS BUT GOT 3");
	}

	/// <summary>Defines a user function answering <c>ok</c>, with <paramref name="bounds"/> appended verbatim.</summary>
	private async Task<(string Function, string Attribute)> DefineUserFunction(string bounds)
	{
		var unique = Guid.NewGuid().ToString("N")[..8];
		var (fn, attr) = ($"arity{unique}", $"ARITY{unique}");

		await Cmd($"&{attr} me=ok");
		await Cmd($"@function {fn}=me,{attr}{bounds}");

		return (fn, attr);
	}

	/// <summary>
	/// PennMUSH spells "as many as you like" as <c>INT_MAX</c>, and so does SharpMUSH: a declared
	/// maximum that merely happens to be large does not satisfy an unbounded entry. The earlier
	/// "at or above the attribute default" tolerance is what let every unbounded function sit at 32
	/// and still read as agreeing (#1102).
	/// </summary>
	private static bool Agrees(SharpFunctionAttribute attribute, (int Min, int Max) penn) =>
		attribute.MinArgs == penn.Min
		&& (penn.Max == int.MaxValue ? attribute.MaxArgs == int.MaxValue : attribute.MaxArgs == penn.Max);

	private static string Describe(int max) => max == int.MaxValue ? "INF" : max.ToString();

	private static Dictionary<string, (int Min, int Max)> PennArity()
	{
		var path = Path.Combine(TestPaths.RepositoryRoot, "SharpMUSH.Tests", "PennMUSH", "function-arity.tsv");
		var table = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

		var rows = File.ReadLines(path)
			.Where(line => line.Length > 0 && line[0] != '#')
			.Select(line => line.Split('\t'));

		foreach (var fields in rows)
		{
			table[fields[0]] = (int.Parse(fields[1]), fields[2] == "INF" ? int.MaxValue : int.Parse(fields[2]));
		}

		return table;
	}
}
