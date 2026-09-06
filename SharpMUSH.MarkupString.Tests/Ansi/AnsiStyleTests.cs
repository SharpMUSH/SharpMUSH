using MarkupString.Ansi;

public class AnsiStyleTests
{
	private static readonly AnsiColor Red = new AnsiColor.Standard(1, false);
	private static readonly AnsiColor Blue = new AnsiColor.Standard(4, false);
	private static readonly AnsiColor Green = new AnsiColor.Standard(2, false);

	[Test]
	public async Task None_IsNone()
	{
		await Assert.That(AnsiStyle.None.IsNone).IsTrue();
		await Assert.That(AnsiStyle.None.HasAnyAttribute).IsFalse();
		await Assert.That(default(AnsiStyle)).IsEqualTo(AnsiStyle.None);
	}

	[Test]
	public async Task HasAnyAttribute_TrueForColourOrFlag()
	{
		await Assert.That(new AnsiStyle { Foreground = Red }.HasAnyAttribute).IsTrue();
		await Assert.That(new AnsiStyle { Background = Red }.HasAnyAttribute).IsTrue();
		await Assert.That(new AnsiStyle { Bold = true }.HasAnyAttribute).IsTrue();
		await Assert.That(new AnsiStyle { Clear = true }.HasAnyAttribute).IsTrue();
		await Assert.That(new AnsiStyle { LinkUrl = "https://example.com" }.HasAnyAttribute).IsFalse();
	}

	[Test]
	public async Task IsNone_FalseWhenALinkIsSet()
	{
		await Assert.That(new AnsiStyle { LinkUrl = "https://example.com" }.IsNone).IsFalse();
		await Assert.That(new AnsiStyle { Underlined = true }.IsNone).IsFalse();
	}

	[Test]
	public async Task StylesAreValueEqual()
	{
		var a = new AnsiStyle { Foreground = new AnsiColor.Standard(1, false), Bold = true };
		var b = new AnsiStyle { Foreground = new AnsiColor.Standard(1, false), Bold = true };
		await Assert.That(a).IsEqualTo(b);
		await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
	}

	[Test]
	public async Task Combine_InnerColourWins()
	{
		var outer = new AnsiStyle { Foreground = Red, Background = Green };
		var inner = new AnsiStyle { Foreground = Blue };
		var result = outer.Combine(inner);
		await Assert.That(result.Foreground).IsEqualTo(Blue);
		await Assert.That(result.Background).IsEqualTo(Green);
	}

	[Test]
	public async Task Combine_OuterColourKeptWhenInnerNull()
	{
		var outer = new AnsiStyle { Foreground = Red, Background = Green };
		var result = outer.Combine(AnsiStyle.None);
		await Assert.That(result.Foreground).IsEqualTo(Red);
		await Assert.That(result.Background).IsEqualTo(Green);
	}

	[Test]
	public async Task Combine_FlagsAreOred()
	{
		var outer = new AnsiStyle { Bold = true, Italic = true };
		var inner = new AnsiStyle { Underlined = true, Italic = false };
		var result = outer.Combine(inner);
		await Assert.That(result.Bold).IsTrue();
		await Assert.That(result.Italic).IsTrue();
		await Assert.That(result.Underlined).IsTrue();
		await Assert.That(result.Blink).IsFalse();
	}

	[Test]
	public async Task Combine_AllFlagsPropagateFromEitherSide()
	{
		var outer = new AnsiStyle
		{
			Bold = true,
			Faint = true,
			Italic = true,
			Underlined = true,
			Overlined = true
		};
		var inner = new AnsiStyle { Blink = true, Inverted = true, StrikeThrough = true };
		var result = outer.Combine(inner);
		await Assert.That(result.Bold).IsTrue();
		await Assert.That(result.Faint).IsTrue();
		await Assert.That(result.Italic).IsTrue();
		await Assert.That(result.Underlined).IsTrue();
		await Assert.That(result.Overlined).IsTrue();
		await Assert.That(result.Blink).IsTrue();
		await Assert.That(result.Inverted).IsTrue();
		await Assert.That(result.StrikeThrough).IsTrue();
		await Assert.That(result.Clear).IsFalse();
	}

	[Test]
	public async Task Combine_InnerClear_DiscardsOuter()
	{
		var outer = new AnsiStyle
		{
			Foreground = Red,
			Background = Green,
			Bold = true,
			Underlined = true,
			LinkUrl = "https://example.com"
		};
		var inner = new AnsiStyle { Clear = true, Foreground = Blue };
		var result = outer.Combine(inner);
		await Assert.That(result.Clear).IsTrue();
		await Assert.That(result.Foreground).IsEqualTo(Blue);
		await Assert.That(result.Background).IsNull();
		await Assert.That(result.Bold).IsFalse();
		await Assert.That(result.Underlined).IsFalse();
		await Assert.That(result.LinkUrl).IsNull();
	}

