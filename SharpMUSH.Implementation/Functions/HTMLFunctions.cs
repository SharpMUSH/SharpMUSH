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
	/// <para><see cref="HtmlTagPolicy"/> is the gate: without Send_OOB, only PennMUSH's tags and only
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

		if (!HtmlTagPolicy.IsTagName(tagName))
		{
			return new CallState(ErrorMessages.Returns.InvalidTagName);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var privileged = await CanSendOob(executor);
		if (!privileged && !HtmlTagPolicy.AllowedTags.Contains(tagName))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var attributes = parameters.Length == 0 ? null
			: privileged ? parameters
			: HtmlTagPolicy.Sanitize(parameters);

		return new CallState(MarkupText.Wrap(HtmlMarkup.Create(tagName, attributes), content));
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

	[SharpFunction(Name = "wsjson", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["message"])]
	public async ValueTask<CallState> websocket_json(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var jsonContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var playerStr = parser.CurrentState.Arguments.ContainsKey("1")
			? parser.CurrentState.Arguments["1"].Message!.ToPlainText()
			: "me";

		var locate = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			playerStr,
			PlayersPreference | AbsoluteMatch);

		if (locate is not AnySharpObject located)
		{
			return CallState.Empty;
		}

		if (!located.IsPlayer)
		{
			return CallState.Empty;
		}

		var isWizard = await executor.IsWizard();
		var isSelf = executor.Object().DBRef == located.Object().DBRef;

		if (!isWizard && !isSelf)
		{
			return CallState.Empty;
		}

		// Try to parse as JSON, but if it fails, send as-is
		object? dataObj;
		try
		{
			dataObj = System.Text.Json.JsonSerializer.Deserialize<object>(jsonContent);
		}
		catch (System.Text.Json.JsonException)
		{
			dataObj = jsonContent;
		}
		catch (System.NotSupportedException)
		{
			dataObj = jsonContent;
		}

		var wsMessage = System.Text.Json.JsonSerializer.Serialize(new
		{
			type = "json",
			data = dataObj
		});

		await foreach (var connection in ConnectionService.Get(located.Object().DBRef))
		{
			if (connection.ConnectionType != "websocket")
			{
				continue;
			}

			await Mediator.Publish(new SharpMUSH.Messaging.Messages.WebSocketOutputMessage(
				connection.Handle,
				wsMessage));
		}

		// Return empty string - OOB data doesn't produce visible output
		return CallState.Empty;
	}

	[SharpFunction(Name = "wshtml", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["html"])]
	public async ValueTask<CallState> websocket_html(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var htmlContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var playerStr = parser.CurrentState.Arguments.ContainsKey("1")
			? parser.CurrentState.Arguments["1"].Message!.ToPlainText()
			: "me";

		var locate = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			playerStr,
			PlayersPreference | AbsoluteMatch);

		if (locate is not AnySharpObject located)
		{
			return CallState.Empty;
		}

		if (!located.IsPlayer)
		{
			return CallState.Empty;
		}

		// Check permissions
		var isWizard = await executor.IsWizard();
		var isSelf = executor.Object().DBRef == located.Object().DBRef;

		if (!isWizard && !isSelf)
		{
			return CallState.Empty;
		}

		var wsMessage = System.Text.Json.JsonSerializer.Serialize(new
		{
			type = "html",
			data = htmlContent
		});

		await foreach (var connection in ConnectionService.Get(located.Object().DBRef))
		{
			if (connection.ConnectionType != "websocket")
			{
				continue;
			}

			await Mediator.Publish(new SharpMUSH.Messaging.Messages.WebSocketOutputMessage(
				connection.Handle,
				wsMessage));
		}

		// Return empty string - OOB data doesn't produce visible output
		return CallState.Empty;
	}

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