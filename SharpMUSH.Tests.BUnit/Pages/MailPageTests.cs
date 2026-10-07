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
using SharpMUSH.Library.DiscriminatedUnions;
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
			.AddSingleton(Substitute.For<IAccountAuthState>())
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(sp => new MailService(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IAccountAuthState>()));

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

		await cut.Find(".mail-row").ClickAsync();

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
	private SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake _mail = default!;

	private IRenderedComponent<SharpMUSH.Client.Pages.Mail> RenderAt(string path)
	{
		_mail = SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo(path);
		var cut = Render<SharpMUSH.Client.Pages.Mail>();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mail-row").Count == 0) throw new InvalidOperationException("rows not rendered yet");
		}, TimeSpan.FromSeconds(5));
		return cut;
	}

	private static async Task Select(IRenderedComponent<SharpMUSH.Client.Pages.Mail> cut, int row)
	{
		await cut.FindAll(".mail-row")[row].ClickAsync();
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

	/// <summary>
	/// Switching character on the Mail page lists the new character's messages, and the message that was
	/// open — the previous character's — is put away. The page reloaded only when the terminal
	/// reconnected, which a character switch does not do.
	/// </summary>
	[Test]
	public async Task SwitchingCharacter_ListsTheNewCharactersMessages()
	{
		var cut = RenderAt("/mail");
		await Select(cut, 0);

		_mail.SwitchCharacter();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mail-row").Count != 1) throw new InvalidOperationException("still the previous character's list");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".mail-row").TextContent).Contains("Second bell");
		await Assert.That(cut.Find(".mail-reading").TextContent).DoesNotContain(SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Body);
	}

	/// <summary>
	/// Each row carries the sender's avatar, the same initials on the same tint as the reading pane's
	/// header, ahead of the sender and subject.
	/// </summary>
	[Test]
	public async Task EachRow_ShowsTheSendersAvatar()
	{
		var cut = RenderAt("/mail");
		var row = cut.FindAll(".mail-row")[0];
		var avatar = row.QuerySelector(".mail-row-avatar")!;
		await Assert.That(avatar.TextContent).IsEqualTo(SharpMUSH.Client.Components.Kit.Initials.From("Tomas Reyes"));
		await Assert.That(avatar.GetAttribute("aria-hidden")).IsEqualTo("true");
		await Assert.That(row.FirstElementChild).IsSameReferenceAs(avatar);

		await Select(cut, 0);
		await Assert.That(cut.Find(".mail-reading-avatar").GetAttribute("style")).IsEqualTo(avatar.GetAttribute("style"));
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
		await Select(cut, 0);
		await Assert.That(cut.Find(".mail-reading-actions a.mail-reply").GetAttribute("href"))
			.IsEqualTo("/mail/compose?to=Tomas%20Reyes&subject=Re%3A%20The%20ledger");
	}

	[Test]
	public async Task Forward_OpensComposeWithTheSubject_AndHandsTheBodyOverOutOfTheAddress()
	{
		var cut = RenderAt("/mail");
		await Select(cut, 0);
		var nav = Services.GetRequiredService<BunitNavigationManager>();

		await cut.Find(".mail-reading-actions button.mail-forward").ClickAsync();

		var uri = new Uri(nav.Uri);
		await Assert.That(uri.AbsolutePath).IsEqualTo("/mail/compose");
		await Assert.That(uri.Query).StartsWith("?subject=Fwd%3A%20The%20ledger&draft=");
		await Assert.That(Uri.UnescapeDataString(nav.Uri)).DoesNotContain(SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Body)
			.Because("a long quoted message in the address hits URL limits and lands in history and logs");
		await Assert.That(nav.Uri).DoesNotContain("to=").Because("a forward is addressed by the reader, not to the sender");

		var compose = Render<SharpMUSH.Client.Pages.MailCompose>();
		await Assert.That(compose.Find("textarea").GetAttribute("value") ?? compose.Find("textarea").TextContent)
			.Contains(SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Body);
	}

	/// <summary>A draft is handed over once: a second visit to the same address opens compose without it.</summary>
	[Test]
	public async Task AForwardedDraft_IsTakenOnce()
	{
		SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		var mail = Services.GetRequiredService<MailService>();
		var id = mail.StageDraft("quoted text");

		await Assert.That(mail.TakeDraft(id) is string body && body == "quoted text").IsTrue();
		await Assert.That(mail.TakeDraft(id) is NotFound).IsTrue();
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

	/// <summary>
	/// A message's number names it only within one character's mailbox, so switching character on
	/// /mail/{n} shows the new character's message n, not the previous character's message kept on
	/// screen under the new one.
	/// </summary>
	[Test]
	public async Task Detail_SwitchingCharacter_ReadsTheNewCharactersMessage()
	{
		var fake = SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/mail/1");
		var cut = Render<SharpMUSH.Client.Pages.MailDetail>(p => p.Add(x => x.Id, 1));
		cut.WaitForAssertion(() => cut.Find(".md-body"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-page-head .kit-page-title").TextContent).IsEqualTo("The ledger");

		fake.SwitchCharacter();

		cut.WaitForAssertion(() =>
		{
			if (cut.Find(".kit-page-head .kit-page-title").TextContent != "Second bell") throw new InvalidOperationException("still the previous character's message");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".md-from").TextContent).Contains("Mara Quill");
	}

	/// <summary>
	/// While the new character's message is being read, the previous one's is not on screen to act on.
	/// The page kept it — subject, Reply and Delete — until the read answered, and Delete there would
	/// have removed the new character's message of the same number.
	/// </summary>
	[Test]
	public async Task Detail_SwitchingCharacter_PutsThePreviousMessageAwayAtOnce()
	{
		var fake = SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/mail/1");
		var cut = Render<SharpMUSH.Client.Pages.MailDetail>(p => p.Add(x => x.Id, 1));
		cut.WaitForAssertion(() => cut.Find("button.md-delete"), TimeSpan.FromSeconds(5));
		fake.HoldReads = new TaskCompletionSource();

		fake.SwitchCharacter();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("button.md-delete").Count > 0) throw new InvalidOperationException("the previous character's Delete is still on screen");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("a.md-reply").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).DoesNotContain("The ledger");
		fake.HoldReads.SetResult();
		cut.WaitForAssertion(() => cut.Find("button.md-delete"), TimeSpan.FromSeconds(5));
	}

	/// <summary>The Mail page puts the previous character's open message away at once too.</summary>
	[Test]
	public async Task SwitchingCharacter_PutsThePreviousMessageAwayAtOnce()
	{
		var cut = RenderAt("/mail");
		await Select(cut, 0);
		_mail.HoldLists = new TaskCompletionSource();

		_mail.SwitchCharacter();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mail-reading-actions").Count > 0) throw new InvalidOperationException("the previous character's message is still open");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).DoesNotContain(SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Body);
		_mail.HoldLists.SetResult();
	}

	[Test]
	public async Task Detail_Delete_ReturnsToTheFolderTheMessageWasIn()
	{
		SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		var nav = Services.GetRequiredService<BunitNavigationManager>();
		nav.NavigateTo("/mail/1?folder=SENT");
		var cut = Render<SharpMUSH.Client.Pages.MailDetail>(p => p.Add(x => x.Id, 1));
		cut.WaitForAssertion(() => cut.Find("button.md-delete"), TimeSpan.FromSeconds(5));

		await cut.Find("button.md-delete").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith("/mail?folder=SENT", StringComparison.Ordinal)) throw new InvalidOperationException($"still at {nav.Uri}");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(nav.Uri).EndsWith("/mail?folder=SENT").Because("deleting from Sent used to land on the inbox");
	}

	[Test]
	public async Task Compose_UsesThePlainHeader_AndTakesToAndSubjectFromTheAddress()
	{
		SharpMUSH.Tests.BUnit.Components.Mail.MailApiFake.Install(this);
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/mail/compose?to=Wren&subject=Fwd%3A%20x&body=ignored");
		var cut = Render<SharpMUSH.Client.Pages.MailCompose>();
		await Assert.That(cut.Find(".kit-page-head .kit-page-title").TextContent).IsEqualTo("ComposeMail");
		await Assert.That(cut.Markup).DoesNotContain("ignored").Because("the body only arrives as a staged draft");
	}
}
