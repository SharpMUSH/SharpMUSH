using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@HTTP",
		Switches = ["DELETE", "POST", "PUT", "GET", "HEAD", "CONNECT", "OPTIONS", "TRACE", "PATCH"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged | CB.NoGuest, MinArgs = 0, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> Http(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;

		parser.CurrentState.Arguments.TryGetValue("0", out var objAttrArg);
		parser.CurrentState.Arguments.TryGetValue("1", out var uriArg);
		parser.CurrentState.Arguments.TryGetValue("2", out var dataArg);

		if (objAttrArg is null)
		{
			await NotifyService!.Notify(executor, "What do you want to query?");
			return new CallState("#-1 What do you want to query?");
		}

		if (uriArg is null)
		{
			await NotifyService!.Notify(executor, "Query where?");
			return new CallState("#-1 Query where?");
		}

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
			await NotifyService!.Notify(executor, "GET requests cannot have a body.");
			return new CallState("#-1 GET requests cannot have a body.");
		}

		if (!Uri.TryCreate(uriArg.Message?.ToPlainText() ?? string.Empty, UriKind.Absolute, out var uri))
		{
			await NotifyService!.Notify(executor, "Invalid URI format.");
			return new CallState("#-1 INVALID URI FORMAT.");
		}

		var message = new HttpRequestMessage
		{
			Headers =
			{
				{"User-Agent", "SharpMUSH"}
			},
			Method = method,
			Content = dataArg is null
				? null
				: new StringContent(dataArg.Message!.ToString()),
			RequestUri = uri
		};

		await Mediator!.Publish(new QueueAttributeRequest(
			async () =>
			{
				var client = HttpClientFactory!.CreateClient("api");
				
				var response = await client.SendAsync(message);

				parser.CurrentState.AddRegister("status",
					MModule.single(response.StatusCode.ToString()));
				parser.CurrentState.AddRegister("content-type",
					MModule.single(string.Join(" ", response.Headers.GetValues("Content-Type"))));

				var content = await response.Content.ReadAsStringAsync();

				parser.Push(parser.CurrentState with
				{
					Arguments = new Dictionary<string, CallState>
					{
						{ "0", new CallState(MModule.single(content)) }
					}
				});

				return parser.CurrentState;
			},
			new DbRefAttribute()));

		return CallState.Empty;
	}

	[SharpCommand(Name = "@RESPOND", Switches = ["HEADER", "TYPE"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2)]
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
				await NotifyService!.Notify(executor, "Content-Type cannot be empty.");
				return new CallState("#-1 CONTENT-TYPE CANNOT BE EMPTY");
			}

			var contentType = contentTypeArg.Message!.ToPlainText();

			if (isHttpContext)
			{
				httpResponse!.ContentType = contentType;
			}
			else
			{
				await NotifyService!.Notify(executor, $"(HTTP): Content-Type set to {contentType}");
			}
		}
		else if (hasHeaderSwitch)
		{
			// @respond/header <name>=<value>
			// Without EqSplit, need to manually parse the equals sign
			parser.CurrentState.Arguments.TryGetValue("0", out var headerArg);

			if (headerArg is null)
			{
				await NotifyService!.Notify(executor, "Header required.");
				return new CallState("#-1 HEADER REQUIRED");
			}

			var headerText = headerArg.Message?.ToPlainText() ?? string.Empty;
			var equalsIndex = headerText.IndexOf('=');

			if (equalsIndex < 0)
			{
				// No equals sign, treat entire thing as header name with empty value
				var headerName = headerText.Trim();
				
				if (string.IsNullOrWhiteSpace(headerName))
				{
					await NotifyService!.Notify(executor, "Header name cannot be empty.");
					return new CallState("#-1 HEADER NAME CANNOT BE EMPTY");
				}

				// Prevent setting Content-Length as per documentation
				if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
				{
					await NotifyService!.Notify(executor, "Cannot set Content-Length header.");
					return new CallState("#-1 CANNOT SET CONTENT-LENGTH HEADER");
				}

				if (isHttpContext)
				{
					httpResponse!.Headers.Add((headerName, string.Empty));
				}
				else
				{
					await NotifyService!.Notify(executor, $"(HTTP): Header {headerName}: ");
				}
			}
			else
			{
				var headerName = headerText[..equalsIndex].Trim();
				var headerValue = headerText[(equalsIndex + 1)..].Trim();

				if (string.IsNullOrWhiteSpace(headerName))
				{
					await NotifyService!.Notify(executor, "Header name cannot be empty.");
					return new CallState("#-1 HEADER NAME CANNOT BE EMPTY");
				}

				// Prevent setting Content-Length as per documentation
				if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
				{
					await NotifyService!.Notify(executor, "Cannot set Content-Length header.");
					return new CallState("#-1 CANNOT SET CONTENT-LENGTH HEADER");
				}

				if (isHttpContext)
				{
					httpResponse!.Headers.Add((headerName, headerValue));
				}
				else
				{
					await NotifyService!.Notify(executor, $"(HTTP): Header {headerName}: {headerValue}");
				}
			}
		}
		else
		{
			// @respond <code> <text>
			parser.CurrentState.Arguments.TryGetValue("0", out var statusCodeArg);
			parser.CurrentState.Arguments.TryGetValue("1", out var statusTextArg);

			if (statusCodeArg is null)
			{
				await NotifyService!.Notify(executor, "Status code required.");
				return new CallState("#-1 STATUS CODE REQUIRED");
			}

			var statusCodeText = statusCodeArg.Message?.ToPlainText() ?? string.Empty;
			var statusText = statusTextArg?.Message?.ToPlainText() ?? string.Empty;

			// Validate status code is 3 digits
			if (!int.TryParse(statusCodeText, out var statusCode) || statusCode < 100 || statusCode > 999)
			{
				await NotifyService!.Notify(executor, "Status code must be a 3-digit number.");
				return new CallState("#-1 STATUS CODE MUST BE A 3-DIGIT NUMBER");
			}

			// Build the full status line
			var statusLine = string.IsNullOrWhiteSpace(statusText) 
				? statusCodeText 
				: $"{statusCodeText} {statusText}";

			// Validate total length < 40 characters as per documentation
			if (statusLine.Length >= 40)
			{
				await NotifyService!.Notify(executor, "Status line must be less than 40 characters.");
				return new CallState("#-1 STATUS LINE MUST BE LESS THAN 40 CHARACTERS");
			}

			if (isHttpContext)
			{
				httpResponse!.StatusLine = statusLine;
			}
			else
			{
				await NotifyService!.Notify(executor, $"(HTTP): Status {statusLine}");
			}
		}

		return CallState.Empty;
	}
}