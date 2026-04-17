using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MarkupString.MarkupImplementation;

namespace MarkupString;

/// <summary>
/// Custom interpolated string handler for <see cref="MString"/>.
///
/// Because <see cref="MString"/> is an F# class that cannot receive new C# constructors,
/// the handler is invoked via the <see cref="MStringInterpolation.Format"/> factory method:
///
/// <code>
/// MString bold  = MModule.markupSingle(Ansi.Create(bold: true), "world");
/// MString plain = MStringInterpolation.Format($"Hello, {bold}! Count: {42}.");
/// // → plain "Hello, " + bold "world" + plain "! Count: 42."
/// </code>
///
/// Each literal segment becomes a plain <c>MModule.single(...)</c> run.
/// Each formatted hole accepts either an <see cref="MString"/> (markup runs are preserved
/// intact) or any other type (converted via <c>.ToString()</c> to a plain run).
///
/// The result is assembled via <c>MModule.concatMany</c> — a single O(n) pass, avoiding
/// the O(n²) intermediate allocations of chained binary <c>MModule.concat</c> calls.
///
/// <para>
/// <b>Format specifiers for <see cref="MString"/> holes</b>
/// </para>
/// <para>
/// When an interpolation hole contains an <see cref="MString"/> value, an optional format
/// specifier (after the <c>:</c>) may request markup-aware post-processing:
/// </para>
/// <list type="table">
///   <listheader><term>Format</term><description>Effect</description></listheader>
///   <item><term><c>trim</c></term><description>Trim spaces from both sides.</description></item>
///   <item><term><c>trim:left</c> / <c>trim:start</c></term><description>Trim spaces from the start.</description></item>
///   <item><term><c>trim:right</c> / <c>trim:end</c></term><description>Trim spaces from the end.</description></item>
///   <item><term><c>trim:both</c></term><description>Trim spaces from both sides (explicit).</description></item>
///   <item><term><c>trim:left:CHARS</c></term><description>Trim the given characters from the start.</description></item>
///   <item><term><c>trim:right:CHARS</c> / <c>trim:end:CHARS</c></term><description>Trim the given characters from the end.</description></item>
///   <item><term><c>trim:both:CHARS</c></term><description>Trim the given characters from both sides.</description></item>
///   <item><term><c>align:left:N</c></term><description>Left-justify (pad right) to width <c>N</c> with spaces.</description></item>
///   <item><term><c>align:right:N</c></term><description>Right-justify (pad left) to width <c>N</c> with spaces.</description></item>
///   <item><term><c>align:center:N</c></term><description>Centre to width <c>N</c> with spaces.</description></item>
///   <item><term><c>align:full:N</c></term><description>Full-justify to width <c>N</c> with spaces.</description></item>
///   <item><term><c>align:left:N:CHAR</c></term><description>Left-justify with fill character <c>CHAR</c>.</description></item>
///   <item><term><c>color:CODES</c></term>
///     <description>Apply ANSI colour/attribute markup. <c>CODES</c> uses the same
///     syntax as the first argument of the <c>ansi()</c> MUSHCode function:
///     single-letter codes (<c>r</c>, <c>g</c>, <c>hr</c>, …), hex RGB
///     (<c>#rrggbb</c>), xterm numbers (<c>200</c> or <c>+xterm200</c>),
///     and RGB triplets (<c>&lt;r g b&gt;</c>).</description></item>
/// </list>
/// <para>
/// The C# alignment specifier (<c>$"{value,N}"</c>) is also honoured for
/// <see cref="MString"/> holes: positive <c>N</c> right-justifies; negative
/// <c>N</c> left-justifies.  It may be combined with a format specifier.
/// </para>
/// </summary>
[InterpolatedStringHandler]
public ref struct MarkupStringHandler
{
	// Segments collected during interpolation. Sized at construction time using
	// the compiler-provided literal and hole counts so no resizing occurs in
	// the common case.
	private readonly List<MString> _parts;

	/// <summary>
	/// Called by the compiler before any <c>AppendLiteral</c>/<c>AppendFormatted</c> calls.
	/// </summary>
	/// <param name="literalLength">
	///   Sum of the character lengths of all literal segments (informational hint; not used
	///   directly here since segments are stored as <c>MString</c> values, not raw chars).
	/// </param>
	/// <param name="formattedCount">Number of interpolation holes.</param>
	public MarkupStringHandler(int literalLength, int formattedCount)
	{
		// Upper bound: compiler produces at most (2 * formattedCount + 1) segments
		// in the pattern: lit? (hole lit?)*.
		_parts = new List<MString>(formattedCount * 2 + 1);
		_ = literalLength; // standard handler contract; not needed for MString segments
	}

	/// <summary>Appends a literal text segment as a plain (unformatted) run.</summary>
	public void AppendLiteral(string value)
	{
		if (value.Length > 0)
			Push(MModule.single(value));
	}

	/// <summary>
	/// Appends an <see cref="MString"/> hole, preserving all markup runs.
	/// </summary>
	public void AppendFormatted(MString value)
		=> Push(value);

	/// <summary>
	/// Appends an <see cref="MString"/> hole with an optional format specifier.
	/// <para>Supported specifiers: <c>trim[:{left|right|both}[:{chars}]]</c>,
	/// <c>align:{left|right|center|full}:{width}[:{fill}]</c>,
	/// <c>color:{codes}</c>.</para>
	/// </summary>
	public void AppendFormatted(MString value, string? format)
		=> Push(ApplyMStringFormat(value, format, 0));

	/// <summary>
	/// Appends an <see cref="MString"/> hole with alignment.
	/// Positive <paramref name="alignment"/> right-justifies; negative left-justifies.
	/// An optional format specifier is applied before padding.
	/// </summary>
	public void AppendFormatted(MString value, int alignment, string? format = null)
		=> Push(ApplyMStringFormat(value, format, alignment));

	/// <summary>
	/// Appends a <see langword="string"/> hole as a plain run.
	/// </summary>
	public void AppendFormatted(string? value)
	{
		if (!string.IsNullOrEmpty(value))
			Push(MModule.single(value));
	}

	/// <summary>
	/// Appends any other value by calling <c>ToString()</c> on it, producing a plain run.
	/// Covers <c>int</c>, <c>double</c>, <c>bool</c>, custom types, etc.
	/// </summary>
	public void AppendFormatted<T>(T value)
	{
		var s = value?.ToString();
		if (!string.IsNullOrEmpty(s))
			Push(MModule.single(s));
	}

	/// <summary>
	/// Appends a formattable value using the provided format specifier, producing a plain run.
	/// </summary>
	public void AppendFormatted<T>(T value, string? format) where T : IFormattable
	{
		var s = value?.ToString(format, null);
		if (!string.IsNullOrEmpty(s))
			Push(MModule.single(s));
	}

	/// <summary>
	/// Appends a value with alignment. Alignment is applied to plain-text values
	/// by padding the resulting string.  For <see cref="MString"/> values, use the
	/// specific <see cref="AppendFormatted(MString,int,string?)"/> overload.
	/// </summary>
	public void AppendFormatted<T>(T value, int alignment, string? format = null)
	{
		// Route through the IFormattable overload when possible for consistency.
		if (value is IFormattable formattable)
		{
			var s = formattable.ToString(format, null);
			if (!string.IsNullOrEmpty(s))
				Push(MModule.single(s));
		}
		else
		{
			var s = value?.ToString();
			if (!string.IsNullOrEmpty(s))
				Push(MModule.single(s));
		}
	}

	/// <summary>
	/// Materialises the accumulated segments into a single <see cref="MString"/>
	/// via <c>MModule.concatMany</c>.
	/// </summary>
	public MString ToMarkupString()
		=> MModule.concatMany(_parts);

	private void Push(MString value)
	{
		_parts.Add(value);
	}

	// ── Format application ────────────────────────────────────────────────────

	/// <summary>
	/// Applies the format specifier and/or C# alignment to an <see cref="MString"/> value.
	/// </summary>
	private static MString ApplyMStringFormat(MString value, string? format, int alignment)
	{
		MString result = string.IsNullOrEmpty(format) ? value : ParseAndApplyFormat(value, format);

		if (alignment != 0)
		{
			int width = Math.Abs(alignment);
			// Positive = right-justify (pad left); negative = left-justify (pad right).
			PadType padType = alignment > 0 ? PadType.Left : PadType.Right;
			result = MModule.Pad(result, MModule.single(" "), width, padType, TruncationType.Truncate);
		}

		return result;
	}

	/// <summary>Dispatches a format string to the appropriate transform.</summary>
	private static MString ParseAndApplyFormat(MString value, string format)
	{
		// Identify the operation: everything before the first ':'.
		int sep = format.IndexOf(':');
		string op = sep < 0 ? format : format[..sep];
		string rest = sep < 0 ? string.Empty : format[(sep + 1)..];

		return op.ToLowerInvariant() switch
		{
			"trim"  => ApplyTrim(value, rest),
			"align" => ApplyAlign(value, rest),
			"color" => ApplyColor(value, rest),
			_       => value, // unknown specifiers are silently ignored
		};
	}

	// ── Trim ──────────────────────────────────────────────────────────────────

	/// <summary>
	/// Applies a trim operation.
	/// <para>
	/// <paramref name="args"/> may be empty (<c>→ TrimBoth, spaces</c>),
	/// a direction keyword (<c>left/start/right/end/both</c>),
	/// or a direction followed by a colon and the characters to trim.
	/// </para>
	/// </summary>
	private static MString ApplyTrim(MString value, string args)
	{
		if (string.IsNullOrEmpty(args))
			return MModule.Trim(value, " ", TrimType.TrimBoth);

		int sep = args.IndexOf(':');
		string dir = sep < 0 ? args : args[..sep];
		string chars = sep < 0 ? " " : args[(sep + 1)..];
		if (chars.Length == 0) chars = " ";

		TrimType trimType = dir.ToLowerInvariant() switch
		{
			"left"  or "start" => TrimType.TrimStart,
			"right" or "end"   => TrimType.TrimEnd,
			_                  => TrimType.TrimBoth, // includes "both" and empty
		};

		return MModule.Trim(value, chars, trimType);
	}

	// ── Align ─────────────────────────────────────────────────────────────────

	/// <summary>
	/// Applies an alignment (pad) operation.
	/// <para>
	/// <paramref name="args"/> format: <c>{direction}:{width}[:{fill}]</c>.
	/// Direction is <c>left/right/center/full</c>; width is a positive integer;
	/// fill is an optional single-character pad string (defaults to space).
	/// </para>
	/// </summary>
	private static MString ApplyAlign(MString value, string args)
	{
		if (string.IsNullOrEmpty(args)) return value;

		var parts = args.Split(':', 3);
		if (parts.Length < 2) return value;

		string dir = parts[0].ToLowerInvariant();
		if (!int.TryParse(parts[1], out int width) || width <= 0) return value;

		string fillStr = parts.Length >= 3 && parts[2].Length > 0 ? parts[2][..1] : " ";
		MString fill = MModule.single(fillStr);

		PadType padType = dir switch
		{
			"left"   => PadType.Right,
			"right"  => PadType.Left,
			"center" => PadType.Center,
			"full"   => PadType.Full,
			_        => PadType.Right, // default to left-justify
		};

		return MModule.Pad(value, fill, width, padType, TruncationType.Truncate);
	}

	// ── Color ─────────────────────────────────────────────────────────────────

	/// <summary>
	/// Wraps <paramref name="value"/> in an ANSI colour/attribute markup run.
	/// <paramref name="codes"/> uses the same syntax as the <c>ansi()</c>
	/// MUSHCode function's first argument.
	/// </summary>
	private static MString ApplyColor(MString value, string codes)
	{
		if (string.IsNullOrEmpty(codes)) return value;
		var markup = AnsiCodeParser.ParseCodes(codes);
		return MModule.MarkupSingle2(markup, value);
	}
}

