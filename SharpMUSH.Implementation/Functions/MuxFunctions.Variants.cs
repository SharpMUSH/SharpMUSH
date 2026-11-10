using System.Globalization;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// TinyMUX functions that have a near twin in SharpMUSH under another name, but differ from it enough that the
/// <c>mux</c> dialect cannot simply rename them: <c>pfind()</c>, <c>playmem()</c>, <c>subeval()</c> and
/// <c>url_unescape()</c>. Behaviour follows TinyMUX's documentation.
/// </summary>
public partial class Functions
{
	/// <summary>
	/// TinyMUX's <c>pfind()</c>: <c>pmatch()</c>, except that a dbref finds any object, not only a player. It tells the
	/// caller nothing, as TinyMUX's quiet matches do: a dbref that matches nothing is <c>#-1</c>. Anything else goes
	/// through TinyMUX's <c>lookup_player</c>: one <c>*</c> is dropped, <c>me</c> is the caller whatever it is, any
	/// further <c>*</c> are dropped, a <c>#</c> and a number find a player (any object for God), and a name finds the
	/// player of that name or a connected one it begins. A miss is <c>#-1 NO MATCH</c>.
	/// </summary>
	[SharpFunction(Name = "pfind", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["name"])]
	public async ValueTask<CallState> PlayerFind(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (name.StartsWith('#'))
		{
			// match_thing_quiet trims the name, and parse_dbref allows spaces around the number.
			var dbref = Number(name[1..]) is { } at ? $"#{at}" : name.Trim();
			return await LocateService.Locate(parser, executor, executor, dbref, LocateFlags.All) is AnySharpObject thing
				? new CallState(thing.Object().DBRef)
				: new CallState("#-1");
		}

		var player = name.StartsWith('*') ? name[1..] : name;
		if (player.Trim().Equals("me", StringComparison.OrdinalIgnoreCase)) return new CallState(executor.Object().DBRef);

		player = player.TrimStart('*');
		var found = player.StartsWith('#')
			? Number(player[1..]) is { } number
				&& await LocateService.Locate(parser, executor, executor, $"#{number}", LocateFlags.All) is AnySharpObject obj
				&& (obj.IsPlayer || executor.IsGod()) ? obj : null
			: player.Length == 0 || player != player.Trim() ? null
			: await LocateService.Locate(parser, executor, executor, player, PlayerNameFlags) is AnySharpObject match
				&& match.IsPlayer ? match : null;
		return found is { } result
			? new CallState(result.Object().DBRef)
			: new CallState(ErrorMessages.Returns.NoMatch);
	}

	/// <summary>A dbref's number as TinyMUX's <c>is_integer</c> reads it: a sign and spaces around it allowed.</summary>
	private static long? Number(string text)
		=> long.TryParse(text, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign,
			CultureInfo.InvariantCulture, out var number) ? number : null;

	/// <summary>A player's name as <c>lookup_player</c> reads it, where <c>me</c> is only a name.</summary>
	private const LocateFlags PlayerNameFlags = LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference
		| LocateFlags.EnglishStyleMatching | LocateFlags.MatchOptionalWildCardForPlayerName;

