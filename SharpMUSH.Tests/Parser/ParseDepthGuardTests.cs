using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// The generated parser is recursive descent with no depth check of its own, so a deeply
/// nested expression overflows the native stack and aborts the whole process with an
/// uncatchable error — a remote denial of service, since direct player input is not
/// length-capped. <see cref="MUSHCodeParser"/> refuses to parse past a fixed nesting depth
/// and returns the call-limit error instead. These cases run at and beyond the observed crash
/// depth; if the guard regressed, the test host would die rather than fail.
/// </summary>
public class ParseDepthGuardTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private static string Repeat(string s, int n) => string.Concat(Enumerable.Repeat(s, n));

	/// <summary>
	/// Parses <paramref name="input"/> and returns its plain text, asserting the result and its
	/// message are present first. Keeps the depth cases from failing with an opaque
	/// <see cref="NullReferenceException"/> (from a null-forgiving dereference) instead of a clear
	/// assertion when a parse unexpectedly yields nothing.
	/// </summary>
	private async Task<string> EvalPlain(string input)
	{
		var result = await Parser.FunctionParse(MModule.single(input));
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Message).IsNotNull();
		return result.Message!.ToPlainText();
	}

	// Above the guard (1000) so it must fire, and — for the largest — past the observed
	// ~11000-level overflow, which the guard now intercepts before parsing.
	[Test]
	[Arguments("brackets", 2000)]
	[Arguments("brackets", 12000)]
	[Arguments("braces", 2000)]
	[Arguments("braces", 14000)]
	[Arguments("functions", 2000)]
	[Arguments("functions", 14000)]
	public async Task RefusesOverDeepNesting(string kind, int depth)
	{
		var input = kind switch
		{
			"brackets" => new string('[', depth) + "x" + new string(']', depth),
			"braces" => new string('{', depth) + "x" + new string('}', depth),
			"functions" => Repeat("add(", depth) + "1" + new string(')', depth),
			_ => throw new ArgumentException(kind),
		};

		await Assert.That(await EvalPlain(input)).IsEqualTo("#-1 CALL LIMIT EXCEEDED");
	}

	/// <summary>
	/// A bare <c>(</c> is plain text, not a recursive rule, so <see cref="MUSHCodeParser"/>
	/// deliberately does not count it toward the nesting depth. This case runs a run of bare
	/// parentheses well past both the guard (1000) and the observed crash depth: it must parse
	/// as literal text — never the call-limit error — which is exactly the assumption that keeps
	/// the guard from either crashing on or wrongly rejecting this construct. If bare parens ever
	/// started recursing the parser, this is the case that would catch it (by killing the host).
	/// </summary>
	[Test]
	[Arguments(2000)]
	[Arguments(12000)]
	public async Task AllowsDeeplyNestedBareParentheses(int depth)
	{
		var input = new string('(', depth) + "x" + new string(')', depth);

		await Assert.That(await EvalPlain(input)).IsEqualTo(input);
	}

	/// <summary>
	/// Legitimately deep code — far deeper than any real softcode, but within the guard — must
	/// still parse and evaluate. Each <c>[...]</c> re-evaluates its contents, so any number of
	/// wrapping brackets around plain text collapses to that text. Depth here is exactly the
	/// bracket count; the maximum, 1000, is the guard's inclusive limit.
	/// </summary>
	[Test]
	[Arguments(1)]
	[Arguments(50)]
	[Arguments(500)]
	[Arguments(1000)]
	public async Task AllowsDeepButBoundedNesting(int depth)
	{
		var input = new string('[', depth) + "ok" + new string(']', depth);

		await Assert.That(await EvalPlain(input)).IsEqualTo("ok");
	}

	/// <summary>
	/// A function call is itself a nesting level, so it counts toward the limit alongside the
	/// brackets around it: 999 brackets plus <c>add(</c> is exactly 1000 and is allowed, while
	/// one more bracket tips it over and is refused.
	/// </summary>
	[Test]
	public async Task FunctionCallCountsAsANestingLevel()
	{
		var atLimit = new string('[', 999) + "add(1,2)" + new string(']', 999);
		var overLimit = new string('[', 1000) + "add(1,2)" + new string(']', 1000);

		await Assert.That(await EvalPlain(atLimit)).IsEqualTo("3");
		await Assert.That(await EvalPlain(overLimit)).IsEqualTo("#-1 CALL LIMIT EXCEEDED");
	}
}
