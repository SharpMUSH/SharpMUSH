using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.LocateFlags;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "html", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly)]
	public static ValueTask<CallState> html(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// TODO: This probably needs to be more complex than that.
		return new ValueTask<CallState>(new CallState(
			MModule.concat(
				MModule.concat(
					MModule.single("<"),
					parser.CurrentState.Arguments["0"].Message),
				MModule.single(">"))));
	}

	[SharpFunction(Name = "tag", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> tag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var tagName = args["0"].Message!.ToPlainText();
		
		// Start building the tag
		var result = new System.Text.StringBuilder();
		result.Append('<');
		result.Append(tagName);
		
		// Process attributes as key-value pairs
		// Arguments after the tag name come in pairs: attr1, val1, attr2, val2, ...
		for (int i = 1; i < args.Count; i += 2)
		{
			var attrName = args[i.ToString()].Message!.ToPlainText();
			
			// If we have a value for this attribute
			if (i + 1 < args.Count)
			{
				var attrValue = args[(i + 1).ToString()].Message!.ToPlainText();
				result.Append(' ');
				result.Append(attrName);
				result.Append("=\"");
				result.Append(attrValue);
				result.Append('"');
			}
			else
			{
				// Odd number of arguments after tag name - just add the attribute without a value
				result.Append(' ');
				result.Append(attrName);
			}
		}
		
		result.Append('>');
		
		return ValueTask.FromResult(new CallState(result.ToString()));
	}
	[SharpFunction(Name = "endtag", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> endtag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var tagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return ValueTask.FromResult(new CallState($"</{tagName}>"));
	}
	[SharpFunction(Name = "tagwrap", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> tagwrap(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var tagName = args["0"].Message!.ToPlainText();
		var content = args["1"].Message!;
		
		var result = new System.Text.StringBuilder();
		
		// Build opening tag
		result.Append('<');
		result.Append(tagName);
		
		// Add attributes if provided
		if (args.Count > 2)
		{
			var attributes = args["2"].Message!.ToPlainText();
			if (!string.IsNullOrEmpty(attributes))
			{
				result.Append(' ');
				result.Append(attributes);
			}
		}
		
		result.Append('>');
		
		// Add content (preserving formatting)
		result.Append(content.ToPlainText());
		
		// Add closing tag
		result.Append("</");
		result.Append(tagName);
		result.Append('>');
		
		return ValueTask.FromResult(new CallState(result.ToString()));
	}

	[SharpFunction(Name = "wsjson", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> websocket_json(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// wsjson() sends JSON data out-of-band (via websocket/GMCP/etc)
		// First argument is the JSON content to send
		// Second optional argument is the player/target (defaults to enactor)
		
		var jsonContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		AnySharpObject target;
		
		if (parser.CurrentState.Arguments.ContainsKey("1"))
		{
			// Target specified - locate the player
			var targetRef = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser, 
				executor, 
				executor, 
				targetRef, 
				LocateFlags.Players | LocateFlags.PreferConnected);
			
			if (locateResult.IsError)
			{
				return new CallState(locateResult.AsError);
			}
			
			target = locateResult.AsOk;
		}
		else
		{
			target = executor;
		}
		
		// TODO: Implement actual websocket/out-of-band communication
		// For now, this is a placeholder that sends the JSON as a regular notification
		// In a full implementation, this would:
		// 1. Check if the target connection supports websockets/GMCP
		// 2. Send the JSON data through the appropriate out-of-band channel
		// 3. Return empty string (since OOB data doesn't display in-band)
		
		// Placeholder: Send as notification for now
		// await NotifyService!.Notify(target, jsonContent, executor, INotifyService.NotificationType.Announce);
		
		// Return empty string - OOB data doesn't produce visible output
		return CallState.Empty;
	}

	[SharpFunction(Name = "wshtml", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> websocket_html(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// wshtml() sends HTML data out-of-band (via websocket/GMCP/etc)
		// First argument is the HTML content to send
		// Second optional argument is the player/target (defaults to enactor)
		
		var htmlContent = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		AnySharpObject target;
		
		if (parser.CurrentState.Arguments.ContainsKey("1"))
		{
			// Target specified - locate the player
			var targetRef = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser, 
				executor, 
				executor, 
				targetRef, 
				LocateFlags.Players | LocateFlags.PreferConnected);
			
			if (locateResult.IsError)
			{
				return new CallState(locateResult.AsError);
			}
			
			target = locateResult.AsOk;
		}
		else
		{
			target = executor;
		}
		
		// TODO: Implement actual websocket/out-of-band communication
		// For now, this is a placeholder that sends the HTML as a regular notification
		// In a full implementation, this would:
		// 1. Check if the target connection supports websockets/HTML
		// 2. Send the HTML data through the appropriate out-of-band channel
		// 3. Return empty string (since OOB data doesn't display in-band)
		
		// Placeholder: Send as notification for now
		// await NotifyService!.Notify(target, htmlContent, executor, INotifyService.NotificationType.Announce);
		
		// Return empty string - OOB data doesn't produce visible output
		return CallState.Empty;
	}
}