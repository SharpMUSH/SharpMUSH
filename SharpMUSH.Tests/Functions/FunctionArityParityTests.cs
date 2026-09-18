using SharpMUSH.Library.Attributes;

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
		// table records as its absolute value. SharpMUSH reaches the same result through the literal
		// flag on the function instead, so the declared maximum is not the comparable number.
		["ansi"] = "FN_LITERAL-equivalent: the trailing argument is not comma-split",
		["lit"] = "FN_LITERAL-equivalent: the trailing argument is not comma-split",

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
	/// PennMUSH's INT_MAX and SharpMUSH's attribute default of 32 both mean "as many as you like",
	/// so any declared maximum at or above the default satisfies an unbounded PennMUSH entry.
	/// </summary>
	private static bool Agrees(SharpFunctionAttribute attribute, (int Min, int Max) penn) =>
		attribute.MinArgs == penn.Min
		&& (penn.Max == int.MaxValue ? attribute.MaxArgs >= 32 : attribute.MaxArgs == penn.Max);

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
