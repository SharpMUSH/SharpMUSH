using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Library.Extensions;
using System.Drawing;
using M = MarkupString.Ansi.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Covers the three levers that keep a <see cref="MarkupString.MarkupText"/> small: value
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
		var a = M.Create(foreground: Color.Red.ToAnsiColor(), bold: true);
		var b = M.Create(foreground: Color.Red.ToAnsiColor(), bold: true);

		await Assert.That(ReferenceEquals(a, b)).IsFalse();
		await Assert.That(a.Equals(b)).IsTrue();
		await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
	}

	[Test]
	public async Task DifferingAnsiMarkups_CompareUnequal()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		var blue = M.Create(foreground: Color.Blue.ToAnsiColor());

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
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		var combined = MarkupText.Concat(MarkupText.Wrap(red, "Hello"), MarkupText.Wrap(red, " World"));

		await Assert.That(combined.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(combined.Runs.Length).IsEqualTo(1);
		await Assert.That(combined.Runs[0].Start).IsEqualTo(0);
		await Assert.That(combined.Runs[0].Length).IsEqualTo("Hello World".Length);
	}

	[Test]
	public async Task ConcatenatingDifferentlyMarkedStrings_KeepsRunsSeparate()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		var blue = M.Create(foreground: Color.Blue.ToAnsiColor());
		var combined = MarkupText.Concat(MarkupText.Wrap(red, "Hello"), MarkupText.Wrap(blue, " World"));

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
		var red = M.Create(foreground: Color.Red.ToAnsiColor(), bold: true);
		const string text = "The quick brown fox";

		var perCharacter = MarkupText.Concat(text.Select(c => MarkupText.Wrap(red, c.ToString())).ToArray());
		var singleRun = MarkupText.Wrap(red, text);

		await Assert.That(perCharacter.ToPlainText()).IsEqualTo(singleRun.ToPlainText());
		await Assert.That(perCharacter.Render(MarkupFormat.Ansi)).IsEqualTo(singleRun.Render(MarkupFormat.Ansi));
		await Assert.That(perCharacter.Render(MarkupFormat.Html)).IsEqualTo(singleRun.Render(MarkupFormat.Html));
		await Assert.That(perCharacter.Runs.Length).IsEqualTo(1);
	}

	// ── Per-instance memory ──────────────────────────────────────────────────────

	/// <summary>
	/// Constructing markup text must stay cheap. The parser builds and discards intermediates by the
	/// thousand and renders none of them, so nothing about a render may be paid for at construction.
	/// </summary>
	[Test]
	public async Task ConstructingAMarkupString_DoesNotAllocateRenderCaches()
	{
		const int iterations = 10_000;

		// Warm the JIT and any statics so their allocations land outside the measurement.
		for (var i = 0; i < 100; i++) GC.KeepAlive(MarkupText.Plain("hello"));

		// Per-thread, not GC.GetTotalAllocatedBytes: that counts the whole process, so any test
		// running in parallel would land its allocations inside this window and inflate the result.
		// Everything between the two reads is synchronous, so it stays on one thread.
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < iterations; i++) GC.KeepAlive(MarkupText.Plain("hello"));
		var perInstance = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;

		// The bound leaves room for allocator variation while still failing if per-instance render
		// caches come back. The lower bound is not padding: it fails the test if the measurement ever
		// reads zero, which would otherwise let a broken probe pass vacuously.
		await Assert.That(perInstance).IsBetween(32, 300);
	}
}
