using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@HTTP",
		Switches = ["DELETE", "POST", "PUT", "GET", "HEAD", "CONNECT", "OPTIONS", "TRACE", "PATCH"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged | CB.NoGuest, MinArgs = 0, MaxArgs = 3, ParameterNames = ["url", "code"])]
	public static async ValueTask<Option<CallState>> Http(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;

		parser.CurrentState.Arguments.TryGetValue("0", out var objAttrArg);
		parser.CurrentState.Arguments.TryGetValue("1", out var uriArg);
		parser.CurrentState.Arguments.TryGetValue("2", out var dataArg);

		if (objAttrArg is null)
		{
			await NotifyService!.Notify(executor, "What do you want to query?", executor);
			return new CallState("#-1 What do you want to query?");
		}

		if (uriArg is null)
		{
			await NotifyService!.Notify(executor, "Query where?", executor);
			return new CallState("#-1 Query where?");
		}

		var objAttrStr = objAttrArg.Message?.ToPlainText() ?? string.Empty;
		var maybeObjAttr = HelperFunctions.SplitObjectAndAttr(objAttrStr);
		if (maybeObjAttr.IsT1)
		{
			await NotifyService!.Notify(executor, "#-1 INVALID OBJECT/ATTRIBUTE", executor);
			return new CallState("#-1 INVALID OBJECT/ATTRIBUTE");
		}

		var (targetObjRef, attrName) = maybeObjAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetObjRef,
			LocateFlags.All,
			async found =>
			{
				var method = switches.FirstOrDefault() switch
				{
					"DELETE" => HttpMethod.Delete,
					"POST" => HttpMethod.Post,
					"PUT" => HttpMethod.Put,
					"GET" => HttpMethod.Get,
					"HEAD" => HttpMethod.Head,
					"CONNECT" => HttpMethod.Connect,
					"OPTIONS" => HttpMethod.Options,
					"TRACE" => HttpMethod.Trace,
					"PATCH" => HttpMethod.Patch,
					_ => HttpMethod.Get
				};

				if (method == HttpMethod.Get && dataArg is not null)
				{
					await NotifyService!.Notify(executor, "GET requests cannot have a body.", executor);
					return new CallState("#-1 GET requests cannot have a body.");
				}

				if (!Uri.TryCreate(uriArg.Message?.ToPlainText() ?? string.Empty, UriKind.Absolute, out var uri))
				{
					await NotifyService!.Notify(executor, "Invalid URI format.", executor);
					return new CallState("#-1 INVALID URI FORMAT.");
				}

				var requestUri = uri;
				var requestBody = dataArg?.Message?.ToString();
				var dbRefAttribute = new DbRefAttribute(found.Object()!.DBRef, attrName.Split("`"));

				await Mediator!.Send(new QueueAttributeRequest(
					async () =>
					{
						var client = HttpClientFactory!.CreateClient("api");

						using var message = new HttpRequestMessage
						{
							Headers =
							{
								{ "User-Agent", "SharpMUSH" }
							},
							Method = method,
							Content = requestBody is null
								? null
								: new StringContent(requestBody),
							RequestUri = requestUri
						};

						var response = await client.SendAsync(message);

						parser.CurrentState.AddRegister("STATUS",
							MModule.single(((int)response.StatusCode).ToString()));
						parser.CurrentState.AddRegister("CONTENT-TYPE",
							MModule.single(response.Content.Headers.ContentType?.ToString() ?? string.Empty));

						var content = await response.Content.ReadAsStringAsync();
						var contentState = new CallState(MModule.single(content));
						var contentDict = new Dictionary<string, CallState> { { "0", contentState } };

						return parser.CurrentState with
						{
							Arguments = contentDict,
							EnvironmentRegisters = contentDict
						};
					},
					dbRefAttribute));

				return CallState.Empty;
			});
	}

	[SharpCommand(Name = "@RESPOND", Switches = ["HEADER", "TYPE"], Behavior = CB.Default | CB.NoGagged | CB.EqSplit,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["connection", "response"])]
	public static async ValueTask<Option<CallState>> Respond(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches.ToList();
		var httpResponse = parser.CurrentState.HttpResponse;

		// Determine if we're in HTTP context (HttpResponse will be set by HTTP handler)
		var isHttpContext = httpResponse is not null;

		// Parse the arguments based on the switch
		var hasTypeSwitch = switches.Contains("TYPE");
		var hasHeaderSwitch = switches.Contains("HEADER");

		if (hasTypeSwitch)
		{
			// @respond/type <content-type>
			parser.CurrentState.Arguments.TryGetValue("0", out var contentTypeArg);
			if (contentTypeArg is null || string.IsNullOrWhiteSpace(contentTypeArg.Message?.ToPlainText()))
			{
				await NotifyService!.Notify(executor, "Content-Type cannot be empty.", executor);
				return new CallState("#-1 CONTENT-TYPE CANNOT BE EMPTY");
			}

			var contentType = contentTypeArg.Message!.ToPlainText();

			if (isHttpContext)
			{
				httpResponse!.ContentType = contentType;
			}
			else
			{
				await NotifyService!.Notify(executor, $"(HTTP): Content-Type set to {contentType}", executor);
			}
		}
		else if (hasHeaderSwitch)
		{
			// @respond/header <name>=<value>
			// With EqSplit, arg 0 is header name, arg 1 is header value
			parser.CurrentState.Arguments.TryGetValue("0", out var headerNameArg);
			parser.CurrentState.Arguments.TryGetValue("1", out var headerValueArg);

			if (headerNameArg is null)
			{
				await NotifyService!.Notify(executor, "Header required.", executor);
				return new CallState("#-1 HEADER REQUIRED");
			}

			var headerName = headerNameArg.Message?.ToPlainText()?.Trim() ?? string.Empty;
			var headerValue = headerValueArg?.Message?.ToPlainText()?.Trim() ?? string.Empty;

			if (string.IsNullOrWhiteSpace(headerName))
			{
				await NotifyService!.Notify(executor, "Header name cannot be empty.", executor);
				return new CallState("#-1 HEADER NAME CANNOT BE EMPTY");
			}

			// Prevent setting Content-Length as per documentation
			if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
			{
				await NotifyService!.Notify(executor, "Cannot set Content-Length header.", executor);
				return new CallState("#-1 CANNOT SET CONTENT-LENGTH HEADER");
			}

			if (isHttpContext)
			{
				httpResponse!.Headers.Add((headerName, headerValue));
			}
			else
			{
				await NotifyService!.Notify(executor, $"(HTTP): Header {headerName}: {headerValue}", executor);
			}
		}
		else
		{
			// @respond <code> <text>
			// With EqSplit, the entire "code text" comes in arg[0], need to manually split on first space
			parser.CurrentState.Arguments.TryGetValue("0", out var statusArg);

			if (statusArg is null)
			{
				await NotifyService!.Notify(executor, "Status code required.", executor);
				return new CallState("#-1 STATUS CODE REQUIRED");
			}

			var fullStatusText = statusArg.Message?.ToPlainText() ?? string.Empty;

			// Split on first space to separate code from text
			var spaceIndex = fullStatusText.IndexOf(' ');
			string statusCodeText;
			string statusText;

			if (spaceIndex > 0)
			{
				statusCodeText = fullStatusText[..spaceIndex].Trim();
				statusText = fullStatusText[(spaceIndex + 1)..].Trim();
			}
			else
			{
				statusCodeText = fullStatusText.Trim();
				statusText = string.Empty;
			}

			// Validate status code is 3 digits
			if (!int.TryParse(statusCodeText, out var statusCode) || statusCode < 100 || statusCode > 999)
			{
				await NotifyService!.Notify(executor, "Status code must be a 3-digit number.", executor);
				return new CallState("#-1 STATUS CODE MUST BE A 3-DIGIT NUMBER");
			}

			// Build the full status line
			var statusLine = string.IsNullOrWhiteSpace(statusText)
				? statusCodeText
				: $"{statusCodeText} {statusText}";

			// Validate total length < 40 characters as per documentation
			if (statusLine.Length >= 40)
			{
				await NotifyService!.Notify(executor, "Status line must be less than 40 characters.", executor);
				return new CallState("#-1 STATUS LINE MUST BE LESS THAN 40 CHARACTERS");
			}

			if (isHttpContext)
			{
				httpResponse!.StatusLine = statusLine;
			}
			else
			{
				await NotifyService!.Notify(executor, $"(HTTP): Status {statusLine}", executor);
			}
		}

		return CallState.Empty;
	}
}