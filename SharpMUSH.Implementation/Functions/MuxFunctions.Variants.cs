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
	/// TinyMUX's <c>pfind()</c>: <c>pmatch()</c>, except that a dbref finds any object, not only a player. A miss
	/// is <c>#-1 NO MATCH</c> and tells the caller nothing, as TinyMUX's quiet matches do.
	/// </summary>
	[SharpFunction(Name = "pfind", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["name"])]
	public async ValueTask<CallState> PlayerFind(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var found = name.StartsWith('#')
			? await LocateService.Locate(parser, executor, executor, name, LocateFlags.All)
			: await LocateService.LocatePlayer(parser, executor, executor, name);

		return found is AnySharpObject obj
			? new CallState(obj.Object().DBRef)
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
	/// TinyMUX's <c>subeval()</c>: the text with its <c>%</c> substitutions and <c>\</c> escapes evaluated, and its
	/// function calls, brackets and braces left as written, its colour kept. <c>%l</c> (the location) is left as
	/// written too.
	/// </summary>
	[SharpFunction(Name = "subeval", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public async ValueTask<CallState> SubstitutionEvaluate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await parser.FunctionParse(SubstitutionsOnly(parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty, mux: true))
			?? CallState.Empty;

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
