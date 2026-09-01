using ANSILibrary;
using SharpMUSH.Library.Models;
using System.Drawing;
using Ansi = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Maps <see cref="SemanticTokenType"/> values to ANSI colours for terminal
/// rendering of <c>sharp</c> fenced code blocks.
/// The palette mirrors common dark-terminal conventions (aligned with VS Code dark+).
/// </summary>
public static class SemanticTokenAnsiPalette
{
	/// <summary>
	/// Returns an ANSI style for the given token type and modifiers, or <c>null</c>
	/// if no colour override should be applied (i.e. the token should use the
	/// terminal's default foreground colour).
	/// </summary>
	public static Ansi? GetStyle(SemanticTokenType tokenType, SemanticTokenModifier modifiers)
	{
		var color = tokenType switch
		{
			SemanticTokenType.Function => Color.FromArgb(0xDC, 0xDC, 0xAA),          // #DCDCAA yellow
			SemanticTokenType.UserFunction => Color.FromArgb(0x4E, 0xC9, 0xB0),      // #4EC9B0 teal
			SemanticTokenType.ObjectReference => Color.FromArgb(0x9C, 0xDC, 0xFE),   // #9CDCFE light blue
			SemanticTokenType.Substitution => Color.FromArgb(0x4F, 0xC1, 0xFF),      // #4FC1FF bright blue
			SemanticTokenType.Register => Color.FromArgb(0x9C, 0xDC, 0xFE),          // #9CDCFE light blue
			SemanticTokenType.Command => Color.FromArgb(0xC5, 0x86, 0xC0),           // #C586C0 purple
			SemanticTokenType.BracketSubstitution => Color.FromArgb(0x56, 0x9C, 0xD6), // #569CD6 blue
			SemanticTokenType.BraceGroup => Color.FromArgb(0xFF, 0xD7, 0x00),        // gold
			SemanticTokenType.EscapeSequence => Color.FromArgb(0xD7, 0xBA, 0x7D),    // #D7BA7D orange
			SemanticTokenType.AnsiCode => Color.FromArgb(0x80, 0x80, 0x80),          // #808080 grey
			SemanticTokenType.Operator => Color.FromArgb(0xD4, 0xD4, 0xD4),          // #D4D4D4 light grey
			SemanticTokenType.Number => Color.FromArgb(0xB5, 0xCE, 0xA8),            // #B5CEA8 light green
			SemanticTokenType.String => Color.FromArgb(0xCE, 0x91, 0x78),            // #CE9178 orange-brown
			SemanticTokenType.Comment => Color.FromArgb(0x6A, 0x99, 0x55),           // #6A9955 green
			SemanticTokenType.Keyword => Color.FromArgb(0x56, 0x9C, 0xD6),           // #569CD6 blue
			_ => (Color?)null
		};

		if (color is null)
			return null;

		var bold = modifiers.HasFlag(SemanticTokenModifier.DefaultLibrary) &&
							 tokenType == SemanticTokenType.Function;

		return Ansi.Create(foreground: new AnsiColor.RGB(color.Value), bold: bold);
	}

	/// <summary>
	/// Colours for matched structural delimiters, indexed by nesting depth. Aligned with VS Code dark+'s
	/// <c>editorBracketHighlight.foreground1..3</c>, like the rest of this palette, and deliberately
	/// distinct from every entry in <see cref="GetStyle"/>: a bracket has to be told apart from the
	/// function name it follows, not just from the bracket one level out.
	/// </summary>
	private static readonly Color[] BracketDepthColors =
	[
		Color.FromArgb(0xFF, 0xD7, 0x00), // gold
		Color.FromArgb(0xDA, 0x70, 0xD6), // orchid
		Color.FromArgb(0x17, 0x9F, 0xFF)  // azure
	];

	/// <summary>
	/// Number of distinct depth colours before the cycle repeats. Exposed so a caller does not have to
	/// hard-code the cycle length to reason about (or test) what a deeply nested bracket is painted.
	/// </summary>
	public static int BracketDepthColorCount => BracketDepthColors.Length;

	/// <summary>
	/// The style for a structural delimiter at <paramref name="depth"/>, cycling every
	/// <see cref="BracketDepthColorCount"/> levels so arbitrarily deep nesting still gets a colour.
	/// <para>
	/// Never <c>null</c>, unlike <see cref="GetStyle"/>: a delimiter reported by
	/// <c>SoftcodeLayout.ComputeDelimiterDepths</c> is known structure, so there is no "leave it at the
	/// terminal default" case to express. Bold, because at 78 columns a single character carrying the
	/// only cue to nesting needs the weight.
	/// </para>
	/// </summary>
	/// <param name="depth">Nesting depth, 0 for an outermost group. Negative values are treated as 0.</param>
	public static Ansi GetBracketDepthStyle(int depth) =>
		Ansi.Create(
			foreground: new AnsiColor.RGB(BracketDepthColors[Math.Max(depth, 0) % BracketDepthColors.Length]),
			bold: true);
}
