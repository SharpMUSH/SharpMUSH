using ANSILibrary;
using MarkupString.MarkupImplementation;
using System.Drawing;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Covers the three levers that keep a <see cref="MarkupString.MarkupString"/> small: value
/// equality on markup (so runs can be compared), run coalescing at construction, and the
/// compact serialization format.
/// </summary>
public class MarkupCompressionTests
{
	/// <summary>
	/// Coalescing compares markup with <c>Equals</c>. Without value equality the comparison is
	/// reference equality, so two separately-created-but-identical markups never merge — which
	/// is every markup the ColorCode syntax highlighter emits.
	/// </summary>
	[Test]
	public async Task EqualButDistinctAnsiMarkups_CompareEqual()
	{
		var a = M.Create(foreground: new AnsiColor.RGB(Color.Red), bold: true);
		var b = M.Create(foreground: new AnsiColor.RGB(Color.Red), bold: true);

		await Assert.That(ReferenceEquals(a, b)).IsFalse();
		await Assert.That(a.Equals(b)).IsTrue();
		await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
	}

	[Test]
	public async Task DifferingAnsiMarkups_CompareUnequal()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blue = M.Create(foreground: new AnsiColor.RGB(Color.Blue));

		await Assert.That(red.Equals(blue)).IsFalse();
	}

	[Test]
	public async Task EqualButDistinctHtmlMarkups_CompareEqual()
	{
		var a = HtmlMarkup.Create("send", "href=look");
		var b = HtmlMarkup.Create("send", "href=look");

		await Assert.That(ReferenceEquals(a, b)).IsFalse();
		await Assert.That(a.Equals(b)).IsTrue();
		await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
	}

	// ── Run coalescing at construction ───────────────────────────────────────────

	[Test]
	public async Task ConcatenatingEquallyMarkedStrings_CoalescesAtConstruction()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var combined = A.concat(A.MarkupSingle(red, "Hello"), A.MarkupSingle(red, " World"));

		await Assert.That(combined.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(combined.Runs.Length).IsEqualTo(1);
		await Assert.That(combined.Runs[0].Start).IsEqualTo(0);
		await Assert.That(combined.Runs[0].Length).IsEqualTo("Hello World".Length);
	}

	[Test]
	public async Task ConcatenatingDifferentlyMarkedStrings_KeepsRunsSeparate()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var blue = M.Create(foreground: new AnsiColor.RGB(Color.Blue));
		var combined = A.concat(A.MarkupSingle(red, "Hello"), A.MarkupSingle(blue, " World"));

		await Assert.That(combined.Runs.Length).IsEqualTo(2);
	}

	/// <summary>
	/// The equivalence coalescing has to preserve: a string assembled from many equally-marked
	/// fragments must render exactly as the same text marked once. This is the safety property —
	/// the run count is an implementation detail, the rendered bytes are not.
	/// </summary>
	[Test]
	public async Task CoalescedRender_MatchesSingleRunRender()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red), bold: true);
		const string text = "The quick brown fox";

		var perCharacter = A.multiple(text.Select(c => A.MarkupSingle(red, c.ToString())).ToArray());
		var singleRun = A.MarkupSingle(red, text);

		await Assert.That(perCharacter.ToPlainText()).IsEqualTo(singleRun.ToPlainText());
		await Assert.That(perCharacter.Render("ansi")).IsEqualTo(singleRun.Render("ansi"));
		await Assert.That(perCharacter.Render("html")).IsEqualTo(singleRun.Render("html"));
		await Assert.That(perCharacter.Runs.Length).IsEqualTo(1);
	}

	/// <summary>
	/// A zero-length run carrying markup is how <c>MarkupSingle2</c> represents "this empty string
	/// is styled". Coalescing must not discard it.
	/// </summary>
	[Test]
	public async Task ZeroLengthMarkedRun_Survives()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var empty = A.MarkupSingle2(red, A.empty());

		await Assert.That(empty.ToPlainText()).IsEqualTo("");
		await Assert.That(empty.Runs.Length).IsEqualTo(1);
		await Assert.That(empty.Runs[0].Length).IsEqualTo(0);
		await Assert.That(empty.Runs[0].Markups.Length).IsEqualTo(1);
	}

	// ── Per-instance memory ──────────────────────────────────────────────────────

	/// <summary>
	/// A <see cref="MarkupString.MarkupString"/> used to allocate six <c>Lazy&lt;string&gt;</c> render
	/// caches and six closures in its constructor — about 840 bytes of the 936 a five-character string
	/// cost — whether or not anything ever rendered it. The parser builds and discards these by the
	/// thousand, and an intermediate is rendered zero times.
	/// </summary>
	[Test]
	public async Task ConstructingAMarkupString_DoesNotAllocateRenderCaches()
	{
		const int iterations = 10_000;

		// Warm the JIT and any statics so their allocations land outside the measurement.
		for (var i = 0; i < 100; i++) GC.KeepAlive(A.single("hello"));

		// Per-thread, not GC.GetTotalAllocatedBytes: that counts the whole process, so any test
		// running in parallel would land its allocations inside this window and inflate the result.
		// Everything between the two reads is synchronous, so it stays on one thread.
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < iterations; i++) GC.KeepAlive(A.single("hello"));
		var perInstance = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;

		// Measured at 936 bytes with the eager Lazy fields, 120 without. The bound leaves room for
		// allocator variation while still failing if the caches come back.
		// The lower bound is not padding: it fails the test if the measurement ever reads zero, which
		// would otherwise let a broken probe pass vacuously.
		await Assert.That(perInstance).IsBetween(32, 300);
	}

	/// <summary>
	/// Dropping <c>Lazy</c> must not drop the caching: a second render has to return the first
	/// render's instance, not recompute it.
	/// </summary>
	[Test]
	public async Task RenderingTwice_ReturnsTheCachedInstance()
	{
		var red = M.Create(foreground: new AnsiColor.RGB(Color.Red));
		var ams = A.MarkupSingle(red, "Hello World");

		foreach (var format in new[] { "ansi", "html", "plaintext", "pueblo", "mxp" })
		{
			// Bound to locals rather than compared inline: two calls to the same method look like a
			// tautology to static analysis, and naming them says what the test is actually about.
			var firstRender = ams.Render(format);
			var secondRender = ams.Render(format);

			await Assert.That(ReferenceEquals(firstRender, secondRender)).IsTrue()
				.Because($"the {format} render must be cached, not recomputed");
		}

		var firstToString = ams.ToString();
		var secondToString = ams.ToString();

		await Assert.That(ReferenceEquals(firstToString, secondToString)).IsTrue();
	}
}
