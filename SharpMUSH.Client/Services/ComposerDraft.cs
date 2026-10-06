using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using MarkupString;
using MarkupString.Ansi;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Client.Services;

/// <summary>
/// What a formatted input holds: the draft as styled text, edited the way the field reports it, and
/// written out as the softcode that makes it.
/// </summary>
/// <remarks>
/// <para>
/// In rich mode the draft is a <see cref="MarkupText"/> and what is shown is what is sent: the text is
/// literal, and <see cref="SoftcodeDecomposer.Decompose"/> writes it (colours as <c>ansi()</c>, everything
/// the parser would act on escaped). In raw mode the draft is the softcode itself, typed as it goes out,
/// and formatting is written into it as <c>[ansi(codes,…)]</c> text.
/// </para>
/// <para>
/// The field is a textarea and reports only its new value; <see cref="ApplyInput"/> works out what changed
/// from that and the caret, so typing, paste, cut, autocorrect and IME commits all take one path. Formatting
/// is only what <c>ansi()</c> can write, so nothing shown is lost on the way out.
/// </para>
/// </remarks>
public sealed partial class ComposerDraft
{
	private const int UndoLimit = 100;

	private readonly List<Snapshot> _undo = [];
	private readonly List<Snapshot> _redo = [];
	private bool _lastWasTyping;

	/// <summary>The draft. In raw mode it carries no markup: its text is the softcode.</summary>
	public MarkupText Text { get; private set; } = MarkupText.Empty;

	/// <summary>Whether the draft is softcode typed as it goes out, rather than styled text.</summary>
	public bool Raw { get; private set; }

	/// <summary>What the field shows, and what the textarea holds.</summary>
	public string Value => Text.Text;

	/// <summary>The style chosen with nothing selected, for what is typed next at <see cref="PendingAt"/>.</summary>
	public AnsiStyle? Pending { get; private set; }

	private int PendingAt { get; set; } = -1;

	/// <summary>Raised after every change, so the field re-renders and the owner can keep the draft.</summary>
	public event Action? Changed;

	public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

	public bool CanUndo => _undo.Count > 0;

	public bool CanRedo => _redo.Count > 0;

	/// <summary>Replaces the draft with <paramref name="text"/>, in the given mode, forgetting the undo history.</summary>
	public void Load(MarkupText text, bool raw)
	{
		Raw = raw;
		Text = raw ? MarkupText.Plain(text.Text) : text;
		Pending = null;
		_undo.Clear();
		_redo.Clear();
		_lastWasTyping = false;
		Changed?.Invoke();
	}

	/// <summary>Replaces the draft with plain <paramref name="text"/>, keeping the mode.</summary>
	public void Load(string text) => Load(MarkupText.Plain(text), Raw);

	/// <summary>Empties the draft, keeping the mode.</summary>
	public void Clear() => Load(MarkupText.Empty, Raw);

	/// <summary>
	/// Switches between rich and raw. Rich to raw writes each styled stretch as the softcode that makes it and
	/// leaves the rest as typed, so softcode the player typed by hand evaluates when sent. Raw to rich takes the
	/// softcode as literal text. Undoable.
	/// </summary>
	public void SetRaw(bool raw)
	{
		if (raw == Raw) return;
		Remember(typing: false);
		Text = raw ? MarkupText.Plain(StyledAsSoftcode(Text)) : MarkupText.Plain(Text.Text);
		Raw = raw;
		Pending = null;
		Changed?.Invoke();
	}

	/// <summary>
	/// The field now holds <paramref name="value"/>, with the caret at <paramref name="caret"/> (null when it
	/// is not known). The change is the stretch between the longest common start and end the caret allows; what
	/// was inserted takes the pending style when it lands where that was chosen, else the style of the character
	/// before it.
	/// </summary>
	public void ApplyInput(string value, int? caret)
	{
		value = value.Replace("\r\n", "\n").Replace('\r', '\n');
		var old = Text.Text;
		if (value == old) return;

		var suffix = 0;
		var maxSuffix = Math.Min(old.Length, value.Length);
		if (caret is { } c) maxSuffix = Math.Min(maxSuffix, Math.Max(0, value.Length - c));
		while (suffix < maxSuffix && old[old.Length - 1 - suffix] == value[value.Length - 1 - suffix]) suffix++;

		var prefix = 0;
		var maxPrefix = Math.Min(old.Length, value.Length) - suffix;
		while (prefix < maxPrefix && old[prefix] == value[prefix]) prefix++;

		var removed = old.Length - suffix - prefix;
		var inserted = value.Substring(prefix, value.Length - suffix - prefix);

		Remember(typing: removed <= 1 && inserted.Length <= 1 && !inserted.Any(char.IsWhiteSpace));

		var replacement = inserted.Length == 0 ? MarkupText.Empty : Raw ? MarkupText.Plain(inserted) : Styled(inserted, prefix);
		Text = Text.Replace(prefix, removed, replacement);
		Pending = null;
		Changed?.Invoke();
	}

