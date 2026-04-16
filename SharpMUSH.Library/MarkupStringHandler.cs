using System.Runtime.CompilerServices;

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
/// </summary>
[InterpolatedStringHandler]
public ref struct MarkupStringHandler
{
    // Segments collected during interpolation. Sized at construction time using
    // the compiler-provided literal and hole counts so no resizing occurs in
    // the common case.
    private MString[] _parts;
    private int _count;

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
        _parts = new MString[formattedCount * 2 + 1];
        _count = 0;
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
    /// Appends a value with alignment. Alignment is intentionally ignored because
    /// <see cref="MString"/> has its own padding API (<c>MModule.pad</c>).
    /// The value is formatted normally using the optional format specifier.
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
        => MModule.concatMany(new ArraySegment<MString>(_parts, 0, _count));

    private void Push(MString value)
    {
        // Defensive growth in case the compiler-provided count was an underestimate.
        if (_count >= _parts.Length)
        {
            var grown = new MString[_parts.Length * 2];
            _parts.CopyTo(grown, 0);
            _parts = grown;
        }
        _parts[_count++] = value;
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
