using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Mail;

/// <summary>
/// The slice of <c>api/mail</c> the Mail section reads: two folders, an INBOX with two unread
/// messages and one read, a SENT folder with one. Reading a message marks it read, as the server does.
/// </summary>
public sealed class MailApiFake : HttpMessageHandler
{
	private readonly List<MailService.MailSummary> _inbox =
	[
		new(1, "Tomas Reyes", "The ledger", DateTimeOffset.UnixEpoch, Read: false, Urgent: false, "INBOX"),
		new(2, "Wren Halloway", "Calendar of Feasts", DateTimeOffset.UnixEpoch, Read: false, Urgent: true, "INBOX"),
		new(3, "Ilsa Varn", "Lanterns", DateTimeOffset.UnixEpoch, Read: true, Urgent: false, "INBOX"),
	];

	private readonly List<MailService.MailSummary> _sent =
	[
		new(1, "Ilsa Varn", "Re: The ledger", DateTimeOffset.UnixEpoch, Read: true, Urgent: false, "SENT"),
	];

	public const string Body = "Meet me by the second bell.";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		var query = request.RequestUri.Query;

		if (request.Method == HttpMethod.Get && path == "api/mail/folders") return Task.FromResult(Json(new[] { "INBOX", "SENT" }));
		if (request.Method == HttpMethod.Get && path == "api/mail") return Task.FromResult(Json(query.Contains("folder=SENT") ? _sent : _inbox));

		var parts = path.Split('/');
		if (parts is ["api", "mail", var folderName, var numberText] && int.TryParse(numberText, out var number))
		{
			var folder = folderName == "SENT" ? _sent : folderName == "INBOX" ? _inbox : null;
			var index = folder?.FindIndex(m => m.Number == number) ?? -1;
			if (folder is not null && index >= 0)
			{
				var row = folder[index];
				if (request.Method == HttpMethod.Delete)
				{
					folder.RemoveAt(index);
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
				}

				folder[index] = row with { Read = true };
				return Task.FromResult(Json(new MailService.MailMessage(row.Number, row.From, row.Subject, Body, row.DateSent, row.Urgent, true, folderName)));
			}
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}

	private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

	/// <summary>Registers the fake behind a real <see cref="MailService"/>, a terminal in the given state, and the rest.</summary>
	public static ITerminalService Install(TrackingBunitContext ctx, bool connected = true)
	{
		var client = ctx.Track(new HttpClient(new MailApiFake()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(connected);
		ctx.Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(terminal)
			.AddSingleton(new MailService(factory))
			.AddSingleton<SidebarCollapseService>()
			.AddEchoLocalizer();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.AddAuthorization().SetAuthorized("headwiz");
		return terminal;
	}
}
