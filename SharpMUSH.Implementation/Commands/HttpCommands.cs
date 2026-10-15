using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@HTTP",
		Switches = ["DELETE", "POST", "PUT", "GET", "HEAD", "CONNECT", "OPTIONS", "TRACE", "PATCH"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged | CB.NoGuest, MinArgs = 0, MaxArgs = 4, ParameterNames = ["url", "code"])]
	public async ValueTask<Option<CallState>> Http(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;

		parser.CurrentState.Arguments.TryGetValue("0", out var objAttrArg);
		parser.CurrentState.Arguments.TryGetValue("1", out var uriArg);
		parser.CurrentState.Arguments.TryGetValue("2", out var dataArg);
		parser.CurrentState.Arguments.TryGetValue("3", out var headersArg);

		if (!await executor.IsWizard() && !await executor.HasPower("CAN_HTTP"))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		if (objAttrArg is null)
		{
			await NotifyService.Notify(executor, "What do you want to query?", executor);
			return new CallState(ErrorMessages.Returns.WhatDoYouWantToQuery);
		}

		if (uriArg is null)
		{
			await NotifyService.Notify(executor, "Query where?", executor);
			return new CallState(ErrorMessages.Returns.QueryWhere);
		}

		var objAttrStr = objAttrArg.Message.ToPlainText();
		if (HelperFunctions.SplitObjectAndAttr(objAttrStr) is not { Object: var targetObjRef, Attribute: var attrName })
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.InvalidObjectAttribute, executor);
			return new CallState(ErrorMessages.Returns.InvalidObjectAttribute);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetObjRef,
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

				if (!Uri.TryCreate(uriArg.Message.ToPlainText(), UriKind.Absolute, out var uri))
				{
					await NotifyService.Notify(executor, "Invalid URI format.", executor);
					return new CallState(ErrorMessages.Returns.InvalidUriFormat);
				}

				// A GET has no body, so its headers follow the URL; every other verb takes them after the data.
				var isGet = method == HttpMethod.Get;
				var headersText = (isGet ? dataArg : headersArg)?.Message.ToPlainText();
				if (ParseRequestHeaders(headersText) is not RequestHeaders headers)
				{
					await NotifyService.Notify(executor,
						"Headers must be a JSON object of names and string values. Aborting.", executor);
					return new CallState(ErrorMessages.Returns.InvalidHttpHeader);
				}

				// PennMUSH reads content-type and userpass from the caller's q-registers. They outlive the
				// code that set them, so only a game that keeps PennMUSH's registers (http_qregisters) reads them;
				// elsewhere both come in the headers argument.
				var qRegisters = Configuration.CurrentValue.Compatibility.HttpQRegisters;
				var registers = qRegisters && parser.CurrentState.Registers.TryPeek(out var top) ? top : [];
				var contentType = headersText is not null
					? headers.ContentType
					: registers.GetValueOrDefault("CONTENT-TYPE")?.ToPlainText();
				var userPass = registers.GetValueOrDefault("USERPASS")?.ToPlainText();

				var requestUri = uri;
				var requestBody = isGet ? null : dataArg?.Message.ToPlainText();
				var outputLimit = parser.CurrentState.OutputLimit;
				var dbRefAttribute = new DbRefAttribute(found.Object()!.DBRef, attrName.Split("`"));
				// The callback's %> and %| are the ones at admission, like any queued entry's; the state below is built later.
				var queuedOutput = parser.CurrentState.PipedOutput;
				var queuedPrinted = parser.CurrentState.PrintedOutput;

				var admission = await Mediator.Send(new AdmitAttributeRequest(
					async () =>
					{
						var client = HttpClientFactory.CreateClient("api");

						using var message = BuildRequest(method, requestUri, requestBody, contentType, userPass, headers);
						using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
							ExecutionBudget.CurrentToken);

						var body = await ReadResponseAsync(response.Content, outputLimit, ExecutionBudget.CurrentToken);
						if (body.Overflowed)
							await NotifyService.Notify(found, "Too much HTTP data received; excess truncated.", found);

						var status = MarkupText.Plain(((int)response.StatusCode).ToString());
						var responseType = MarkupText.Plain(response.Content.Headers.ContentType?.ToString() ?? string.Empty);
						var overflow = MarkupText.Plain(body.Overflowed ? "1" : "0");
						// PennMUSH hands these over as q-registers; a PennMUSH import keeps them with http_qregisters.
						if (qRegisters)
						{
							parser.CurrentState.AddRegister("STATUS", status);
							parser.CurrentState.AddRegister("CONTENT-TYPE", responseType);
							parser.CurrentState.AddRegister("HTTP-OVERFLOW", overflow);
						}

						var contentDict = new Dictionary<string, CallState>(StringComparer.OrdinalIgnoreCase)
						{
							["0"] = new CallState(MarkupText.Plain(body.Text)),
							["status"] = new CallState(status),
							["content-type"] = new CallState(responseType),
							["http-overflow"] = new CallState(overflow)
						};

						return parser.CurrentState with
						{
							Arguments = contentDict,
							EnvironmentRegisters = contentDict,
							QueuedOutput = queuedOutput,
							QueuedPrinted = queuedPrinted
						};
					},
					dbRefAttribute, parser.CurrentState.Executor, parser.CurrentState.Enactor), ExecutionBudget.CurrentToken);

				if (!admission.Accepted)
				{
					// Capacity and shutdown failures are already reported by the scheduler.
					// Preflight failures return before that generic reporting boundary.
					if (admission.Reason is SharpMUSH.Library.Models.SchedulerModels.QueueRejectionReason.InvalidTarget
						or SharpMUSH.Library.Models.SchedulerModels.QueueRejectionReason.AlreadyReleased)
						await NotifyService.Notify(executor, admission.Error, executor);
					return new CallState(admission.Error);
				}

				return CallState.Empty;
			});
	}

	/// <summary>The headers argument of <c>@http</c>: its headers in order, less Content-Type, which sets the body's type.</summary>
	private sealed record RequestHeaders(List<(string Name, string Value)> Headers, string? ContentType);

	/// <summary>What <c>@http</c> read of a response: the text up to the output limit, and whether there was more.</summary>
	private readonly record struct ResponseBody(string Text, bool Overflowed);

	/// <summary>
	/// Reads the headers argument: a JSON object whose members are header names, each with a string value or an
	/// array of strings (the header sent once per string). Anything else fails the whole argument, as do the
	/// names and values <see cref="CheckHeaders"/> refuses. Empty text sends no headers.
	/// </summary>
	private static Result<RequestHeaders> ParseRequestHeaders(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return new RequestHeaders([], null);

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(text);
		}
		catch (JsonException)
		{
			return new Error<string>(text);
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object) return new Error<string>(text);

			var pairs = new List<(string Name, string Value)>();
			foreach (var member in document.RootElement.EnumerateObject())
			{
				switch (member.Value.ValueKind)
				{
					case JsonValueKind.String:
						pairs.Add((member.Name, member.Value.GetString()!.Trim()));
						break;
					case JsonValueKind.Array when member.Value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String):
						pairs.AddRange(member.Value.EnumerateArray().Select(v => (member.Name, v.GetString()!.Trim())));
						break;
					default:
						return new Error<string>(member.Name);
				}
			}

			return CheckHeaders(pairs);
		}
	}

	/// <summary>
	/// Fails on a name that is not an HTTP token, a value holding a control character, or a Content-Length
	/// header (the body sets that); takes the Content-Type header out as the body's type.
	/// </summary>
	private static Result<RequestHeaders> CheckHeaders(List<(string Name, string Value)> pairs)
	{
		var accepted = new List<(string Name, string Value)>();
		string? contentType = null;
		foreach (var (name, value) in pairs)
		{
			if (name.Length == 0 || !name.All(IsTokenChar) || value.Any(c => char.IsControl(c) && c != '\t')
				|| name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
				return new Error<string>(name);

			if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
				contentType = value;
			else
				accepted.Add((name, value));
		}

		return new RequestHeaders(accepted, contentType);

		// RFC 9110 token characters.
		static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c);
	}

	/// <summary>
	/// The request <c>@http</c> sends. A body goes out in the charset its content type names (UTF-8 when it names
	/// none), as <c>application/x-www-form-urlencoded</c> unless a content type is given. A header replaces
	/// the engine's own User-Agent and the <c>userpass</c> Authorization; a body header on a request with no body is dropped.
	/// </summary>
	private static HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, string? body, string? contentType,
		string? userPass, RequestHeaders headers)
	{
		var message = new HttpRequestMessage(method, uri);
		message.Headers.TryAddWithoutValidation("User-Agent", "SharpMUSH");

		if (!string.IsNullOrEmpty(body))
		{
			var type = string.IsNullOrEmpty(contentType) ? "application/x-www-form-urlencoded" : contentType;
			var charset = MediaTypeHeaderValue.TryParse(type, out var parsed) ? parsed.CharSet : null;
			message.Content = new ByteArrayContent(CharsetEncoding(charset).GetBytes(body));
			message.Content.Headers.TryAddWithoutValidation("Content-Type", type);
		}

		if (!string.IsNullOrEmpty(userPass))
			message.Headers.Authorization = new AuthenticationHeaderValue("Basic",
				Convert.ToBase64String(Encoding.UTF8.GetBytes(userPass)));

		foreach (var (name, _) in headers.Headers)
		{
			if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
				message.Headers.Remove(name);
		}

		foreach (var (name, value) in headers.Headers)
		{
			if (!message.Headers.TryAddWithoutValidation(name, value))
				message.Content?.Headers.TryAddWithoutValidation(name, value);
		}

		return message;
	}

	/// <summary>
	/// Reads the response body in the charset it declares (UTF-8 when it declares none or one this runtime does
	/// not know), stopping at <paramref name="limit"/> characters, the most one value may hold here.
	/// </summary>
	private static async ValueTask<ResponseBody> ReadResponseAsync(HttpContent content, int limit, CancellationToken token)
	{
		await using var stream = await content.ReadAsStreamAsync(token);
		using var reader = new StreamReader(stream, CharsetEncoding(content.Headers.ContentType?.CharSet),
			detectEncodingFromByteOrderMarks: true);
		var text = new StringBuilder();
		var buffer = new char[8192];
		int read;
		while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
		{
			if (text.Length + read > limit)
			{
				text.Append(buffer, 0, limit - text.Length);
				return new ResponseBody(text.ToString(), true);
			}

			text.Append(buffer, 0, read);
		}

		return new ResponseBody(text.ToString(), false);
	}

	private static Encoding CharsetEncoding(string? charset)
	{
		if (string.IsNullOrWhiteSpace(charset)) return new UTF8Encoding(false);
		try
		{
			return Encoding.GetEncoding(charset.Trim('"'));
		}
		catch (ArgumentException)
		{
			return new UTF8Encoding(false);
		}
	}

	[SharpCommand(Name = "@RESPOND", Switches = ["HEADER", "TYPE"], Behavior = CB.Default | CB.NoGagged | CB.EqSplit,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["connection", "response"])]
	public async ValueTask<Option<CallState>> Respond(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var httpResponse = parser.CurrentState.HttpResponse;

		var isHttpContext = httpResponse is not null;

		var hasTypeSwitch = switches.Contains("TYPE");
		var hasHeaderSwitch = switches.Contains("HEADER");

		if (hasTypeSwitch)
		{
			parser.CurrentState.Arguments.TryGetValue("0", out var contentTypeArg);
			if (contentTypeArg is null || string.IsNullOrWhiteSpace(contentTypeArg.Message.ToPlainText()))
			{
				await NotifyService.Notify(executor, "Content-Type cannot be empty.", executor);
				return new CallState(ErrorMessages.Returns.ContentTypeCannotBeEmpty);
			}

			var contentType = contentTypeArg.Message.ToPlainText();

			if (isHttpContext)
			{
				httpResponse!.ContentType = contentType;
			}
			else
			{
				await NotifyService.Notify(executor, $"(HTTP): Content-Type set to {contentType}", executor);
			}
		}
		else if (hasHeaderSwitch)
		{
			// With EqSplit, arg 0 is header name, arg 1 is header value
			parser.CurrentState.Arguments.TryGetValue("0", out var headerNameArg);
			parser.CurrentState.Arguments.TryGetValue("1", out var headerValueArg);

			if (headerNameArg is null)
			{
				await NotifyService.Notify(executor, "Header required.", executor);
				return new CallState(ErrorMessages.Returns.HeaderRequired);
			}

			var headerName = headerNameArg.Message.ToPlainText()?.Trim() ?? string.Empty;
			var headerValue = headerValueArg?.Message.ToPlainText()?.Trim() ?? string.Empty;

			if (string.IsNullOrWhiteSpace(headerName))
			{
				await NotifyService.Notify(executor, "Header name cannot be empty.", executor);
				return new CallState(ErrorMessages.Returns.HeaderNameCannotBeEmpty);
			}

			if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
			{
				await NotifyService.Notify(executor, "Cannot set Content-Length header.", executor);
				return new CallState(ErrorMessages.Returns.CannotSetContentLengthHeader);
			}

			if (isHttpContext)
			{
				httpResponse!.Headers.Add((headerName, headerValue));
			}
			else
			{
				await NotifyService.Notify(executor, $"(HTTP): Header {headerName}: {headerValue}", executor);
			}
		}
		else
		{
			// With EqSplit, the entire "code text" comes in arg[0], need to manually split on first space
			parser.CurrentState.Arguments.TryGetValue("0", out var statusArg);

			if (statusArg is null)
			{
				await NotifyService.Notify(executor, "Status code required.", executor);
				return new CallState(ErrorMessages.Returns.StatusCodeRequired);
			}

			var fullStatusText = statusArg.Message.ToPlainText();

			// PennMUSH-exact validation (src/cmds.c cmd_respond, oracle-verified): exactly three
			// digits, a space, then text whose first character is alphanumeric. The text is
			// REQUIRED — `@respond 500` alone is rejected, as is `@respond 200 "quoted"` (the
			// quote is not alphanumeric). All characters must be ASCII. The error messages match
			// PennMUSH verbatim because they leak into the HTTP response body via output capture.
			if (fullStatusText.Length < 5
					|| !char.IsAsciiDigit(fullStatusText[0])
					|| !char.IsAsciiDigit(fullStatusText[1])
					|| !char.IsAsciiDigit(fullStatusText[2])
					|| fullStatusText[3] != ' '
					|| !char.IsAsciiLetterOrDigit(fullStatusText[4])
					|| fullStatusText.Any(c => !char.IsAscii(c)))
			{
				await NotifyService.Notify(executor, "@respond must be 3 digits, space, then text .", executor);
				return new CallState(ErrorMessages.Returns.StatusCodeMustBe3Digit);
			}

			var statusLine = fullStatusText;

			if (statusLine.Length >= 40)
			{
				await NotifyService.Notify(executor, "@respond status code too long.", executor);
				return new CallState(ErrorMessages.Returns.StatusLineTooLong);
			}

			if (isHttpContext)
			{
				httpResponse!.StatusLine = statusLine;
			}
			else
			{
				await NotifyService.Notify(executor, $"(HTTP): Status {statusLine}", executor);
			}
		}

		return CallState.Empty;
	}
}