using System.Buffers;
using MarkupString.Ansi;

public class SgrWriterTests
{
	private const string Esc = "\u001b";

	private static string Transition(AnsiStyle from, AnsiStyle to)
	{
		var writer = new ArrayBufferWriter<char>(64);
		SgrWriter.Transition(from, to, writer);
		return new string(writer.WrittenSpan);
	}

	private static bool TransitionWrote(AnsiStyle from, AnsiStyle to)
	{
		var writer = new ArrayBufferWriter<char>(64);
		return SgrWriter.Transition(from, to, writer);
	}

	private static string Codes(AnsiColor color, bool background)
	{
		Span<char> buffer = stackalloc char[64];
		var codes = new SgrBuilder(buffer);
		SgrWriter.WriteColorCodes(color, background, ref codes);
		return new string(codes.Written);
	}

	[Test]
	public async Task Transition_AttributeAndColour_WritesOneSequence()
	{
		var to = new AnsiStyle { Bold = true, Foreground = new AnsiColor.Standard(1, false) };
		await Assert.That(Transition(AnsiStyle.None, to)).IsEqualTo($"{Esc}[1;31m");
	}

	[Test]
	public async Task Transition_AddedAttribute_WritesOnlyTheAddition()
	{
		var from = new AnsiStyle { Bold = true };
		var to = new AnsiStyle { Bold = true, Underlined = true };
		await Assert.That(Transition(from, to)).IsEqualTo($"{Esc}[4m");
	}

	[Test]
	public async Task Transition_RemovedAttribute_ResetsThenRewritesTheWholeStyle()
	{
		var from = new AnsiStyle { Bold = true, Underlined = true };
		var to = new AnsiStyle { Bold = true };
		await Assert.That(Transition(from, to)).IsEqualTo($"{Esc}[0m{Esc}[1m");
	}

	[Test]
	public async Task Transition_UnchangedStyle_WritesNothing()
	{
		var style = new AnsiStyle { Foreground = new AnsiColor.Xterm(200) };
		await Assert.That(Transition(style, style)).IsEqualTo(string.Empty);
		await Assert.That(TransitionWrote(style, style)).IsFalse();
	}

	[Test]
	public async Task Transition_RgbForegroundAndDefaultBackground_WritesBothInOrder()
	{
		var to = new AnsiStyle { Foreground = new AnsiColor.Rgb(1, 2, 3), Background = AnsiColor.Default.Instance };
		await Assert.That(Transition(AnsiStyle.None, to)).IsEqualTo($"{Esc}[38;2;1;2;3;49m");
	}

	[Test]
	public async Task Transition_ClearInTarget_ResetsFirstThenWritesTheRest()
	{
		var to = new AnsiStyle { Clear = true, Bold = true };
		await Assert.That(Transition(AnsiStyle.None, to)).IsEqualTo($"{Esc}[0m{Esc}[1m");
	}

	[Test]
	public async Task Transition_ClearAlone_WritesOnlyTheReset()
	{
		await Assert.That(Transition(AnsiStyle.None, new AnsiStyle { Clear = true })).IsEqualTo($"{Esc}[0m");
	}

	[Test]
	public async Task Transition_BrightForeground_EmitsBoldAndTheBaseColour()
	{
		var to = new AnsiStyle { Foreground = new AnsiColor.Standard(1, true) };
		await Assert.That(Transition(AnsiStyle.None, to)).IsEqualTo($"{Esc}[1;31m");
	}

	[Test]
	public async Task Transition_BrightForegroundWithBold_EmitsBoldOnce()
	{
		var to = new AnsiStyle { Bold = true, Foreground = new AnsiColor.Standard(1, true) };
		await Assert.That(Transition(AnsiStyle.None, to)).IsEqualTo($"{Esc}[1;31m");
	}

