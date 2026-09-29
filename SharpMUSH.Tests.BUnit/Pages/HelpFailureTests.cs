using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;
using System.Text;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Answers each request from the path it asked for.</summary>
file sealed class RouteHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(respond(request.RequestUri!.AbsolutePath.TrimStart('/')));
}

/// <summary>
/// The help pages when the help API does not answer with help. <see cref="HelpPageTests"/> covers
/// the pages against the real controller; these cover the server being down, slow, or refusing.
/// </summary>
/// <remarks>
/// <see cref="GameHelpService"/> used to answer <see langword="null"/> for every failure and the pages
/// printed one fixed sentence, so a reader could not tell "the help files are being rebuilt" from
/// "you may not read the admin corpus". Its catch also named the exception types it expected and
/// left out <see cref="TaskCanceledException"/>, which is what <see cref="HttpClient.Timeout"/>
/// throws: a slow server took the page down instead of showing a message.
/// </remarks>
public class HelpFailureTests : TrackingBunitContext
{
	private const string Rebuilding = "The help index is being rebuilt.";

	private void AddHelpServices(Func<HttpResponseMessage> respond) => AddHelpServices(_ => respond());

	private void AddHelpServices(Func<string, HttpResponseMessage> respond)
	{
		var client = Track(new HttpClient(new RouteHandler(respond))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		});

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new GameHelpService(factory, NullLogger<GameHelpService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static HttpResponseMessage Unavailable() =>
		new(HttpStatusCode.ServiceUnavailable)
		{
			Content = new StringContent($$"""{"error":"{{Rebuilding}}"}""", Encoding.UTF8, "application/json")
		};

	[TUnit.Core.Test]
	public async Task Index_SaysWhyTheServerDidNotAnswer()
	{
		AddHelpServices(Unavailable);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".mud-alert").TextContent).Contains("HelpLoadFailed");
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains(Rebuilding);
	}

	[TUnit.Core.Test]
	public async Task Topic_SaysWhyTheServerDidNotAnswer()
	{
		AddHelpServices(Unavailable);
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, "newbie"));
		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".mud-alert").TextContent).Contains(Rebuilding);
		await Assert.That(cut.Markup).DoesNotContain("HelpNoSuchTopic")
			.Because("a server that did not answer has not said the topic is missing");
	}

	[TUnit.Core.Test]
	public async Task Topic_ATimeoutIsAMessageNotACrash()
	{
		AddHelpServices(() => throw new TaskCanceledException(
			"The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
			new TimeoutException()));
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, "newbie"));
		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".mud-alert").TextContent).Contains("HelpLoadFailed");
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains("HttpClient.Timeout");
	}

	/// <summary>
	/// A 404 is the server's documented "no such topic", even from a body that carries no entry — it
	/// must not become a load failure.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Topic_ABareNotFoundIsStillAMiss()
	{
		AddHelpServices(() => new HttpResponseMessage(HttpStatusCode.NotFound));
		this.AddAuthorization();

		var cut = Render<SharpMUSH.Client.Pages.HelpTopic>(p => p.Add(c => c.Topic, "nosuchtopicxyz"));
		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".mud-alert").TextContent).Contains("HelpNoSuchTopic");
		await Assert.That(cut.Markup).DoesNotContain("HelpLoadFailed");
	}

	/// <summary>
	/// Staff whose admin index failed used to see the page with the admin section silently missing,
	/// which reads as "there is no admin help".
	/// </summary>
	[TUnit.Core.Test]
	public async Task Index_StaffAreToldWhenTheAdminCorpusFailed()
	{
		AddHelpServices(path => path == "api/help"
			? new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(
					"""{"corpus":"help","topic":"help","html":"<p>Index.</p>","topics":["newbie"]}""",
					Encoding.UTF8, "application/json")
			}
			: Unavailable());
		this.AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");

		var cut = Render<SharpMUSH.Client.Pages.Help>();
		cut.WaitForAssertion(() => cut.Find(".mud-alert"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("Index.")
			.Because("the general corpus answered; only the admin one failed");
		await Assert.That(cut.Find(".mud-alert").TextContent).Contains(Rebuilding);
	}
}
