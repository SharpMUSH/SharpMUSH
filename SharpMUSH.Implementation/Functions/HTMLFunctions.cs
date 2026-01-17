using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using static SharpMUSH.Library.Services.Interfaces.LocateFlags;
using static MarkupString.MarkupImplementation;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "html", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = ["tag", "text..."])]
	public ValueTask<CallState> HTML(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Basic HTML tag wrapper - wraps content in angle brackets for simple tag generation
		return new ValueTask<CallState>(new CallState(
			MModule.concat(
				MModule.concat(
					MModule.single("<"),
					parser.CurrentState.Arguments["0"].Message),
				MModule.single(">"))));
	}

	[SharpFunction(Name = "tag", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["tagname", "content", "attributes"])]
	public ValueTask<CallState> Tag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>("#-1 USE TAGWRAP INSTEAD");

	[SharpFunction(Name = "endtag", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["tagname"])]
	public ValueTask<CallState> EndTag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>("#-1 USE TAGWRAP INSTEAD");

	[SharpFunction(Name = "tagwrap", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["tag", "content"])]
	public ValueTask<CallState> TagWrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var tagName = args["0"].Message!.ToPlainText();
		var content = args["1"].Message!;

		// Add attributes if provided
		Microsoft.FSharp.Core.FSharpOption<string>? attributes = null;
		if (args.Count > 2)
		{
			var attrText = args["2"].Message!.ToPlainText();
			if (!string.IsNullOrEmpty(attrText))
			{
				attributes = Microsoft.FSharp.Core.FSharpOption<string>.Some(attrText);
			}
		}

		// Create HTML markup structure for semantic information
		var htmlMarkup = attributes == null
			? HtmlMarkup.Create(tagName)
			: HtmlMarkup.Create(tagName,
				Microsoft.FSharp.Core.FSharpOption<Microsoft.FSharp.Core.FSharpOption<string>>.Some(attributes));

		// Return a MarkupString that contains both the HTML markup structure
		// and the actual HTML text (so it appears in both ToString() and ToPlainText())
		var wrappedContent = MModule.markupSingle2(htmlMarkup, content);

		// But for now, return the plain HTML string since tests expect it
		return ValueTask.FromResult<CallState>(wrappedContent.ToString());
	}

	[SharpFunction(Name = "wsjson", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["message"])]
	public async ValueTask<CallState> websocket_json(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// wsjson() sends JSON data out-of-band (via websocket/GMCP/etc)
		// First argument is the JSON content to send
		// Second optional argument is the player/target (defaults to enactor)

		var jsonContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		AnySharpObject target;

		if (parser.CurrentState.Arguments.ContainsKey("1"))
		{
			// Target specified - locate the player
			var targetRef = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			var locateResult = await _locateService!.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				targetRef,
				PlayersPreference | AbsoluteMatch);

			if (locateResult.IsError)
			{
				return new CallState(locateResult.AsError);
			}

			target = locateResult.AsAnyObject;
		}
		else
		{
			target = executor;
		}

		// TODO: Actual websocket/out-of-band communication is planned for future release.
		// For now, this is a placeholder that sends the JSON as a regular notification
		//
		// Full implementation requirements:
		// 1. Add websocket support to _connectionService (ws:// and wss:// protocols)
		// 2. Implement GMCP (Generic MUD Communication Protocol) support
		// 3. Add connection capability negotiation (detect websocket/GMCP support)
		// 4. Modify ConnectionData to include supported protocols/capabilities
		// 5. Route OOB data through appropriate channel based on connection type
		// 6. Support GMCP packages: Client.Media, Client.GUI, etc.
		// 7. Implement MXP (MUD eXtension Protocol) as alternative to GMCP
		//
		// When implemented:
		// - Check if target connection supports websockets/GMCP
		// - Send the JSON data through the appropriate out-of-band channel
		// - Return empty string (since OOB data doesn't display in-band)

		// Placeholder: Send as notification for now
		// await _notifyService!.Notify(target, jsonContent, executor, INotifyService.NotificationType.Announce);

		// Return empty string - OOB data doesn't produce visible output
		return CallState.Empty;
	}

	[SharpFunction(Name = "wshtml", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["html"])]
	public async ValueTask<CallState> websocket_html(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// wshtml() sends HTML data out-of-band (via websocket/GMCP/etc)
		// First argument is the HTML content to send
		// Second optional argument is the player/target (defaults to enactor)

		var htmlContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		AnySharpObject target;

		if (parser.CurrentState.Arguments.ContainsKey("1"))
		{
			// Target specified - locate the player
			var targetRef = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			var locateResult = await _locateService!.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				targetRef,
				PlayersPreference | AbsoluteMatch);

			if (locateResult.IsError)
			{
				return new CallState(locateResult.AsError);
			}

			target = locateResult.AsAnyObject;
		}
		else
		{
			target = executor;
		}

		// TODO: Actual websocket/out-of-band communication is planned for future release.
		// For now, this is a placeholder that sends the HTML as a regular notification
		//
		// Full implementation requirements:
		// 1. Add websocket support to _connectionService (ws:// and wss:// protocols)
		// 2. Implement HTML-over-websocket or MXP (MUD eXtension Protocol)
		// 3. Add connection capability negotiation (detect HTML support)
		// 4. Sanitize HTML to prevent XSS attacks (whitelist safe tags)
		// 5. Support HTML features: colors, links, images, formatting
		// 6. Implement CSP (Content Security Policy) for safety
		//
		// When implemented:
		// - Check if target connection supports websockets/HTML
		// - Sanitize and send the HTML data through the appropriate channel
		// - Return empty string (since OOB data doesn't display in-band)

		// Placeholder: Send as notification for now
		// await _notifyService!.Notify(target, htmlContent, executor, INotifyService.NotificationType.Announce);

		// Return empty string - OOB data doesn't produce visible output
		return CallState.Empty;
	}

	[SharpFunction(Name = "WEBSOCKET_HTML", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, 
		ParameterNames = ["html", "player"])]
	public async ValueTask<CallState> WebSocketHTML(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Send HTML data via websocket - similar to wshtml()
		var htmlContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		
		AnySharpObject target;
		if (parser.CurrentState.Arguments.TryGetValue("1", out var targetArg))
		{
			var targetRef = targetArg.Message!.ToPlainText();
			var locateResult = await _locateService!.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				targetRef,
				PlayersPreference | AbsoluteMatch);

			if (locateResult.IsError)
			{
				return new CallState(locateResult.AsError);
			}

			target = locateResult.AsAnyObject;
		}
		else
		{
			target = executor;
		}

		// TODO: Actual websocket/out-of-band HTML communication is planned for future release.
		// 
		// Full implementation requirements:
		// 1. Add websocket support to _connectionService
		// 2. Implement HTML rendering capability detection
		// 3. Add HTML sanitization to prevent security issues
		// 4. Support rich HTML features for web-based MUSH clients
		//
		// When implemented, this will send HTML through OOB channel
		// Placeholder - returns empty string as OOB data doesn't display in-band
		return CallState.Empty;
	}
}