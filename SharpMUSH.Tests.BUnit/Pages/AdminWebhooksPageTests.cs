using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Webhooks;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Fakes <c>api/admin/webhooks</c>: one webhook, <c>logger</c>, with a delivered try and a failed one, and a create
/// that answers with a secret.
/// </summary>
internal sealed class WebhooksApiHandler : HttpMessageHandler
{
	public const string Secret = "whsec_dGVzdHNlY3JldA==";

	/// <summary>The override a create answers sets the secret, instead of a secret; null for none.</summary>
	public string? OverriddenBy { get; set; }

	public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

	private readonly List<WebhookView> _webhooks =
	[
		new("logger", "https://logs.example.com/game", ["PLAYER`CONNECT"], "private", true, null, "#1:1", DateTimeOffset.UnixEpoch,
			new WebhookStatus(4, 1, 0, 0, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
			[
				new WebhookAttempt("msg_2", "PLAYER`CONNECT", 2, DateTimeOffset.UnixEpoch, 200, null, TimeSpan.FromMilliseconds(40), WebhookAttemptOutcome.Delivered),
				new WebhookAttempt("msg_2", "PLAYER`CONNECT", 1, DateTimeOffset.UnixEpoch, 503, "answered 503 Service Unavailable", TimeSpan.FromMilliseconds(40), WebhookAttemptOutcome.Retrying),
			])),
	];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
		Requests.Add((request.Method, path, body));

		if (request.Method == HttpMethod.Get && path == "api/admin/webhooks")
		{
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(_webhooks) };
		}

		if (request.Method == HttpMethod.Post && path == "api/admin/webhooks")
		{
			var created = (await request.Content!.ReadFromJsonAsync<WebhookCreateRequest>(cancellationToken))!;
			_webhooks.Add(new WebhookView(created.Name, created.Url, created.Events ?? [], "public", true, null, null, DateTimeOffset.UnixEpoch, WebhookStatus.None));
			var issued = new WebhookSecret(created.Name, $"webhook.{created.Name}.signing", OverriddenBy is null ? Secret : null, OverriddenBy);
			return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(issued) };
		}

		if (request.Method == HttpMethod.Post && path.EndsWith("/test", StringComparison.Ordinal))
		{
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new WebhookTestSent("logger", "msg_3")) };
		}

		return new HttpResponseMessage(HttpStatusCode.NotFound);
	}
}

/// <summary>bUnit tests for /admin/webhooks: the list with its delivery record, creating one, and sending a test.</summary>
public class AdminWebhooksPageTests : TrackingBunitContext
{
	private BunitAuthorizationContext Auth { get; }

	public AdminWebhooksPageTests()
	{
		Auth = this.AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private (IRenderedComponent<SharpMUSH.Client.Pages.Admin.AdminWebhooks> Page, WebhooksApiHandler Api) RenderPage(
		string? overriddenBy = null)
	{
		var api = new WebhooksApiHandler { OverriddenBy = overriddenBy };
		var client = Track(new HttpClient(api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<WebhooksAdminService>()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		Auth.SetAuthorized("headwiz");
		Auth.SetPolicies("config.admin");
		var cut = Render<SharpMUSH.Client.Pages.Admin.AdminWebhooks>();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("[data-webhook]").Count == 0) throw new InvalidOperationException("webhooks not rendered yet");
		});
		return (cut, api);
	}

	[Test]
	public async Task ListsEachWebhookWithItsAddressCountsAndRecentTries()
	{
		var (cut, _) = RenderPage();

		var card = cut.Find("[data-webhook='logger']");
		await Assert.That(cut.Markup).Contains("https://logs.example.com/game");
		await Assert.That(card.QuerySelectorAll(".adm-hooks-counts dd").Select(dd => dd.TextContent).ToList())
			.IsEquivalentTo(new[] { "4", "1", "0", "0", "0" });
		await Assert.That(card.QuerySelectorAll(".adm-hooks-recent tbody tr").Length).IsEqualTo(2);
		await Assert.That(card.TextContent).Contains("answered 503 Service Unavailable");
		await Assert.That(card.QuerySelectorAll(".adm-hooks-reach").Length).IsEqualTo(0);
	}

	[Test]
	public async Task CreatingSendsTheFieldsAndShowsTheSecret()
	{
		var (cut, api) = RenderPage();

		await cut.Find("#webhook-new-name input, input#webhook-new-name").ChangeAsync("relay");
		await cut.Find("#webhook-new-url input, input#webhook-new-url").ChangeAsync("https://bot.example.com/hook");
		await cut.Find("#webhook-new-events input, input#webhook-new-events").ChangeAsync("channel`message page`message");
		await cut.Find("#webhook-create").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("#webhook-secret").Count == 0) throw new InvalidOperationException("secret not shown yet");
		});

		var (_, _, body) = api.Requests.Single(r => r.Method == HttpMethod.Post);
		var sent = System.Text.Json.JsonSerializer.Deserialize<WebhookCreateRequest>(body,
			new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
		await Assert.That(sent.Name).IsEqualTo("relay");
		await Assert.That(sent.Url).IsEqualTo("https://bot.example.com/hook");
		await Assert.That(sent.Events!).IsEquivalentTo(new[] { "channel`message", "page`message" });
		await Assert.That(cut.Find("#webhook-secret").TextContent).Contains(WebhooksApiHandler.Secret);
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("[data-webhook='relay']").Count == 0) throw new InvalidOperationException("list not reloaded yet");
		});
		await Assert.That(cut.Find("[data-webhook='relay'] .adm-hooks-reach").GetAttribute("data-reach")).IsEqualTo("public");
	}

	[Test]
	public async Task ASecretAnOverrideSetsIsNamedNotShown()
	{
		var (cut, _) = RenderPage("SHARPMUSH_SECRET_WEBHOOK_RELAY_SIGNING");

		await cut.Find("#webhook-new-name input, input#webhook-new-name").ChangeAsync("relay");
		await cut.Find("#webhook-create").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("#webhook-secret").Count == 0) throw new InvalidOperationException("secret not shown yet");
		});

		await Assert.That(cut.Find("#webhook-secret-overridden").TextContent).Contains("SHARPMUSH_SECRET_WEBHOOK_RELAY_SIGNING");
		await Assert.That(cut.FindAll("#webhook-secret code").Count).IsEqualTo(0);
	}

	[Test]
	public async Task AWebhooksSecretCannotBeShownAgain()
	{
		var (cut, _) = RenderPage();

		await Assert.That(cut.Markup).DoesNotContain("ShowSecret");
		await Assert.That(cut.FindAll("#webhook-secret").Count).IsEqualTo(0);
	}
}
