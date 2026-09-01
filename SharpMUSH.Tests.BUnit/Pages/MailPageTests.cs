using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// The slice of <c>api/mail</c> the /mail page touches. The body is served only at the number the
/// list reported, so a page reading the wrong number 404s here rather than silently passing.
/// </summary>
file sealed class MailPageApiHandler : HttpMessageHandler
{
	public const string Subject = "Rawr Subject";
	public const string Body = "The body of the message.";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method != HttpMethod.Get)
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

		return Task.FromResult(path switch
		{
			"api/mail/folders" => Json(new[] { "INBOX" }),
			"api/mail" => Json(new[]
			{
				new
				{
					Number = 1, From = "God", Subject, DateSent = DateTimeOffset.UnixEpoch,
					Read = false, Urgent = false, Folder = "INBOX"
				}
			}),
			"api/mail/INBOX/1" => Json(new
			{
				Number = 1,
				From = "God",
				Subject,
				Body,
				DateSent = DateTimeOffset.UnixEpoch,
				Urgent = false,
				Read = true,
				Folder = "INBOX"
			}),
			_ => new HttpResponseMessage(HttpStatusCode.NotFound)
		});
	}

	private static HttpResponseMessage Json<T>(T value)
		=> new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

/// <summary>
/// Selecting a row has to put the message text on screen: <c>.mail-reading-body</c> rendered the
/// envelope over a "read full message" link and no body at all.
/// </summary>
public class MailPageTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> ownedHttpClients = [];

	private void Arrange()
	{
		var apiClient = Track(new HttpClient(new MailPageApiHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		ownedHttpClients.Add(apiClient);

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(true);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(terminal)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(sp => new MailService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<MailService>.Instance));

		this.AddAuthorization().SetAuthorized("headwiz");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task SelectingAMessage_ShowsItsBodyInTheReadingPane()
	{
		Arrange();

		var cut = Render<SharpMUSH.Client.Pages.Mail>();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mail-row").Count == 0)
				throw new InvalidOperationException("mailbox rows not rendered yet");
		});

		cut.Find(".mail-row").Click();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Find(".mail-reading-body").TextContent.Contains(MailPageApiHandler.Body))
				throw new InvalidOperationException("message body not rendered yet");
		});

		await Assert.That(cut.Find(".mail-reading-body").TextContent).Contains(MailPageApiHandler.Body);
	}

	public async ValueTask DisposeAsync()
	{
		foreach (var client in ownedHttpClients)
		{
			client.Dispose();
		}

		ownedHttpClients.Clear();
		await ValueTask.CompletedTask;
		GC.SuppressFinalize(this);
	}
}
