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

	[SharpCommand(Name = "@RESPOND", Switches = ["HEADER", "TYPE"], Behavior = CB.Default | CB.NoGagged | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Respond(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}