	/// <summary>
	/// Applies <paramref name="format"/> to <c>[start, end)</c>. With nothing selected in rich mode it becomes the
	/// style for what is typed next there; in raw mode the selection, empty or not, is wrapped in
	/// <c>[ansi(codes,…)]</c>.
	/// </summary>
	/// <returns>The selection to put back: the same text, now formatted (in raw mode, inside the call).</returns>
	public (int Start, int End) Apply(ComposerFormat format, int start, int end)
	{
		(start, end) = Clamp(start, end);
		if (Raw) return WrapAsSoftcode(format, start, end);

		if (start == end)
		{
			var current = Pending is { } pending && PendingAt == start ? pending : StyleAt(start);
			Pending = format.ApplyTo(current, on: !format.IsSetIn(current));
			PendingAt = start;
			Changed?.Invoke();
			return (start, end);
		}

		Remember(typing: false);
		var on = !AllHave(format, start, end);
		Text = Restyle(start, end, style => format.ApplyTo(style, on));
		Changed?.Invoke();
		return (start, end);
	}

	/// <summary>The style the character before <paramref name="offset"/> shows (the first one at 0), as the toolbar reports it.</summary>
	public AnsiStyle StyleAt(int offset)
	{
		if (Raw || Text.Length == 0) return AnsiStyle.None;
		var index = Math.Clamp(offset - 1, 0, Text.Length - 1);
		return Flat(SetAt(index) is { } set ? Effective(set) : AnsiStyle.None);
	}

	/// <summary>The style for the toolbar to show at a caret or over a selection: the pending one when it applies.</summary>
	public AnsiStyle StyleFor(int start, int end) =>
		start == end && Pending is { } pending && PendingAt == start ? pending : start == end ? StyleAt(start) : StyleAt(start + 1);

	public bool Undo() => Step(_undo, _redo);

	public bool Redo() => Step(_redo, _undo);

	/// <summary>
	/// The draft as what goes on the wire, after a verb: in rich mode <c>decompose()</c>'s answer, in raw mode
	/// the softcode with only its whitespace and command separators protected
	/// (<see cref="MushComposeEncoder"/>). <paramref name="splitLines"/> sends each non-blank line on its own.
	/// </summary>
	public IReadOnlyList<string> ToSoftcode(bool splitLines = false)
	{
		var parts = splitLines
			? Text.Split("\n").Where(line => !string.IsNullOrWhiteSpace(line.Text))
			: [Text];
		return parts.Select(part => Raw ? MushComposeEncoder.Encode(part.Text) : SoftcodeDecomposer.Decompose(part)).ToList();
	}

	/// <summary>The softcode in a rich draft that the player probably meant to evaluate, or null.</summary>
	/// <remarks>
	/// Naive on purpose: a function call (<c>[name(</c>) or a substitution a player types by hand (<c>%r</c>,
	/// <c>%b</c>, <c>%t</c>, <c>%;</c>, <c>%#</c>, <c>%n</c>, <c>%0</c>-<c>%9</c>, <c>%q</c>/<c>%v</c>
	/// registers). It only decides whether the field asks; nothing is changed on its strength.
	/// </remarks>
	public string? SoftcodeLookalike => Raw ? null : SoftcodePattern().Match(Text.Text) is { Success: true } m ? m.Value : null;

	[GeneratedRegex(@"\[\s*[A-Za-z_][\w`.]*\(|%(?:[rRbBtT;#nN0-9]|[qQvV][A-Za-z0-9])")]
	private static partial Regex SoftcodePattern();

	private (int, int) Clamp(int start, int end)
	{
		start = Math.Clamp(start, 0, Text.Length);
		end = Math.Clamp(end, 0, Text.Length);
		return start <= end ? (start, end) : (end, start);
	}

	private (int Start, int End) WrapAsSoftcode(ComposerFormat format, int start, int end)
	{
		if (format.Codes is not { Length: > 0 } codes) return (start, end);
		Remember(typing: false);
		var open = $"[ansi({codes},";
		var text = Text.Text;
		Text = MarkupText.Plain(text[..start] + open + text[start..end] + ")]" + text[end..]);
		Changed?.Invoke();
		return (start + open.Length, end + open.Length);
	}

