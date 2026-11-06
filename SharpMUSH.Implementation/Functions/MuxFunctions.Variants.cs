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
	/// <c>me</c> is the caller. It tells the caller nothing, as TinyMUX's quiet matches do: a dbref that matches
	/// nothing is <c>#-1</c>, a name <c>#-1 NO MATCH</c>.
	/// </summary>
	[SharpFunction(Name = "pfind", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["name"])]
	public async ValueTask<CallState> PlayerFind(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (name.StartsWith('#'))
		{
			return await LocateService.Locate(parser, executor, executor, name, LocateFlags.All) is AnySharpObject obj
				? new CallState(obj.Object().DBRef)
				: new CallState("#-1");
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
	/// <paramref name="code"/> with its substitutions and escapes evaluated and nothing else. Each run of words is
	/// evaluated apart, with its brackets, parentheses and braces escaped and everything between brackets escaped
	/// as well, and the spaces between runs are kept as they are, so the parser has none to compress. The text
	/// between escapes is cut from the code as it is, which keeps its markup.
	/// </summary>
	private static async ValueTask<MString> SubstitutionsEvaluated(IMUSHCodeParser parser, MString code)
	{
		var raw = code.Text;
		var output = new List<MString>();
		var word = new List<MString>();
		var (start, depth) = (0, 0);

		for (var i = 0; i < raw.Length; i++)
		{
			var c = raw[i];
			if (c == ' ')
			{
				await EndWord(i);
				var end = i;
				while (end < raw.Length && raw[end] == ' ') end++;
				output.Add(code.Substring(i, end - i));
				(start, i) = (end, end - 1);
			}
			else if (depth > 0)
			{
				if (c is '%' or '\\' or '[' or ']' or '(' or ')' or '{' or '}') Escape(i);
				depth += c switch { '[' => 1, ']' => -1, _ => 0 };
			}
			else if (c == '%' && i + 1 < raw.Length && raw[i + 1] is 'l' or 'L')
			{
				word.Add(code.Substring(start, i - start));
				start = ++i + 1;
			}
			else if (c is '\\' or '%' && i + 1 < raw.Length)
			{
				i++;
			}
			else if (c is '[' or ']' or '(' or ')' or '{' or '}')
			{
				Escape(i);
				if (c == '[') depth++;
			}
		}

		await EndWord(raw.Length);
		return MarkupText.Concat(output);

		void Escape(int at)
		{
			word.Add(code.Substring(start, at - start));
			word.Add(MarkupText.Plain("\\"));
			start = at;
		}

		async ValueTask EndWord(int end)
		{
			word.Add(code.Substring(start, end - start));
			var text = MarkupText.Concat(word);
			word.Clear();
			output.Add(text.ToPlainText().Length == 0 ? text : (await parser.FunctionParse(text))?.Message ?? MarkupText.Empty);
		}
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