/// <summary>
/// Static factory for building <see cref="MString"/> values from interpolated strings.
///
/// <para>
/// Because <see cref="MString"/> is an immutable F# class, it cannot expose the constructor
/// required to make <c>MString r = $"..."</c> route through <see cref="MarkupStringHandler"/>
/// directly. Instead, pass the interpolated string to <see cref="Format"/>:
/// </para>
///
/// <code>
/// MString bold   = MModule.markupSingle(Ansi.Create(bold: true), "world");
/// MString result = MStringInterpolation.Format($"Hello, {bold}! Count: {42}.");
/// </code>
///
/// Shorthand via a <c>using static</c> import:
/// <code>
/// using static MarkupString.MStringInterpolation;
///
/// MString result = Format($"Hello, {bold}! Count: {42}.");
/// </code>
/// </summary>
public static class MStringInterpolation
{
    /// <summary>
    /// Builds a markup-preserving <see cref="MString"/> from an interpolated string.
    /// The compiler routes the <c>$"..."</c> expression through
    /// <see cref="MarkupStringHandler"/>, calling <c>AppendLiteral</c> /
    /// <c>AppendFormatted</c> for each segment before passing the completed handler here.
    /// </summary>
    public static MString Format(MarkupStringHandler handler)
        => handler.ToMarkupString();
}