	/// <summary>Inserted text, styled like the text it joins (or as the pending style says).</summary>
	private MarkupText Styled(string inserted, int at)
	{
		if (Pending is { } pending && PendingAt == at)
		{
			return pending.IsNone ? MarkupText.Plain(inserted) : MarkupText.Wrap(new AnsiMarkup(pending), inserted);
		}
		if (at == 0) return MarkupText.Plain(inserted);
		return SetAt(at - 1) is { } set ? MarkupText.Wrap(set, inserted) : MarkupText.Plain(inserted);
	}

	private MarkupSet? SetAt(int index)
	{
		foreach (var run in Text.Runs)
		{
			if (run.Start <= index && index < run.End) return run.Markups;
		}
		return null;
	}

	/// <summary>What the ANSI layers of <paramref name="set"/> come to, innermost winning.</summary>
	private static AnsiStyle Effective(MarkupSet? set)
	{
		var style = AnsiStyle.None;
		if (set is null) return style;
		// The set lists the innermost first; fold from the outside in.
		for (var i = set.Count - 1; i >= 0; i--)
		{
			if (set[i] is AnsiMarkup ansi) style = style.Combine(ansi.Style);
		}
		return style;
	}

	/// <summary>An effective style as one layer standing on its own: nothing outside it left to clear or turn off.</summary>
	private static AnsiStyle Flat(AnsiStyle style) =>
		style with { Clear = false, BlinkOff = false, BoldOff = false, InvertedOff = false, UnderlinedOff = false };

	private bool AllHave(ComposerFormat format, int start, int end)
	{
		var position = start;
		while (position < end)
		{
			if (!format.IsSetIn(Effective(SetAt(position)))) return false;
			position = NextBoundary(position, end);
		}
		return true;
	}

	private int NextBoundary(int position, int end)
	{
		foreach (var run in Text.Runs)
		{
			if (run.Start > position) return Math.Min(run.Start, end);
			if (run.End > position) return Math.Min(run.End, end);
		}
		return end;
	}

	/// <summary>
	/// <c>[start, end)</c> rebuilt stretch by stretch: each one's ANSI layers become one layer holding
	/// <paramref name="change"/> of their effective style, innermost, and its other markup (links, tags) stays
	/// outside it. A plain <see cref="MarkupText.Wrap(IMarkup, MarkupText)"/> would add the new colour as the
	/// outermost layer, where any colour already there wins over it.
	/// </summary>
	private MarkupText Restyle(int start, int end, Func<AnsiStyle, AnsiStyle> change)
	{
		var parts = ImmutableArray.CreateBuilder<MarkupText>();
		var position = start;
		while (position < end)
		{
			var next = NextBoundary(position, end);
			var set = SetAt(position);
			var style = Flat(change(Flat(Effective(set))));
			var layers = new List<IMarkup>();
			if (!style.IsNone) layers.Add(new AnsiMarkup(style));
			if (set is not null) layers.AddRange(set.Where(m => m is not AnsiMarkup));
			var segment = Text.Text.Substring(position, next - position);
			parts.Add(layers.Count == 0 ? MarkupText.Plain(segment) : MarkupText.Wrap(MarkupSet.Of(layers), segment));
			position = next;
		}
		return Text.Replace(start, end - start, MarkupText.Concat(parts.ToImmutable().AsSpan()));
	}

	/// <summary>Each run of styled text as <c>decompose()</c> writes it, the unstyled text between as it is.</summary>
	private static string StyledAsSoftcode(MarkupText text)
	{
		var builder = new StringBuilder();
		var position = 0;
		var runs = text.Runs;
		var i = 0;
		while (i < runs.Length)
		{
			var stretchStart = runs[i].Start;
			var stretchEnd = runs[i].End;
			while (i + 1 < runs.Length && runs[i + 1].Start == stretchEnd) stretchEnd = runs[++i].End;
			i++;
			builder.Append(text.Text, position, stretchStart - position);
			builder.Append(SoftcodeDecomposer.Decompose(text.Substring(stretchStart, stretchEnd - stretchStart)));
			position = stretchEnd;
		}
		builder.Append(text.Text, position, text.Length - position);
		return builder.ToString();
	}

	private readonly record struct Snapshot(MarkupText Text, bool Raw);

	private void Remember(bool typing)
	{
		_redo.Clear();
		if (typing && _lastWasTyping && _undo.Count > 0)
		{
			return;
		}
		_lastWasTyping = typing;
		_undo.Add(new Snapshot(Text, Raw));
		if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
	}

	private bool Step(List<Snapshot> from, List<Snapshot> to)
	{
		if (from.Count == 0) return false;
		to.Add(new Snapshot(Text, Raw));
		var snapshot = from[^1];
		from.RemoveAt(from.Count - 1);
		Text = snapshot.Text;
		Raw = snapshot.Raw;
		Pending = null;
		_lastWasTyping = false;
		Changed?.Invoke();
		return true;
	}
}
