using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkupString;

/// <summary>
/// Temporary migration shim. Every consumer still spells the markup API as
/// <c>MModule.something(...)</c> through <c>global using MModule = global::MarkupString.MarkupStringModule;</c>;
/// this class keeps those call sites compiling against <see cref="MarkupText"/> while the consumer
/// migration lands one group at a time. Members are thin forwarders, and keep the old argument
/// order and the old null tolerance of the lowercase spellings.
/// <para>
/// The MUSH-specific members (<c>splitList</c>, <c>compressSpaces</c>, the wildcard helpers) carry
/// their own copies of the old implementations because their permanent home,
/// <c>SharpMUSH.Library.Markup.MushText</c>, lives in an assembly this one cannot reference. The two
/// must stay semantically identical.
/// </para>
/// <para>This whole file is deleted in task 15, once no call site names it.</para>
/// </summary>
public static partial class MarkupStringModule
{
	// ── construction ────────────────────────────────────────────────────────────

	public static MarkupText Single(string str) => MarkupText.Plain(str);
	public static MarkupText single(string? str) => str is null ? MarkupText.Empty : MarkupText.Plain(str);

	public static MarkupText Empty() => MarkupText.Empty;
	public static MarkupText empty() => MarkupText.Empty;

	public static MarkupText Space() => MarkupText.Space;

	public static MarkupText MarkupSingle(IMarkup markup, string str) => MarkupText.Wrap(markup, str);
	public static MarkupText markupSingle((IMarkup, string) t) => MarkupText.Wrap(t.Item1, t.Item2);

	public static MarkupText MarkupSingleMulti(ImmutableArray<IMarkup> markups, string str) =>
		markups.IsDefaultOrEmpty ? MarkupText.Plain(str) : MarkupText.Wrap(MarkupSet.Of(markups.AsSpan()), str);
	public static MarkupText markupSingleMulti((ImmutableArray<IMarkup>, string) t) =>
		MarkupSingleMulti(t.Item1, t.Item2);

	public static MarkupText MarkupSingle2(IMarkup markup, MarkupText inner) => MarkupText.Wrap(markup, inner);
	public static MarkupText markupSingle2((IMarkup, MarkupText) t) => MarkupText.Wrap(t.Item1, t.Item2);

	public static MarkupText MarkupMultiple(IMarkup markup, IEnumerable<MarkupText> items) =>
		MarkupText.Wrap(markup, MarkupText.Concat(items));
	public static MarkupText markupMultiple((IMarkup, IEnumerable<MarkupText>) t) =>
		MarkupMultiple(t.Item1, t.Item2);

	// ── inspection ──────────────────────────────────────────────────────────────

	public static string PlainText(MarkupText ams) => ams.ToPlainText();
	public static string plainText(MarkupText? ams) => ams?.ToPlainText() ?? string.Empty;

	public static MarkupText PlainText2(MarkupText ams) => MarkupText.Plain(ams.ToPlainText());
	public static MarkupText plainText2(MarkupText ams) => PlainText2(ams);

	public static int GetLength(MarkupText ams) => ams.Length;
	public static int getLength(MarkupText? ams) => ams?.Length ?? 0;

	public static int IndexOf(MarkupText ams, string search) => ams.IndexOf(search);
	public static int indexOf(MarkupText? ams, string search) => ams?.IndexOf(search) ?? -1;

	public static int IndexOf2(MarkupText ams, MarkupText search) => ams.IndexOf(search.Text);
	public static int indexOf2(MarkupText ams, MarkupText search) => IndexOf2(ams, search);

	public static int IndexOfLast(MarkupText ams, string search) => ams.LastIndexOf(search);
	public static int indexOfLast(MarkupText ams, string search) => IndexOfLast(ams, search);

	public static int IndexOfLast2(MarkupText ams, MarkupText search) => ams.LastIndexOf(search.Text);
	public static int indexOfLast2(MarkupText ams, MarkupText search) => IndexOfLast2(ams, search);

	public static IEnumerable<int> IndexesOf(MarkupText ams, MarkupText search) => ams.IndexesOf(search.Text);
	public static IEnumerable<int> indexesOf(MarkupText ams, MarkupText search) => IndexesOf(ams, search);

