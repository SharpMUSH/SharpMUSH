using MarkupString.Ansi;

public class AnsiMarkupCodecTests
{
	private static readonly MarkupRegistry Registry = MarkupRegistry.Empty.WithAnsi();

	private static string Serialize(AnsiMarkup markup) =>
		MarkupTextSerializer.Serialize(MarkupText.Wrap(markup, "x"), Registry);

	private static AnsiStyle Deserialize(string json) =>
		((AnsiMarkup)MarkupTextSerializer.Deserialize(json, Registry).Runs[0].Markups.Innermost).Style;

	private static AnsiStyle RoundTrip(AnsiMarkup markup) => Deserialize(Serialize(markup));

	private static string Palette(string properties) =>
		$$"""{"t":"x","p":[null,[{{{properties}}}]],"r":[1,1]}""";

	[Test]
	public async Task Write_UsesTheAnsiKindAndCompactKeys()
	{
		var markup = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false), bold: true);
		await Assert.That(Serialize(markup)).IsEqualTo("""{"t":"x","p":[null,[{"k":"ansi","f":1,"bo":1}]],"r":[1,1]}""");
	}

	[Test]
	public async Task Write_ColourForms()
	{
		await Assert.That(Serialize(AnsiMarkup.Create(foreground: AnsiColor.Default.Instance))).Contains("\"f\":\"d\"");
		await Assert.That(Serialize(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, true)))).Contains("\"f\":9");
		await Assert.That(Serialize(AnsiMarkup.Create(foreground: new AnsiColor.Xterm(200)))).Contains("\"f\":200");
		await Assert.That(Serialize(AnsiMarkup.Create(foreground: new AnsiColor.Rgb(1, 2, 3)))).Contains("\"f\":\"#010203\"");
		await Assert.That(Serialize(AnsiMarkup.Create(background: new AnsiColor.Standard(2, false)))).Contains("\"g\":2");
	}

	[Test]
	public async Task Write_UnsetColours_AreOmitted()
	{
		await Assert.That(Serialize(AnsiMarkup.Create(bold: true))).DoesNotContain("\"f\"");
		await Assert.That(Serialize(AnsiMarkup.Create(bold: true))).DoesNotContain("\"g\"");
	}

	[Test]
	public async Task Write_UrlLinkKind_IsOmittedBecauseItIsTheDefault()
	{
		var json = Serialize(AnsiMarkup.Create(linkUrl: "http://x"));
		await Assert.That(json).Contains("\"lu\":\"http://x\"");
		await Assert.That(json).DoesNotContain("\"lk\"");
	}

	[Test]
	public async Task Write_CommandLink_CarriesTheKind()
	{
		var json = Serialize(AnsiMarkup.Create(linkUrl: "look", linkText: "h", linkKind: LinkKind.Command));
		await Assert.That(json).Contains("\"lt\":\"h\"");
		await Assert.That(json).Contains("\"lu\":\"look\"");
		await Assert.That(json).Contains("\"lk\":1");
	}

	[Test]
	public async Task RoundTrip_EveryColourCase()
	{
		await Assert.That(RoundTrip(AnsiMarkup.Create(foreground: AnsiColor.Default.Instance)).Foreground)
			.IsEqualTo(AnsiColor.Default.Instance);
		await Assert.That(RoundTrip(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false))).Foreground)
			.IsEqualTo(new AnsiColor.Standard(1, false));
		await Assert.That(RoundTrip(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, true))).Foreground)
			.IsEqualTo(new AnsiColor.Standard(1, true));
		await Assert.That(RoundTrip(AnsiMarkup.Create(foreground: new AnsiColor.Xterm(200))).Foreground)
			.IsEqualTo(new AnsiColor.Xterm(200));
		await Assert.That(RoundTrip(AnsiMarkup.Create(foreground: new AnsiColor.Rgb(1, 2, 3))).Foreground)
			.IsEqualTo(new AnsiColor.Rgb(1, 2, 3));
		await Assert.That(RoundTrip(AnsiMarkup.Create(background: new AnsiColor.Xterm(17))).Background)
			.IsEqualTo(new AnsiColor.Xterm(17));
		await Assert.That(RoundTrip(AnsiMarkup.Create()).Foreground).IsNull();
	}

	[Test]
	public async Task RoundTrip_EveryFlagAndLink()
	{
		var markup = AnsiMarkup.Create(
			foreground: new AnsiColor.Rgb(1, 2, 3),
			background: AnsiColor.Default.Instance,
			linkText: "hint",
			linkUrl: "look",
			linkKind: LinkKind.Command,
			blink: true, bold: true, clear: true, faint: true, inverted: true,
			italic: true, overlined: true, underlined: true, strikeThrough: true);

		var style = RoundTrip(markup);

		await Assert.That(style).IsEqualTo(markup.Style);
	}

	[Test]
	public async Task Read_LowIntegerIndexes_BecomeStandardColours()
	{
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"f\":3")).Foreground)
			.IsEqualTo(new AnsiColor.Standard(3, false));
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"f\":11")).Foreground)
			.IsEqualTo(new AnsiColor.Standard(3, true));
	}

	/// <summary>
	/// The consequence of <see cref="Read_LowIntegerIndexes_BecomeStandardColours"/> for a caller who
	/// built the colour as an xterm index: the wire has one integer for both spellings, so the first
	/// sixteen come back as standard colours. The colour is the same — both resolve to #ff55ff — but
	/// the ANSI bytes are not (<c>1;35</c> rather than <c>38;5;13</c>), which is why
	/// <c>RoundTripProperties</c> generates xterm indices from 16 up.
	/// </summary>
	[Test]
	public async Task RoundTrip_XtermIndexUnderSixteen_ComesBackAsTheStandardColour()
	{
		await Assert.That(RoundTrip(AnsiMarkup.Create(foreground: new AnsiColor.Xterm(13))).Foreground)
			.IsEqualTo(new AnsiColor.Standard(5, true));
		await Assert.That(new AnsiColor.Xterm(13).ToHex()).IsEqualTo(new AnsiColor.Standard(5, true).ToHex());
	}

	[Test]
	public async Task Read_LegacyRgbaString_DropsTheAlpha()
	{
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"f\":\"#aabbccdd\"")).Foreground)
			.IsEqualTo(new AnsiColor.Rgb(0xAA, 0xBB, 0xCC));
	}

	[Test]
	[Arguments("[30]", (byte)0, false)]
	[Arguments("[31]", (byte)1, false)]
	[Arguments("[37]", (byte)7, false)]
	[Arguments("[90]", (byte)0, true)]
	[Arguments("[97]", (byte)7, true)]
	[Arguments("[1,34]", (byte)4, true)]
	public async Task Read_LegacyForegroundByteArrays(string array, byte index, bool bright)
	{
		await Assert.That(Deserialize(Palette($"\"k\":\"ansi\",\"f\":{array}")).Foreground)
			.IsEqualTo(new AnsiColor.Standard(index, bright));
	}

	[Test]
	[Arguments("[40]", (byte)0, false)]
	[Arguments("[41]", (byte)1, false)]
	[Arguments("[47]", (byte)7, false)]
	[Arguments("[100]", (byte)0, true)]
	[Arguments("[107]", (byte)7, true)]
	[Arguments("[1,44]", (byte)4, true)]
	public async Task Read_LegacyBackgroundByteArrays(string array, byte index, bool bright)
	{
		await Assert.That(Deserialize(Palette($"\"k\":\"ansi\",\"g\":{array}")).Background)
			.IsEqualTo(new AnsiColor.Standard(index, bright));
	}

	[Test]
	public async Task Read_LegacyDefaultAndExtendedByteArrays()
	{
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"f\":[39]")).Foreground).IsEqualTo(AnsiColor.Default.Instance);
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"g\":[49]")).Background).IsEqualTo(AnsiColor.Default.Instance);
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"f\":[38,5,200]")).Foreground).IsEqualTo(new AnsiColor.Xterm(200));
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"g\":[48,5,17]")).Background).IsEqualTo(new AnsiColor.Xterm(17));
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"f\":[38,2,1,2,3]")).Foreground).IsEqualTo(new AnsiColor.Rgb(1, 2, 3));
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"g\":[48,2,4,5,6]")).Background).IsEqualTo(new AnsiColor.Rgb(4, 5, 6));
	}

	[Test]
	public async Task Read_UnrecognisedByteArray_LeavesTheColourUnset()
	{
		await Assert.That(Deserialize(Palette("\"k\":\"ansi\",\"f\":[123,4]")).Foreground).IsNull();
	}

	[Test]
	public async Task Read_PayloadWithoutAKind_IsTakenAsAnsi()
	{
		var style = Deserialize(Palette("\"lu\":\"help topic\""));
		await Assert.That(style.LinkUrl).IsEqualTo("help topic");
		await Assert.That(style.LinkKind).IsEqualTo(LinkKind.Url);
	}

	[Test]
	public async Task Read_LegacyFlagKeys_AreReadByPresence()
	{
		var style = Deserialize(Palette("\"k\":\"ansi\",\"bl\":1,\"bo\":1,\"cl\":1,\"fa\":1,\"in\":1,\"it\":1,\"ov\":1,\"un\":1,\"st\":1"));
		await Assert.That(style.Blink).IsTrue();
		await Assert.That(style.Bold).IsTrue();
		await Assert.That(style.Clear).IsTrue();
		await Assert.That(style.Faint).IsTrue();
		await Assert.That(style.Inverted).IsTrue();
		await Assert.That(style.Italic).IsTrue();
		await Assert.That(style.Overlined).IsTrue();
		await Assert.That(style.Underlined).IsTrue();
		await Assert.That(style.StrikeThrough).IsTrue();
	}

	[Test]
	public async Task Read_LegacyPayload_StillRenders()
	{
		var text = MarkupTextSerializer.Deserialize(Palette("\"f\":[1,31],\"lu\":\"look\",\"lk\":1"), Registry);
		await Assert.That(text.Render(MarkupFormat.Html, Registry))
			.IsEqualTo("<span style=\"color: #ff5555\"><a class=\"ms-cmd-link\" role=\"button\" tabindex=\"0\" xch_cmd=\"look\">x</a></span>");
	}
}