	[Test]
	public async Task Transition_BrightForegroundAlreadyBold_DoesNotRepeatBold()
	{
		var from = new AnsiStyle { Bold = true };
		var to = new AnsiStyle { Bold = true, Foreground = new AnsiColor.Standard(4, true) };
		await Assert.That(Transition(from, to)).IsEqualTo($"{Esc}[34m");
	}

	[Test]
	public async Task Transition_BrightForegroundToPlain_ResetsBecauseBoldGoesAway()
	{
		var from = new AnsiStyle { Foreground = new AnsiColor.Standard(1, true) };
		var to = new AnsiStyle { Foreground = new AnsiColor.Standard(1, false) };
		await Assert.That(Transition(from, to)).IsEqualTo($"{Esc}[0m{Esc}[31m");
	}

	[Test]
	public async Task Transition_ColourRemoved_Resets()
	{
		var from = new AnsiStyle { Foreground = new AnsiColor.Standard(1, false) };
		await Assert.That(Transition(from, AnsiStyle.None)).IsEqualTo($"{Esc}[0m");
		await Assert.That(TransitionWrote(from, AnsiStyle.None)).IsTrue();
	}

	[Test]
	public async Task Transition_XtermForegroundAndRgbBackground()
	{
		var to = new AnsiStyle { Foreground = new AnsiColor.Xterm(200), Background = new AnsiColor.Rgb(1, 2, 3) };
		await Assert.That(Transition(AnsiStyle.None, to)).IsEqualTo($"{Esc}[38;5;200;48;2;1;2;3m");
	}

	[Test]
	public async Task Transition_BrightBackground_UsesTheHundredRow()
	{
		var to = new AnsiStyle { Background = new AnsiColor.Standard(2, true) };
		await Assert.That(Transition(AnsiStyle.None, to)).IsEqualTo($"{Esc}[102m");
	}

	[Test]
	public async Task Transition_EveryAttribute_WritesThemInSgrOrder()
	{
		var to = new AnsiStyle
		{
			Bold = true,
			Faint = true,
			Italic = true,
			Underlined = true,
			Overlined = true,
			Blink = true,
			Inverted = true,
			StrikeThrough = true
		};
		await Assert.That(Transition(AnsiStyle.None, to)).IsEqualTo($"{Esc}[1;2;3;4;53;5;7;9m");
	}

	[Test]
	public async Task Transition_LinkOnly_WritesNothing()
	{
		var to = new AnsiStyle { LinkUrl = "http://x" };
		await Assert.That(Transition(AnsiStyle.None, to)).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Reset_WritesTheZeroSequence()
	{
		var writer = new ArrayBufferWriter<char>(8);
		SgrWriter.Reset(writer);
		await Assert.That(new string(writer.WrittenSpan)).IsEqualTo($"{Esc}[0m");
	}

	[Test]
	public async Task WriteColorCodes_CoversEveryColourCase()
	{
		await Assert.That(Codes(new AnsiColor.Standard(1, false), background: false)).IsEqualTo("31");
		await Assert.That(Codes(new AnsiColor.Standard(1, true), background: false)).IsEqualTo("1;31");
		await Assert.That(Codes(new AnsiColor.Standard(1, false), background: true)).IsEqualTo("41");
		await Assert.That(Codes(new AnsiColor.Standard(1, true), background: true)).IsEqualTo("101");
		await Assert.That(Codes(AnsiColor.Default.Instance, background: false)).IsEqualTo("39");
		await Assert.That(Codes(AnsiColor.Default.Instance, background: true)).IsEqualTo("49");
		await Assert.That(Codes(new AnsiColor.Xterm(200), background: false)).IsEqualTo("38;5;200");
		await Assert.That(Codes(new AnsiColor.Xterm(200), background: true)).IsEqualTo("48;5;200");
		await Assert.That(Codes(new AnsiColor.Rgb(1, 2, 3), background: false)).IsEqualTo("38;2;1;2;3");
		await Assert.That(Codes(new AnsiColor.Rgb(1, 2, 3), background: true)).IsEqualTo("48;2;1;2;3");
	}
}