	// ── combination ─────────────────────────────────────────────────────────────

	public static MarkupText Concat(MarkupText a, MarkupText b) => MarkupText.Concat(a, b);
	public static MarkupText concat(MarkupText? a, MarkupText? b) =>
		MarkupText.Concat(a ?? MarkupText.Empty, b ?? MarkupText.Empty);

	public static MarkupText ConcatMany(IEnumerable<MarkupText> items) => MarkupText.Concat(items);
	public static MarkupText concatMany(IEnumerable<MarkupText> items) => MarkupText.Concat(items);

	public static MarkupText Multiple(IEnumerable<MarkupText> items) => MarkupText.Concat(items);
	public static MarkupText multiple(IEnumerable<MarkupText?> items) =>
		MarkupText.Concat(items.Where(x => x is not null).Select(x => x!));

	public static MarkupText MultipleWithDelimiter(MarkupText delimiter, IEnumerable<MarkupText> items) =>
		MarkupText.Join(delimiter, items);
	public static MarkupText multipleWithDelimiter(MarkupText? delimiter, IEnumerable<MarkupText?> items) =>
		MarkupText.Join(delimiter ?? MarkupText.Empty, items.Where(x => x is not null).Select(x => x!));

	public static IEnumerable<MarkupText> IntersperseFunc(Func<int, MarkupText> sepFunc, IEnumerable<MarkupText> items)
	{
		var i = 0;
		foreach (var item in items)
		{
			if (i > 0) yield return sepFunc(i);
			yield return item;
			i++;
		}
	}
	public static IEnumerable<MarkupText> intersperseFunc(Func<int, MarkupText> sepFunc, IEnumerable<MarkupText> items) =>
		IntersperseFunc(sepFunc, items);

	public static MarkupText MultipleWithDelimiterFunc(Func<int, MarkupText> delimiterFunc, IEnumerable<MarkupText> items) =>
		MarkupText.Join(delimiterFunc, items);
	public static MarkupText multipleWithDelimiterFunc(Func<int, MarkupText> delimiterFunc, IEnumerable<MarkupText> items) =>
		MarkupText.Join(delimiterFunc, items);

	public static MarkupText ConcatAttach(MarkupText a, MarkupText b) => a.AttachTail(b);
	public static MarkupText concatAttach(MarkupText a, MarkupText b) => a.AttachTail(b);

	// ── slicing and editing ─────────────────────────────────────────────────────

	public static MarkupText Substring(int start, int length, MarkupText ams) => ams.Substring(start, length);
	public static MarkupText substring(int start, int length, MarkupText? ams) =>
		ams is null ? MarkupText.Empty : ams.Substring(start, length);

	public static MarkupText[] Split(string delimiter, MarkupText ams) => ams.Split(delimiter);
	public static MarkupText[] split(string delimiter, MarkupText? ams) => ams is null ? [] : ams.Split(delimiter);

	public static MarkupText[] Split2(MarkupText delimiter, MarkupText ams) => ams.Split(delimiter.Text);
	public static MarkupText[] split2(MarkupText delimiter, MarkupText ams) => ams.Split(delimiter.Text);

	public static MarkupText Trim(MarkupText ams, string trimChars, TrimType trimType) => ams.Trim(trimType, trimChars);
	public static MarkupText trim(MarkupText ams, string trimChars, TrimType trimType) => ams.Trim(trimType, trimChars);

	public static MarkupText Trim2(MarkupText ams, MarkupText trimStr, TrimType trimType) =>
		ams.Trim(trimType, trimStr.Text);
	public static MarkupText trim2(MarkupText ams, MarkupText trimStr, TrimType trimType) =>
		ams.Trim(trimType, trimStr.Text);

	public static MarkupText Remove(MarkupText ams, int index, int length) => ams.Remove(index, length);
	public static MarkupText remove(MarkupText ams, int index, int length) => ams.Remove(index, length);

	public static MarkupText Replace(MarkupText ams, MarkupText replacement, int index, int length) =>
		ams.Replace(index, length, replacement);
	public static MarkupText replace(MarkupText ams, MarkupText replacement, int index, int length) =>
		ams.Replace(index, length, replacement);

