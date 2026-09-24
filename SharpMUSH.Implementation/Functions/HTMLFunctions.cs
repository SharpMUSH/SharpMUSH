using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using static SharpMUSH.Library.Services.Interfaces.LocateFlags;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// PennMUSH's html(), tag() and endtag() write one end of a tag each. Markup here is a span over the
	/// text it covers, which has no half-open form, and a tag written into the text as characters is
	/// escaped like any other &lt; — so all three point at <c>tagwrap()</c>.
	/// </summary>
	[SharpFunction(Name = "html", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = ["tag", "text..."])]
	public ValueTask<CallState> HTML(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(ErrorMessages.Returns.UseTagwrapInstead);

	[SharpFunction(Name = "tag", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["tagname", "content", "attributes"])]
	public ValueTask<CallState> Tag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(ErrorMessages.Returns.UseTagwrapInstead);

	[SharpFunction(Name = "endtag", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["tagname"])]
	public ValueTask<CallState> EndTag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(ErrorMessages.Returns.UseTagwrapInstead);

	/// <summary>
	/// <c>tagwrap(&lt;name&gt;[, &lt;parameters&gt;], &lt;string&gt;)</c> — the wrapped STRING is the last
	/// argument, with the optional tag parameters in the middle (<c>help tagwrap</c>, and PennMUSH).
	///
	/// <para>The tag is markup on the string, not text in it, as PennMUSH's is (<c>safe_tag_wrap</c>,
	/// <c>src/markup.c</c>): a Pueblo, MXP or portal client receives it as a tag, every other client the
	/// string alone, and <c>strlen()</c> counts only the string. Written into the text instead, the
	/// renderer escapes it like any other &lt; and every client shows it literally.</para>
	///
	/// <para><see cref="TagwrapPolicy"/> is the gate: without Send_OOB, only PennMUSH's tags and only
	/// parameters a browser cannot be made to run.</para>
	/// </summary>
	[SharpFunction(Name = "tagwrap", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["tag", "parameters", "content"])]
	public async ValueTask<CallState> TagWrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var tagName = args["0"].Message!.ToPlainText().Trim();
		var hasParameters = args.Count > 2;
		var content = args[hasParameters ? "2" : "1"].Message!;
		var parameters = hasParameters ? args["1"].Message!.ToPlainText().Trim() : string.Empty;

		if (!HtmlMarkup.IsValidTagName(tagName))
		{
			return new CallState(ErrorMessages.Returns.InvalidTagName);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await CanSendOob(executor))
		{
			return new CallState(MarkupText.Wrap(
				HtmlMarkup.Create(tagName, parameters.Length == 0 ? null : parameters), content));
		}

		return TagwrapPolicy.Wrap(tagName, parameters) is { } markup
			? new CallState(MarkupText.Wrap(markup, content))
			: new CallState(ErrorMessages.Returns.PermissionDenied);
	}

	/// <summary>
	/// <c>cmdlink(&lt;string&gt;, &lt;command&gt;[, &lt;hint&gt;])</c> — SharpMUSH's own: &lt;string&gt;
	/// as a link that runs &lt;command&gt; when clicked. Pueblo and MXP write a command link differently
	/// (<c>&lt;A XCH_CMD&gt;</c> against <c>&lt;SEND HREF&gt;</c>), so softcode cannot write one with
	/// <c>tagwrap()</c> that serves both; this names the link and each client's renderer writes it in
	/// that client's dialect, or as the bare string for a client with none. The hint, the command when
	/// not given, is the tooltip. Gated as PennMUSH gates <c>XCH_CMD</c>: a wizard or Send_OOB.
	/// </summary>
	[SharpFunction(Name = "cmdlink", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "command", "hint"])]
	public async ValueTask<CallState> CmdLink(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = args["0"].Message!;
		var command = args["1"].Message!.ToPlainText();
		var hint = args.TryGetValue("2", out var hintArg) ? hintArg.Message!.ToPlainText() : string.Empty;

		if (!await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator)))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (string.IsNullOrWhiteSpace(command))
		{
			return new CallState(text);
		}

		// A client sends the command as it stands, and a line break in it would send a second line.
		if (command.Any(char.IsControl))
		{
			return new CallState(ErrorMessages.Returns.InvalidArgument);
		}

		return new CallState(MarkupText.Wrap(
			Ansi.Create(linkUrl: command, linkKind: LinkKind.Command, linkText: hint.Length > 0 ? hint : command),
			text));
	}

	/// <summary>PennMUSH's <c>Can_Send_OOB</c> (<c>hdrs/mushdb.h</c>).</summary>
	private static async ValueTask<bool> CanSendOob(AnySharpObject executor)
		=> await executor.IsWizard() || await executor.HasPower("Send_OOB");

	/// <summary>
	/// <c>wshtml(&lt;html&gt;)</c> — an HTML fragment as markup (<see cref="HtmlFragment"/>): text nodes
	/// become the text, each element a layer over what it encloses. It sends nothing; the value is for
	/// whatever emits it, and every client reads it the way it reads <c>tagwrap()</c> output. Each element
	/// is held to the same gate: anything well-formed with Send_OOB, <see cref="TagwrapPolicy"/> without.
	///
	/// <para>What is malformed is repaired the way a browser repairs it, not refused.</para>
	///
	/// <para>PennMUSH's <c>wshtml(&lt;html&gt;, &lt;default&gt;)</c> carries a second, plain-text reading
	/// because its buffer holds raw bytes and cannot degrade a tag on its own. Markup can, so the second
	/// argument has no counterpart: the fragment's own text is what a client without HTML sees.</para>
	/// </summary>
	[SharpFunction(Name = "wshtml", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["html"])]
	public async ValueTask<CallState> WsHtml(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var html = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var privileged = await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator));

		return HtmlFragment.Parse(html, privileged ? AnyElement : TagwrapPolicy.Wrap) switch
		{
			MString markup => new CallState(markup),
			Error<string> error => new CallState(error.Value),
		};
	}

	/// <summary>
	/// The Send_OOB reading of an element: any tag, every attribute, re-encoded the way
	/// <see cref="TagwrapPolicy.Wrap(string, IReadOnlyList{HtmlAttribute})"/> re-encodes the ones it keeps.
	/// </summary>
	private static HtmlMarkup AnyElement(string tagName, IReadOnlyList<HtmlAttribute> attributes)
		=> HtmlMarkup.Tag(tagName, [.. attributes]);

	[SharpFunction(Name = "WEBSOCKET_HTML", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["html", "player"])]
	public async ValueTask<CallState> WebSocketHTML(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var htmlContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (parser.CurrentState.Arguments.TryGetValue("1", out var targetArg)
				&& await LocateService.LocateAndNotifyIfInvalidWithCallState(
					parser,
					executor,
					executor,
					targetArg.Message!.ToPlainText(),
					PlayersPreference | AbsoluteMatch) is Error<CallState> error)
		{
			return error.Value;
		}

		// TODO: Actual websocket/out-of-band HTML communication is planned for future release.
		// 
		// Full implementation requirements:
		// 1. Add websocket support to ConnectionService
		// 2. Implement HTML rendering capability detection
		// 3. Add HTML sanitization to prevent security issues
		// 4. Support rich HTML features for web-based MUSH clients
		//
		// When implemented, this will send HTML through OOB channel
		// Placeholder - returns empty string as OOB data doesn't display in-band
		return CallState.Empty;
	}
}