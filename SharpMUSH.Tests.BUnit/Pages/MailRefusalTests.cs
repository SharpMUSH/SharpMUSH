using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// When <c>api/mail</c> refuses, the mail pages say what the server said. <see cref="MailService"/>
/// used to answer with a <see langword="bool"/> or an empty list, so a send to a misspelt name
/// showed a guess ("check the recipient name") whatever the reason, and an unreachable mail store
/// looked exactly like an empty inbox.
/// </summary>
public class MailRefusalTests : TrackingBunitContext
{
	private void Arrange(HttpStatusCode status, string body)
	{
		var handler = new CapturingHttpHandler(() => new HttpResponseMessage(status)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		});
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });

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
	public async Task ARefusedSendShowsTheServersReason()
	{
		Arrange(HttpStatusCode.NotFound, """{"error":"No such character: Bbo"}""");
		Services.GetRequiredService<NavigationManager>().NavigateTo("/mail/compose?to=Bbo");

		var cut = Render<SharpMUSH.Client.Pages.MailCompose>();
		await cut.FindAll("button").First(b => b.TextContent.Contains("Send")).ClickAsync();

		var snackbar = Services.GetRequiredService<ISnackbar>();
		cut.WaitForAssertion(() =>
		{
			if (!snackbar.ShownSnackbars.Any())
				throw new InvalidOperationException("no snackbar yet");
		});

		var shown = snackbar.ShownSnackbars.Single();
		await Assert.That(shown.Severity).IsEqualTo(Severity.Error);
		await Assert.That(shown.Message).IsEqualTo("MailSendFailed(No such character: Bbo)");
	}

	[Test]
	public async Task AFailedMailboxReadIsNotAnEmptyInbox()
	{
		Arrange(HttpStatusCode.InternalServerError, """{"error":"The mail store did not answer."}""");

		var cut = Render<SharpMUSH.Client.Pages.Mail>();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".mail-list-error").Count == 0)
				throw new InvalidOperationException("mailbox error not rendered yet");
		});

		await Assert.That(cut.Find(".mail-list-error").TextContent).IsEqualTo("The mail store did not answer.");
		await Assert.That(cut.Markup).DoesNotContain("MailEmpty");
	}
}