	public static MarkupText InsertAt(MarkupText input, MarkupText insert, int index) => input.Insert(index, insert);
	public static MarkupText insertAt(MarkupText input, MarkupText insert, int index) => input.Insert(index, insert);

	public static MarkupText Repeat(MarkupText ams, int count) => ams.Repeat(count);
	public static MarkupText repeat(MarkupText ams, int count) => ams.Repeat(count);

	public static MarkupText Pad(MarkupText ams, MarkupText padStr, int width, PadType padType, TruncationType truncType) =>
		ams.Pad(padStr, width, padType, truncType);
	public static MarkupText pad(MarkupText ams, MarkupText padStr, int width, PadType padType, TruncationType truncType) =>
		ams.Pad(padStr, width, padType, truncType);

	public static MarkupText Center2(MarkupText ams, MarkupText padStr, MarkupText padStrRight, int width,
		TruncationType truncType) => ams.Center(padStr, padStrRight, width, truncType);
	public static MarkupText center2(MarkupText ams, MarkupText padStr, MarkupText padStrRight, int width,
		TruncationType truncType) => ams.Center(padStr, padStrRight, width, truncType);

	public static MarkupText Apply(MarkupText ams, Func<string, string> transform) => ams.Apply(transform);
	public static MarkupText apply(MarkupText ams, Func<string, string> transform) => ams.Apply(transform);

	public static MarkupText Apply2(MarkupText ams, Func<MarkupText, MarkupText> transform) => ams.Map(transform);
	public static MarkupText apply2(MarkupText ams, Func<MarkupText, MarkupText> transform) => ams.Map(transform);

	/// <summary>
	/// Walks the text run by run, handing each segment to <paramref name="evaluator"/> once per markup
	/// layer (innermost first) and once with <see langword="null"/> for unstyled stretches.
	/// </summary>
	public static string EvaluateWith(Func<IMarkup?, string, string> evaluator, MarkupText ams)
	{
		var builder = new StringBuilder(ams.Length);
		var position = 0;
		foreach (var run in ams.Runs)
		{
			if (run.Start > position) builder.Append(evaluator(null, ams.Text[position..run.Start]));
			var segment = ams.Text.Substring(run.Start, run.Length);
			foreach (var markup in run.Markups) segment = evaluator(markup, segment);
			builder.Append(segment);
			position = run.End;
		}
		if (position < ams.Length) builder.Append(evaluator(null, ams.Text[position..]));
		return builder.ToString();
	}
	public static string evaluateWith(Func<IMarkup?, string, string> evaluator, MarkupText ams) =>
		EvaluateWith(evaluator, ams);

	// ── MUSH-specific helpers (mirrored by SharpMUSH.Library.Markup.MushText) ────

	public static MarkupText[] SplitList(MarkupText delimiter, MarkupText ams)
	{
		var items = ams.Split(delimiter.Text);
		return delimiter.Text == " " ? Array.FindAll(items, x => x.Length > 0) : items;
	}
	public static MarkupText[] splitList(MarkupText? delimiter, MarkupText? ams) =>
		SplitList(delimiter ?? MarkupText.Empty, ams ?? MarkupText.Empty);