	/// <summary>
	/// TinyMUX's <c>playmem()</c>: <c>playermem()</c> with the player optional, the executor when it is left out,
	/// and refused for a player the executor cannot examine. SharpMUSH reports no memory use, so it is 0.
	/// </summary>
	[SharpFunction(Name = "playmem", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public async ValueTask<CallState> PlayMem(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var name = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message?.ToPlainText() ?? string.Empty;
		if (name.Length == 0) return new CallState(0);

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, name,
			LocateFlags.All,
			async found => await PermissionService.CanExamine(executor, found)
				? new CallState(0)
				: PermissionRefusal.Examine(found).Return);
	}

	/// <summary>
	/// TinyMUX's <c>subeval()</c>: the text with its <c>%</c> substitutions and <c>\</c> escapes evaluated once
	/// more. Function calls, braces and whatever sits between brackets are left as written, <c>%l</c> gives
	/// nothing, spaces are not compressed, and the text keeps its colour.
	/// </summary>
	[SharpFunction(Name = "subeval", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public async ValueTask<CallState> SubstitutionEvaluate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(await SubstitutionsEvaluated(parser, parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty,
			ExecutorAttribute));

	/// <summary>The executor's attribute <paramref name="name"/> as <c>%=&lt;name&gt;</c> reads it, or nothing.</summary>
	private async ValueTask<MString> ExecutorAttribute(IMUSHCodeParser parser, string name)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await AttributeService.GetAttributeAsync(executor, executor, name, IAttributeService.AttributeMode.Read)
			is SharpAttribute[] attribute
			? attribute.Last().Value
			: MarkupText.Empty;
	}

	/// <summary>
	/// <paramref name="code"/> read token by token as TinyMUX's scanner reads it (<c>ast_scan.rl</c>), with each
	/// substitution and escape replaced as TinyMUX's <c>ast_eval_subst</c> replaces it and everything else written as
	/// it is. A bracket and what it holds stay as written, a brace group's contents are evaluated, and a group left
	/// open is closed at the end. A substitution SharpMUSH also has is evaluated alone by the parser and takes the
	/// markup of the text it replaces; the rest is cut from the code, which keeps its markup and its points.
	/// </summary>
	private static async ValueTask<MString> SubstitutionsEvaluated(IMUSHCodeParser parser, MString code,
		Func<IMUSHCodeParser, string, ValueTask<MString>> attribute)
	{
		var raw = code.Text;
		var point = Points(code);
		var output = new List<MString>();
		var open = new Stack<char>();
		var start = 0;

		for (var i = 0; i < raw.Length;)
		{
			var c = point[i] ? '\0' : raw[i];
			var length = TokenLength(raw, point, i);
			if (open.Contains(']') || c is not ('%' or '\\' or '#') || length == 1 && c == '#')
			{
				if (c is '[' or '{') open.Push(c == '[' ? ']' : '}');
				else if (c is ']' or '}' && open.Count > 0 && open.Peek() == c) open.Pop();
			}
			else
			{
				output.Add(code.Substring(start, i - start));
				output.Add(c == '\\' ? code.Substring(i + 1, length - 1) : await Substituted(parser, code, point, i, length, attribute));
				start = i + length;
			}

			i += length;
		}

		output.Add(code.Substring(start, raw.Length - start));
		if (open.Count > 0) output.Add(MarkupText.Plain(string.Concat(open)));
		return MarkupText.Concat(output);
	}

	/// <summary>How long the token at <paramref name="at"/> is, by TinyMUX's scanner rules.</summary>
	private static int TokenLength(string raw, bool[] point, int at)
	{
		char? At(int index) => index < raw.Length && !point[index] ? raw[index] : null;
		var (first, second, third) = (At(at), At(at + 1), At(at + 2));
		var hasSecond = at + 1 < raw.Length;
		var hasThird = at + 2 < raw.Length;
		switch (first)
		{
			case '\\':
				return hasSecond ? 2 : 1;
			case '#':
				return second is '#' or '@' or '$' ? 2 : 1;
			case '%' when !hasSecond:
				return 1;
			case '%' when second is 'q' or 'Q' or 'c' or 'C' or 'x' or 'X' or '=' && third == '<':
				var end = at + 3;
				while (end < raw.Length && (point[end] || raw[end] is not ('>' or '}' or ')' or ']' or '[' or '{' or ','))) end++;
				return end < raw.Length && raw[end] == '>' ? end + 1 - at : end - at;
			case '%' when second is >= '0' and <= '9':
				return 2;
			case '%' when second is 'q' or 'Q' or 'c' or 'C' or 'x' or 'X':
				return hasThird ? 3 : 2;
			case '%' when second is 'v' or 'V' && char.IsAsciiLetter(third ?? '\0'):
				return 3;
			case '%' when second is 'i' or 'I' && char.IsAsciiDigit(third ?? '\0'):
				return 3;
			case '%':
				return 2;
			default:
				return 1;
		}
	}

	/// <summary>
	/// What the substitution token of <paramref name="length"/> characters at <paramref name="at"/> gives, as
	/// TinyMUX's <c>ast_eval_subst</c> decides it: nothing for an empty or malformed register or attribute name, the
	/// token as written for an unclosed name, the character itself for a letter TinyMUX does not know, and otherwise
	/// what SharpMUSH's own substitution gives.
	/// </summary>
	private static async ValueTask<MString> Substituted(IMUSHCodeParser parser, MString code, bool[] point, int at,
		int length, Func<IMUSHCodeParser, string, ValueTask<MString>> attribute)
	{
		var token = code.Text.Substring(at, length);
		if (length == 1) return code.Substring(at, 1);
		if (token[0] == '#') return await Evaluated(parser, code, at, length);
		if (point[at + 1]) return code.Substring(at + 1, 1);

		// A token written out as it stands keeps its points; any other gives them after its value.
		var letter = char.ToUpperInvariant(token[1]);
		var asWritten = length >= 3 && (letter is 'C' or 'X' && point[at + 2]
			|| token[2] == '<' && token[^1] != '>' && (letter is 'Q' or 'C' or 'X' || letter == '=' && length >= 4));
		var value = await TokenValue(parser, code, point, at, length, attribute);
		return asWritten ? value
			: MarkupText.Concat([value, .. Enumerable.Range(at, length).Where(index => point[index]).Select(index => code.Substring(index, 1))]);
	}

	/// <summary><see cref="Substituted"/>'s value for a token whose second character is not a point.</summary>
	private static async ValueTask<MString> TokenValue(IMUSHCodeParser parser, MString code, bool[] point, int at,
		int length, Func<IMUSHCodeParser, string, ValueTask<MString>> attribute)
	{
		var token = code.Text.Substring(at, length);
		var named = length >= 3 && token[2] == '<';
		var closed = named && length >= 4 && token[^1] == '>';
		var name = closed ? token[3..^1] : string.Empty;
		return char.ToUpperInvariant(token[1]) switch
		{
			'Q' when named => !closed ? code.Substring(at, length)
				: name.Length is > 0 and <= 32 && name.All(x => char.IsAsciiLetterOrDigit(x) || x == '_')
					? await Evaluated(parser, code, at, length)
					: MarkupText.Empty,
			'=' when closed && name.Length > 0 && char.IsAsciiDigit(name[0]) => ArgumentNumber(name) is { } number
				? await Evaluated(parser, code, at, length, $"%=<{number}>")
				: MarkupText.Empty,
			'Q' => length == 3 && char.IsAsciiLetterOrDigit(token[2]) ? await Evaluated(parser, code, at, length) : MarkupText.Empty,
			'=' when named && length >= 4 => !closed ? code.Substring(at, length)
				: AttributeName().IsMatch(name)
					? Styled(code, at, length, await attribute(parser, name))
					: MarkupText.Empty,
			'=' or 'L' => MarkupText.Empty,
			'C' or 'X' when named && !closed => code.Substring(at, length),
			'C' or 'X' when length == 3 && point[at + 2] => code.Substring(at + 2, 1),
			'V' or 'I' or 'C' or 'X' => length == 3 ? await Evaluated(parser, code, at, length) : MarkupText.Empty,
			>= '0' and <= '9' or '#' or '!' or '@' or '%' or 'R' or 'B' or 'T' or 'N' or 'S' or 'P' or 'O' or 'A' or 'M' or 'K'
				or '|' or '+' or ':' => await Evaluated(parser, code, at, length),
			_ => code.Substring(at + 1, 1)
		};
	}

	/// <summary>
	/// The substitution token at <paramref name="at"/> (or <paramref name="written"/> in its place) evaluated alone
	/// by the parser, in the markup of the text it replaces.
	/// </summary>
	private static async ValueTask<MString> Evaluated(IMUSHCodeParser parser, MString code, int at, int length,
		string? written = null)
	{
		var value = (await parser.FunctionParse(MarkupText.Plain(written ?? code.Text.Substring(at, length))))?.Message
			?? MarkupText.Empty;
		return Styled(code, at, length, value);
	}

	/// <summary>
	/// <paramref name="value"/> in the markup of the token it replaces: inserted after the token's first character,
	/// where it takes that character's markup, with the token then removed around it.
	/// </summary>
	private static MString Styled(MString code, int at, int length, MString value)
	{
		var token = code.Substring(at, length);
		return value.Text.Length == 0 ? value
			: token.Insert(1, value).Remove(1 + value.Text.Length, length - 1).Remove(0, 1);
	}

	/// <summary>
	/// The argument <c>%=&lt;name&gt;</c> names, as TinyMUX reads it: its first one to three digits, and none when a
	/// fourth digit follows.
	/// </summary>
	private static int? ArgumentNumber(string name)
	{
		var digits = name.TakeWhile(char.IsAsciiDigit).Count();
		return digits > 3 ? null : int.Parse(name[..digits]);
	}

	/// <summary>Which characters of <paramref name="text"/> carry a point, the ones its plain text leaves out.</summary>
	private static bool[] Points(MString text)
	{
		var (raw, plain) = (text.Text, text.ToPlainText());
		var point = new bool[raw.Length];
		for (int i = 0, j = 0; i < raw.Length; i++)
		{
			if (j < plain.Length && raw[i] == plain[j]) j++;
			else point[i] = true;
		}

		return point;
	}

	/// <summary>
	/// An attribute name as <c>%=&lt;name&gt;</c> reads one: TinyMUX's <c>mux_isattrnameinitial</c> for the first
	/// character and <c>mux_isattrname</c> for the rest, taking its letters beyond ASCII from Latin-1 only.
	/// </summary>
	[System.Text.RegularExpressions.GeneratedRegex(@"^[#.A-Za-z_~\u00AA\u00B5\u00BA\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u00FF][!#$&'()+\-./0-9<=>?@A-Za-z^_`~\u00AA\u00B5\u00BA\u00C0-\u00D6\u00D8-\u00F6\u00F8-\u00FF]*$")]
	private static partial System.Text.RegularExpressions.Regex AttributeName();

	/// <summary>
	/// TinyMUX's <c>url_unescape()</c>: each <c>%XX</c> becomes the byte it names, read as UTF-8, and each
	/// <c>+</c> a space, as a submitted form writes it. <c>urldecode()</c> keeps the <c>+</c>.
	/// </summary>
	[SharpFunction(Name = "url_unescape", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public ValueTask<CallState> UrlUnescape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(
			Uri.UnescapeDataString(parser.CurrentState.Arguments["0"].Message.ToPlainText().Replace("+", "%20"))));
}
