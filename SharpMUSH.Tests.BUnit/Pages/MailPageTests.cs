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
public class MailPageTests : TrackingBunitContext
{
	private void Arrange()
	{
		var apiClient = Track(new HttpClient(new MailPageApiHandler()) { BaseAddress = new Uri("https://localhost:8081/") });

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(true);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(terminal)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(sp => new MailService(sp.GetRequiredService<IHttpClientFactory>()));

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
}

/// <summary>
/// D1 §6.5 Mail: the folders moved to the section sidebar, so the folder on screen comes from the
/// address; the page is the plain header over a list card and a reading card. Reply and Forward were
/// buttons with no handler — they now open the compose form filled in, as the detail page's Reply does.
/// </summary>
public class MailPageD1Tests : TrackingBunitContext
{
	private IRenderedComponent<SharpMUSH.Client.Pages.Mail> RenderAt(string path)
	{
		SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo(path);
		var cut = Render<SharpMUSH.Client.Pages.Mail>();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mail-row").Count == 0) throw new InvalidOperationException("rows not rendered yet");
		}, TimeSpan.FromSeconds(5));
		return cut;
	}

	private static void Select(IRenderedComponent<SharpMUSH.Client.Pages.Mail> cut, int row)
	{
		cut.FindAll(".mail-row")[row].Click();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Find(".mail-reading-body").TextContent.Contains(SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Body))
				throw new InvalidOperationException("body not rendered yet");
		}, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task ThePlainHeader_AListCard_AndAReadingCard()
	{
		var cut = RenderAt("/mail");
		await Assert.That(cut.Find(".kit-page-head .kit-page-title").TextContent).IsEqualTo("Mail");
		await Assert.That(cut.Find(".kit-page-head .kit-page-actions a[href='/mail/compose']")).IsNotNull();
		await Assert.That(cut.Find(".mail-list.kit-card .kit-card-title").TextContent).IsEqualTo("INBOX");
		await Assert.That(cut.FindAll(".mail-reading.kit-card").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".mail-folders").Count).IsEqualTo(0).Because("the folders live in the section sidebar now");
	}

	[Test]
	public async Task TheFolderComesFromTheAddress()
	{
		var cut = RenderAt("/mail?folder=SENT");
		await Assert.That(cut.Find(".mail-list .kit-card-title").TextContent).IsEqualTo("SENT");
		await Assert.That(cut.FindAll(".mail-row").Count).IsEqualTo(1);
		await Assert.That(cut.Find(".mail-row").TextContent).Contains("Re: The ledger");
	}

	[Test]
	public async Task Reply_OpensComposeAddressedToTheSender()
	{
		var cut = RenderAt("/mail");
		Select(cut, 0);
		await Assert.That(cut.Find(".mail-reading-actions a.mail-reply").GetAttribute("href"))
			.IsEqualTo("/mail/compose?to=Tomas%20Reyes&subject=Re%3A%20The%20ledger");
	}

	[Test]
	public async Task Forward_OpensComposeWithTheSubjectAndTheBody()
	{
		var cut = RenderAt("/mail");
		Select(cut, 0);
		var href = cut.Find(".mail-reading-actions a.mail-forward").GetAttribute("href")!;
		await Assert.That(href).StartsWith("/mail/compose?subject=Fwd%3A%20The%20ledger&body=");
		await Assert.That(Uri.UnescapeDataString(href)).Contains(SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Body);
		await Assert.That(href).DoesNotContain("to=").Because("a forward is addressed by the reader, not to the sender");
	}

	[Test]
	public async Task Detail_NamesTheMessage_AndRepliesToItsSender()
	{
		SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/mail/2");
		var cut = Render<SharpMUSH.Client.Pages.MailDetail>(p => p.Add(x => x.Id, 2));
		cut.WaitForAssertion(() => cut.Find(".md-body"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-page-head .kit-page-title").TextContent).IsEqualTo("Calendar of Feasts");
		await Assert.That(cut.Find(".kit-page-head .kit-page-kicker").TextContent).IsEqualTo("Mail · INBOX");
		await Assert.That(cut.Find(".md-head .kit-pill").TextContent.Trim()).IsEqualTo("Urgent");
		await Assert.That(cut.Find("a.md-reply").GetAttribute("href"))
			.IsEqualTo("/mail/compose?to=Wren%20Halloway&subject=Re%3A%20Calendar%20of%20Feasts");
	}

	[Test]
	public async Task Detail_Delete_ReturnsToTheFolderTheMessageWasIn()
	{
		SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		var nav = Services.GetRequiredService<BunitNavigationManager>();
		nav.NavigateTo("/mail/1?folder=SENT");
		var cut = Render<SharpMUSH.Client.Pages.MailDetail>(p => p.Add(x => x.Id, 1));
		cut.WaitForAssertion(() => cut.Find("button.md-delete"), TimeSpan.FromSeconds(5));

		cut.Find("button.md-delete").Click();
		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith("/mail?folder=SENT", StringComparison.Ordinal)) throw new InvalidOperationException($"still at {nav.Uri}");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(nav.Uri).EndsWith("/mail?folder=SENT").Because("deleting from Sent used to land on the inbox");
	}

	[Test]
	public async Task Compose_TakesTheBodyFromTheAddress()
	{
		SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/mail/compose?subject=Fwd%3A%20x&body=quoted%20text");
		var cut = Render<SharpMUSH.Client.Pages.MailCompose>();
		await Assert.That(cut.Find(".kit-page-head .kit-page-title").TextContent).IsEqualTo("ComposeMail");
		await Assert.That(cut.FindAll("textarea").Single().GetAttribute("value") ?? cut.Find("textarea").TextContent).Contains("quoted text");
	}
}