	[Test]
	public async Task Combine_OuterClear_IsKept()
	{
		// Clear says "this run begins by resetting ambient SGR state" — a statement about the
		// previous run, not something a fold may consume. An outer span that clears must still
		// carry that reset after an inner span (that does not itself clear) is folded onto it.
		var outer = new AnsiStyle { Clear = true, Foreground = Red };
		var result = outer.Combine(new AnsiStyle { Bold = true });
		await Assert.That(result.Clear).IsTrue();
		await Assert.That(result.Foreground).IsEqualTo(Red);
		await Assert.That(result.Bold).IsTrue();
	}

	[Test]
	public async Task Combine_WithNone_KeepsClear()
	{
		var style = new AnsiStyle { Clear = true, Foreground = Red };
		await Assert.That(style.Combine(AnsiStyle.None)).IsEqualTo(style);
	}

	[Test]
	public async Task Combine_InnerLinkWins()
	{
		var outer = new AnsiStyle { LinkUrl = "https://outer", LinkText = "outer", LinkKind = LinkKind.Url };
		var inner = new AnsiStyle { LinkUrl = "look", LinkText = "inner", LinkKind = LinkKind.Command };
		var result = outer.Combine(inner);
		await Assert.That(result.LinkUrl).IsEqualTo("look");
		await Assert.That(result.LinkText).IsEqualTo("inner");
		await Assert.That(result.LinkKind).IsEqualTo(LinkKind.Command);
	}

	[Test]
	public async Task Combine_OuterLinkKeptWhenInnerHasNoUrl()
	{
		var outer = new AnsiStyle { LinkUrl = "help me", LinkText = "hint", LinkKind = LinkKind.Command };
		var inner = new AnsiStyle { Bold = true, LinkText = "ignored", LinkKind = LinkKind.Url };
		var result = outer.Combine(inner);
		await Assert.That(result.LinkUrl).IsEqualTo("help me");
		await Assert.That(result.LinkText).IsEqualTo("hint");
		await Assert.That(result.LinkKind).IsEqualTo(LinkKind.Command);
	}

	[Test]
	public async Task Combine_WithNone_IsIdentityOnBothSides()
	{
		var style = new AnsiStyle
		{
			Foreground = Red,
			Background = Green,
			Italic = true,
			LinkUrl = "https://example.com",
			LinkText = "hint",
			LinkKind = LinkKind.Url
		};
		await Assert.That(style.Combine(AnsiStyle.None)).IsEqualTo(style);
		await Assert.That(AnsiStyle.None.Combine(style)).IsEqualTo(style);
	}

	[Test]
	public async Task AnsiMarkup_IsValueEqualAndWrapsTheStyle()
	{
		var a = AnsiMarkup.Create(foreground: Red, bold: true);
		var b = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false), bold: true);
		await Assert.That(a).IsEqualTo(b);
		await Assert.That(a.GetHashCode()).IsEqualTo(b.GetHashCode());
		await Assert.That(a.Style.Foreground).IsEqualTo(Red);
		await Assert.That(a.Style.Bold).IsTrue();
		await Assert.That(a.Style.Background).IsNull();
		await Assert.That(a is IMarkup).IsTrue();
	}

	[Test]
	public async Task AnsiMarkup_Create_DefaultsToTheEmptyStyle()
	{
		await Assert.That(AnsiMarkup.Create().Style).IsEqualTo(AnsiStyle.None);
	}

	[Test]
	public async Task AnsiMarkup_Create_CarriesEveryFlag()
	{
		var markup = AnsiMarkup.Create(
			foreground: Red,
			background: Blue,
			linkText: "hint",
			linkUrl: "look",
			linkKind: LinkKind.Command,
			blink: true,
			bold: true,
			clear: true,
			faint: true,
			inverted: true,
			italic: true,
			overlined: true,
			underlined: true,
			strikeThrough: true);
		var s = markup.Style;
		await Assert.That(s.Foreground).IsEqualTo(Red);
		await Assert.That(s.Background).IsEqualTo(Blue);
		await Assert.That(s.LinkText).IsEqualTo("hint");
		await Assert.That(s.LinkUrl).IsEqualTo("look");
		await Assert.That(s.LinkKind).IsEqualTo(LinkKind.Command);
		await Assert.That(s.Blink).IsTrue();
		await Assert.That(s.Bold).IsTrue();
		await Assert.That(s.Clear).IsTrue();
		await Assert.That(s.Faint).IsTrue();
		await Assert.That(s.Inverted).IsTrue();
		await Assert.That(s.Italic).IsTrue();
		await Assert.That(s.Overlined).IsTrue();
		await Assert.That(s.Underlined).IsTrue();
		await Assert.That(s.StrikeThrough).IsTrue();
	}
}
