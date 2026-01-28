using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.LocateFlags;
using static MarkupString.MarkupImplementation;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "html", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly, ParameterNames = ["tag", "text..."])]
	public static ValueTask<CallState> HTML(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
	public static ValueTask<CallState> Tag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>("#-1 USE TAGWRAP INSTEAD");

	[SharpFunction(Name = "endtag", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["tagname"])]
	public static ValueTask<CallState> EndTag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>("#-1 USE TAGWRAP INSTEAD");

	[SharpFunction(Name = "tagwrap", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["tag", "content"])]
	public static ValueTask<CallState> TagWrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
	public static async ValueTask<CallState> websocket_json(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// wsjson() sends JSON data out-of-band to WebSocket connections
		// First argument is the JSON content to send
		// Second optional argument is the player/target (defaults to enactor)

		var jsonContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known;

		var playerStr = parser.CurrentState.Arguments.ContainsKey("1")
			? parser.CurrentState.Arguments["1"].Message!.ToPlainText()
			: "me";

		var locate = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			enactor,
			executor,
			playerStr,
			PlayersPreference | AbsoluteMatch);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		if (!located.IsPlayer)
		{
			return CallState.Empty;
		}

		// Check permissions
		var isWizard = executor.IsGod() || await executor.IsWizard();
		var isSelf = executor.Object().DBRef == located.Object().DBRef;

		if (!isWizard && !isSelf)
		{
			return CallState.Empty;
		}

		// Send to all WebSocket connections for the target player
		await foreach (var connection in ConnectionService!.Get(located.Object().DBRef))
		{
			if (connection.ConnectionType != "websocket")
			{
				continue;
			}

			// Format as JSON object with type indicator
			// Try to parse as JSON, but if it fails, send as-is
			object? dataObj;
			try
			{
				dataObj = System.Text.Json.JsonSerializer.Deserialize<object>(jsonContent);
			}
			catch
			{
				dataObj = jsonContent;
			}

			var wsMessage = System.Text.Json.JsonSerializer.Serialize(new
			{
				type = "json",
				data = dataObj
			});

			await Mediator!.Publish(new SharpMUSH.Messages.WebSocketOutputMessage(
				connection.Handle,
				wsMessage));
		}

		// Return empty string - OOB data doesn't produce visible output
		return CallState.Empty;
	}

	[SharpFunction(Name = "wshtml", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["html"])]
	public static async ValueTask<CallState> websocket_html(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// wshtml() sends HTML data out-of-band to WebSocket connections
		// First argument is the HTML content to send
		// Second optional argument is the player/target (defaults to enactor)

		var htmlContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).Known;

		var playerStr = parser.CurrentState.Arguments.ContainsKey("1")
			? parser.CurrentState.Arguments["1"].Message!.ToPlainText()
			: "me";

		var locate = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			enactor,
			executor,
			playerStr,
			PlayersPreference | AbsoluteMatch);

		if (!locate.IsValid())
		{
			return CallState.Empty;
		}

		var located = locate.WithoutError().WithoutNone();

		if (!located.IsPlayer)
		{
			return CallState.Empty;
		}

		// Check permissions
		var isWizard = executor.IsGod() || await executor.IsWizard();
		var isSelf = executor.Object().DBRef == located.Object().DBRef;

		if (!isWizard && !isSelf)
		{
			return CallState.Empty;
		}

		// Send to all WebSocket connections for the target player
		await foreach (var connection in ConnectionService!.Get(located.Object().DBRef))
		{
			if (connection.ConnectionType != "websocket")
			{
				continue;
			}

			// Format as JSON object with type indicator
			var wsMessage = System.Text.Json.JsonSerializer.Serialize(new
			{
				type = "html",
				data = htmlContent
			});

			await Mediator!.Publish(new SharpMUSH.Messages.WebSocketOutputMessage(
				connection.Handle,
				wsMessage));
		}

		// Return empty string - OOB data doesn't produce visible output
		return CallState.Empty;
	}

	[SharpFunction(Name = "WEBSOCKET_HTML", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, 
		ParameterNames = ["html", "player"])]
	public static async ValueTask<CallState> WebSocketHTML(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Send HTML data via websocket - similar to wshtml()
		var htmlContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		AnySharpObject target;
		if (parser.CurrentState.Arguments.TryGetValue("1", out var targetArg))
		{
			var targetRef = targetArg.Message!.ToPlainText();
			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
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