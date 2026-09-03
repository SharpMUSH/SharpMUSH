using ANSILibrary;
using MarkupString.MarkupImplementation;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Highlight survives the trip to HTML.
///
/// <para><c>ansi()</c> folds a highlight into the colour it applies to, emitting the byte pair
/// <c>[1, 34]</c> for <c>hb</c> — the ANSI bold attribute followed by blue. Both halves were being
/// dropped on the way to a browser: <see cref="ANSI.AnsiToRgb"/> matched a two-byte sequence and
/// kept only the colour, so <c>ansi(hb,…)</c> rendered identically to <c>ansi(b,…)</c>, and a bare
/// <c>ansi(h,…)</c> — a highlight with no colour to ride on — recorded nothing at all and vanished.
/// A terminal shows both as bold/bright; the portal showed neither.</para>
/// </summary>
public class AnsiHighlightFidelityTests
{
	/// <summary>
	/// Bold + a base colour is the bright variant of that colour. The palette has carried the bright
	/// range (90-97, 100-107) all along; nothing was reaching it.
	/// </summary>
	[Test]
	[Arguments(30, 85, 85, 85)]     // bright black -> grey
	[Arguments(31, 255, 85, 85)]    // bright red
	[Arguments(32, 85, 255, 85)]    // bright green
	[Arguments(34, 85, 85, 255)]    // bright blue — the case that reported as plain #0000aa
	[Arguments(37, 255, 255, 255)]  // bright white
	public async Task HighlightedForeground_IsTheBrightVariant(int code, int r, int g, int b)
	{
		var colour = ANSI.AnsiToRgb([1, (byte)code]);

		await Assert.That((colour.R, colour.G, colour.B)).IsEqualTo(((byte)r, (byte)g, (byte)b));
	}

	[Test]
	[Arguments(41, 255, 85, 85)]
	[Arguments(44, 85, 85, 255)]
	public async Task HighlightedBackground_IsTheBrightVariant(int code, int r, int g, int b)
	{
		var colour = ANSI.AnsiToRgb([1, (byte)code]);

		await Assert.That((colour.R, colour.G, colour.B)).IsEqualTo(((byte)r, (byte)g, (byte)b));
	}

	/// <summary>An unhighlighted colour is unchanged — the dim base is still the base.</summary>
	[Test]
	[Arguments(31, 170, 0, 0)]
	[Arguments(34, 0, 0, 170)]
	public async Task PlainForeground_KeepsItsBaseColour(int code, int r, int g, int b)
	{
		var colour = ANSI.AnsiToRgb([(byte)code]);

		await Assert.That((colour.R, colour.G, colour.B)).IsEqualTo(((byte)r, (byte)g, (byte)b));
	}

	/// <summary>
	/// A code outside the brightenable ranges is left alone rather than shifted into nonsense — a
	/// highlight on an xterm-256 index has no bright twin to promote to.
	/// </summary>
	[Test]
	public async Task HighlightOnAnXtermIndex_IsNotShifted()
	{
		var highlighted = ANSI.AnsiToRgb([1, 200]);
		var plain = ANSI.AnsiToRgb([200]);

		await Assert.That((highlighted.R, highlighted.G, highlighted.B))
			.IsEqualTo((plain.R, plain.G, plain.B));
	}

	/// <summary>
	/// A structure carrying Bold renders the class. Pinned because the highlight path deliberately
	/// does NOT set it — the highlight rides in the colour bytes, and setting both made the ANSI
	/// renderer emit the bold attribute twice — so this is the only thing keeping the HTML side of
	/// Bold honest for the markup that does set it.
	/// </summary>
	[Test]
	public async Task BoldStructure_RendersTheBoldClass()
	{
		var html = AnsiMarkup.WrapAsHtmlClass(
			new AnsiStructure
			{
				Bold = true,
				Foreground = AnsiColor.NoAnsi.Instance,
				Background = AnsiColor.NoAnsi.Instance
			}, "text");

		await Assert.That(html).Contains("ms-bold");
	}
}
