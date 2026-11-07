using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
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
	/// TinyMUX's <c>pfind()</c>: <c>pmatch()</c>, except that a dbref finds any object, not only a player, and
	/// <c>me</c> is the caller, and a leading <c>*</c> is dropped, after which a dbref finds only a player unless the
	/// caller is God. It tells the caller nothing, as TinyMUX's quiet matches do: a dbref that matches
	/// nothing is <c>#-1</c>, a name <c>#-1 NO MATCH</c>.
	/// </summary>
	[SharpFunction(Name = "pfind", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["name"])]
	public async ValueTask<CallState> PlayerFind(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var starred = name.StartsWith('*');
		if (starred) name = name[1..];
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (name.StartsWith('#'))
		{
			return await LocateService.Locate(parser, executor, executor, name, LocateFlags.All) switch
			{
				AnySharpObject obj when !starred || obj.IsPlayer || executor.IsGod() => new CallState(obj.Object().DBRef),
				_ => new CallState(starred ? ErrorMessages.Returns.NoMatch : "#-1")
			};
		}

		return await LocateService.LocateConnectionTarget(parser, executor, executor, name) is AnySharpObject player
			? new CallState(player.Object().DBRef)
			: new CallState(ErrorMessages.Returns.NoMatch);
	}

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
		=> new(await SubstitutionsEvaluated(parser, parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty));

	/// <summary>
	/// <paramref name="code"/> with its substitutions and escapes evaluated and nothing else, read as TinyMUX's
	/// scanner reads it. Spaces, brackets, parentheses and braces are escaped, and so is every character between
	/// brackets, where a brace group or a <c>\x</c>, <c>%x</c> or <c>%qx</c> token does not close the bracket. A
	/// bracket or brace left open is closed at the end. <c>%q&lt;</c> or <c>%=&lt;</c> whose name does not close
	/// before one of <c>}])[{,</c> stays as written, and <c>%&lt;</c> and <c>%*&lt;</c>, which TinyMUX does not have,
	/// lose their <c>%</c>. The text between points is parsed once each, wrapped in <c>[]</c> so the parser keeps its
	/// leading and trailing spaces; the points and the text between escapes are cut from the code as they are, which
	/// keeps their markup.
	/// </summary>
	private static async ValueTask<MString> SubstitutionsEvaluated(IMUSHCodeParser parser, MString code)
	{
		var raw = code.Text;
		var point = Points(code);
		var output = new List<MString>();
		var parts = new List<MString>();
		var open = new Stack<char>();
		var start = 0;

		for (var i = 0; i < raw.Length; i++)
		{
			var c = raw[i];
			char? next = i + 1 < raw.Length && !point[i + 1] ? raw[i + 1] : null;
			if (point[i])
			{
				await Flush(i);
				output.Add(code.Substring(i, 1));
				start = i + 1;
			}
			else if (open.Contains(']'))
			{
				Escape(i);
				if (c is '\\' or '%' && next is not null)
				{
					Escape(++i);
					if (c == '%' && raw[i] is 'q' or 'Q' or 'c' or 'C' or 'x' or 'X' or 'v' or 'V' or 'i' or 'I'
							&& i + 1 < raw.Length && !point[i + 1]) Escape(++i);
				}
				else if (c is '[' or '{') open.Push(c == '[' ? ']' : '}');
				else if (c is ']' or '}' && open.Peek() == c) open.Pop();
			}
			else if (c == '%' && next is 'l' or 'L')
			{
				Cut(i, 2);
				i++;
			}
			else if (c == '%' && (next == '<' || next == '*' && At(i + 2) == '<'))
			{
				Cut(i, 1);
			}
			else if (c == '%' && next is 'q' or 'Q' or '=' && At(i + 2) == '<')
			{
				var end = i + 3;
				while (At(end) is { } x && x is not ('>' or '}' or ')' or ']' or '[' or '{' or ',')) end++;
				if (At(end) != '>')
				{
					Escape(i);
					i++;
				}
				else if (end == i + 3)
				{
					Cut(i, 4);
					i = end;
				}
				else
				{
					i = end;
				}
			}
			else if (c is '\\' or '%' && next is not null)
			{
				i++;
			}
			else if (c == '%')
			{
				Escape(i);
			}
			else if (c == '\\')
			{
				Cut(i, 1);
			}
			else if (c is ' ' or '[' or ']' or '(' or ')' or '{' or '}')
			{
				Escape(i);
				if (c is '[' or '{') open.Push(c == '[' ? ']' : '}');
				else if (c == '}' && open.Count > 0) open.Pop();
			}
		}

		await Flush(raw.Length);
		if (open.Count > 0) output.Add(MarkupText.Plain(string.Concat(open)));
		return MarkupText.Concat(output);

		char? At(int at) => at < raw.Length && !point[at] ? raw[at] : null;

		void Escape(int at)
		{
			parts.Add(code.Substring(start, at - start));
			parts.Add(MarkupText.Plain("\\"));
			start = at;
		}

		void Cut(int at, int length)
		{
			parts.Add(code.Substring(start, at - start));
			start = at + length;
		}

		async ValueTask Flush(int end)
		{
			parts.Add(code.Substring(start, end - start));
			var text = MarkupText.Concat(parts);
			parts.Clear();
			if (text.Text.Length == 0) return;
			var parsed = await parser.FunctionParse(MarkupText.Concat([MarkupText.Plain("[]"), text, MarkupText.Plain("[]")]));
			output.Add(parsed?.Message ?? MarkupText.Empty);
		}
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
	/// TinyMUX's <c>url_unescape()</c>: each <c>%XX</c> becomes the byte it names, read as UTF-8, and each
	/// <c>+</c> a space, as a submitted form writes it. <c>urldecode()</c> keeps the <c>+</c>.
	/// </summary>
	[SharpFunction(Name = "url_unescape", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public ValueTask<CallState> UrlUnescape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(
			Uri.UnescapeDataString(parser.CurrentState.Arguments["0"].Message.ToPlainText().Replace("+", "%20"))));
}