	/// <summary>
	/// Collapses every run of two or more spaces to one (PennMUSH <c>PE_COMPRESS_SPACES</c>),
	/// preserving the markup on the text around them.
	/// </summary>
	public static MarkupText CompressSpaces(MarkupText ams)
	{
		if (ams.IndexOf("  ") < 0) return ams;

		var text = ams.Text;
		var edits = new List<Edit>();
		var i = 0;
		while (i < text.Length)
		{
			if (text[i] != ' ') { i++; continue; }
			var start = i;
			while (i < text.Length && text[i] == ' ') i++;
			if (i - start > 1) edits.Add(new Edit(start, i - start, MarkupText.Space));
		}
		return edits.Count == 0 ? ams : ams.Splice(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits));
	}
	public static MarkupText compressSpaces(MarkupText ams) => CompressSpaces(ams);

	[GeneratedRegex(@"(?<!\\)\\\*")]
	private static partial Regex GlobPatternRegex();
	[GeneratedRegex(@"(?<!\\)\\\?")]
	private static partial Regex QuestionPatternRegex();
	[GeneratedRegex(@"\\\\\\\*")]
	private static partial Regex KindPatternRegex();
	[GeneratedRegex(@"\\\\\\\?")]
	private static partial Regex KindPattern2Regex();

	/// <summary>
	/// Inline single-line mode, so the <c>.</c> that <c>*</c> and <c>?</c> compile to also matches a
	/// newline — PennMUSH's matcher has no notion of a line.
	/// </summary>
	private const string SingleLineMode = "(?s)";

	private static string ApplyRegexPattern(string pat)
	{
		pat = GlobPatternRegex().Replace(pat, "(.*?)");
		pat = QuestionPatternRegex().Replace(pat, "(.)");
		pat = KindPatternRegex().Replace(pat, @"\*");
		pat = KindPattern2Regex().Replace(pat, @"\?");
		return SingleLineMode + pat;
	}

	public static string GetWildcardMatchAsRegex(MarkupText pattern) =>
		ApplyRegexPattern($"^{Regex.Escape(pattern.ToPlainText())}$");
	public static string getWildcardMatchAsRegex(MarkupText pattern) => GetWildcardMatchAsRegex(pattern);

	public static string GetWildcardMatchAsRegex2(string pattern) =>
		ApplyRegexPattern($"^{Regex.Escape(pattern)}$");
	public static string getWildcardMatchAsRegex2(string pattern) => GetWildcardMatchAsRegex2(pattern);

	public static bool IsWildcardMatch(MarkupText input, MarkupText pattern) =>
		Regex.IsMatch(input.ToPlainText(), GetWildcardMatchAsRegex(pattern));
	public static bool isWildcardMatch(MarkupText? input, MarkupText? pattern) =>
		input is not null && pattern is not null && IsWildcardMatch(input, pattern);

	public static bool IsWildcardMatch2(MarkupText input, string pattern) =>
		Regex.IsMatch(input.ToPlainText(), GetWildcardMatchAsRegex2(pattern));
	public static bool isWildcardMatch2(MarkupText input, string pattern) => IsWildcardMatch2(input, pattern);

	public static IEnumerable<(Match Match, IEnumerable<MarkupText> Groups)> GetMatches(MarkupText input, string pattern)
	{
		foreach (var m in Regex.Matches(input.ToPlainText(), pattern).Cast<Match>())
		{
			var groups = m.Groups.Cast<Group>().Select(g => input.Substring(g.Index, g.Length));
			yield return (m, groups);
		}
	}
	public static IEnumerable<(Match, IEnumerable<MarkupText>)> getMatches(MarkupText input, string pattern) =>
		GetMatches(input, pattern);

	public static IEnumerable<(Match, IEnumerable<MarkupText>)> GetRegexpMatches(MarkupText input, MarkupText pattern) =>
		GetMatches(input, pattern.ToPlainText());
	public static IEnumerable<(Match, IEnumerable<MarkupText>)> getRegexpMatches(MarkupText input, MarkupText pattern) =>
		GetRegexpMatches(input, pattern);

	public static IEnumerable<(Match, IEnumerable<MarkupText>)> GetWildcardMatches(MarkupText input, MarkupText pattern) =>
		GetMatches(input, GetWildcardMatchAsRegex(pattern));
	public static IEnumerable<(Match, IEnumerable<MarkupText>)> getWildcardMatches(MarkupText input, MarkupText pattern) =>
		GetWildcardMatches(input, pattern);

	// ── serialisation ───────────────────────────────────────────────────────────

	public static string Serialize(MarkupText ams) => MarkupTextSerializer.Serialize(ams);
	public static string serialize(MarkupText ams) => MarkupTextSerializer.Serialize(ams);

	public static MarkupText Deserialize(string jsonString) => MarkupTextSerializer.Deserialize(jsonString);
	public static MarkupText deserialize(string jsonString) => MarkupTextSerializer.Deserialize(jsonString);
}
