using MarkupString.Ansi;

namespace SharpMUSH.Client.Services;

/// <summary>
/// One thing the format menu does to a selection. Only what <c>ansi()</c> can write is here: italic, faint,
/// strike-through and overline have no code, so offering them would show what the game never receives.
/// </summary>
public abstract record ComposerFormat
{
	/// <summary>The style with this format turned on (or, for a toggle, off).</summary>
	public abstract AnsiStyle ApplyTo(AnsiStyle style, bool on);

	/// <summary>Whether <paramref name="style"/> already shows this format; a selection that all does turns it off.</summary>
	public abstract bool IsSetIn(AnsiStyle style);

	/// <summary>The <c>ansi()</c> codes for this format, for wrapping softcode.</summary>
	public abstract string Codes { get; }

	/// <summary>A text colour; null takes the colour away.</summary>
	public sealed record Foreground(AnsiColor? Color) : ComposerFormat
	{
		public override AnsiStyle ApplyTo(AnsiStyle style, bool on) => style with { Foreground = on ? Color : null };
		public override bool IsSetIn(AnsiStyle style) => Equals(style.Foreground, Color);
		public override string Codes => AnsiCodeWriter.Write(new AnsiStyle { Foreground = Color ?? AnsiColor.Default.Instance });
	}

	/// <summary>A background colour; null takes it away.</summary>
	public sealed record Background(AnsiColor? Color) : ComposerFormat
	{
		public override AnsiStyle ApplyTo(AnsiStyle style, bool on) => style with { Background = on ? Color : null };
		public override bool IsSetIn(AnsiStyle style) => Equals(style.Background, Color);
		public override string Codes => AnsiCodeWriter.Write(new AnsiStyle { Background = Color ?? AnsiColor.Default.Instance });
	}

	/// <summary>Hilite: <c>h</c>.</summary>
	public sealed record Bold : ComposerFormat
	{
		public override AnsiStyle ApplyTo(AnsiStyle style, bool on) => style with { Bold = on };
		public override bool IsSetIn(AnsiStyle style) => style.Bold || style.Foreground is AnsiColor.Standard { Bright: true };
		public override string Codes => "h";
	}

	/// <summary><c>u</c>.</summary>
	public sealed record Underline : ComposerFormat
	{
		public override AnsiStyle ApplyTo(AnsiStyle style, bool on) => style with { Underlined = on };
		public override bool IsSetIn(AnsiStyle style) => style.Underlined;
		public override string Codes => "u";
	}

	/// <summary>Inverse: <c>i</c>.</summary>
	public sealed record Inverse : ComposerFormat
	{
		public override AnsiStyle ApplyTo(AnsiStyle style, bool on) => style with { Inverted = on };
		public override bool IsSetIn(AnsiStyle style) => style.Inverted;
		public override string Codes => "i";
	}

	/// <summary>Flash: <c>f</c>.</summary>
	public sealed record Blink : ComposerFormat
	{
		public override AnsiStyle ApplyTo(AnsiStyle style, bool on) => style with { Blink = on };
		public override bool IsSetIn(AnsiStyle style) => style.Blink;
		public override string Codes => "f";
	}

	/// <summary>Takes every colour and attribute away; links and tags stay.</summary>
	public sealed record ClearFormatting : ComposerFormat
	{
		public override AnsiStyle ApplyTo(AnsiStyle style, bool on) => AnsiStyle.None with
		{
			LinkUrl = style.LinkUrl,
			LinkText = style.LinkText,
			LinkKind = style.LinkKind
		};
		public override bool IsSetIn(AnsiStyle style) => false;
		public override string Codes => "n";
	}

	/// <summary>The sixteen palette colours, dark then bright, as the menu lays them out.</summary>
	public static IReadOnlyList<AnsiColor.Standard> Palette { get; } =
		[.. Enumerable.Range(0, 16).Select(i => new AnsiColor.Standard((byte)(i % 8), i >= 8))];
}
