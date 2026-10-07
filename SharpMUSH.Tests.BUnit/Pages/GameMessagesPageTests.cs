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
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Fakes <c>api/admin/messages</c>: every message with a plain stored text, the connect screen's in colour, and
/// the Messages package installed as #7, and whichever object the last <c>PUT source</c> named.
/// </summary>
internal sealed class GameMessagesApiHandler : HttpMessageHandler
{
	public const string GreenConnect = "\u001b[38;2;0;245;183m==\u001b[0m Welcome";

	public List<(string Path, string Body)> Puts { get; } = [];

	private readonly Dictionary<GameMessage, string> _texts = GameMessages.All.ToDictionary(m => m, m => m == GameMessage.Connect
		? GreenConnect
		: $"{m} text");

	public const int PackageObject = 7;

	private int? _object;

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (!path.StartsWith("api/admin/messages", StringComparison.Ordinal))
		{
			return new HttpResponseMessage(HttpStatusCode.NotFound);
		}

		if (request.Method == HttpMethod.Put)
		{
			var body = await request.Content!.ReadAsStringAsync(cancellationToken);
			Puts.Add((path, body));
			if (path == "api/admin/messages/source")
			{
				_object = (await request.Content.ReadFromJsonAsync<GameMessageSourceRequest>(cancellationToken))!.ObjectDbref;
			}
			else
			{
				var message = Enum.Parse<GameMessage>(path["api/admin/messages/".Length..]);
				_texts[message] = (await request.Content.ReadFromJsonAsync<GameMessageTextRequest>(cancellationToken))!.Text;
			}
		}

		return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(State()) };
	}

	private GameMessagesResponse State() => new(
		_object is null ? GameMessageSource.Stored : GameMessageSource.Object,
		PackageInstalled: true,
		ObjectDbref: _object,
		ObjectName: _object is null ? null : "Messages",
		PackageObjectDbref: PackageObject,
		Messages: [.. GameMessages.All.Select(m => new GameMessageEntry(m, _texts[m], IsDefault: true, GameMessages.AttributeName(m),
			ObjectHasAttribute: false, _texts[m]))]);
}

/// <summary>bUnit tests for /admin/messages: the stored texts edited in colour, and the source switch.</summary>
public class GameMessagesPageTests : TrackingBunitContext
{
	private BunitAuthorizationContext Auth { get; }

	public GameMessagesPageTests()
	{
		Auth = this.AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	internal (IRenderedComponent<SharpMUSH.Client.Pages.Admin.GameMessages> Page, GameMessagesApiHandler Api) RenderPage()
	{
		var api = new GameMessagesApiHandler();
		var client = Track(new HttpClient(api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<GameMessagesService>()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		Auth.SetAuthorized("headwiz");
		Auth.SetPolicies("config.admin");
		var cut = Render<SharpMUSH.Client.Pages.Admin.GameMessages>();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".adm-msg-item").Count == 0) throw new InvalidOperationException("messages not rendered yet");
		});
		return (cut, api);
	}

	[Test]
	public async Task ListsEveryMessageAndPreviewsTheConnectScreenInColour()
	{
		var (cut, _) = RenderPage();

		await Assert.That(cut.FindAll(".adm-msg-item").Count).IsEqualTo(GameMessages.All.Count);
		await Assert.That(cut.Find("#messages-preview").InnerHtml).Contains("color: #00f5b7");
		await Assert.That(cut.Find("textarea").GetAttribute("wrap")).IsEqualTo("off");
	}

	/// <summary>The editor writes the text back as a terminal receives it: the colour it was loaded with survives an edit.</summary>
	[Test]
	public async Task SavingWritesTheEditedTextWithItsColours()
	{
		var (cut, api) = RenderPage();

		await Assert.That(cut.Find("#messages-save").HasAttribute("disabled")).IsTrue();

		await cut.Find("textarea").InputAsync("== Welcome back");
		cut.WaitForAssertion(() =>
		{
			if (cut.Find("#messages-save").HasAttribute("disabled")) throw new InvalidOperationException("still clean");
		});
		await cut.Find("#messages-save").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (api.Puts.Count == 0) throw new InvalidOperationException("not saved yet");
		});

		var (path, body) = api.Puts.Single();
		var text = System.Text.Json.JsonSerializer.Deserialize<GameMessageTextRequest>(body,
			new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.Text;
		await Assert.That(path).IsEqualTo("api/admin/messages/Connect");
		await Assert.That(text).StartsWith("\u001b[38;2;0;245;183m==");
		await Assert.That(MarkupString.Ansi.AnsiEscapeParser.Parse(text).ToPlainText()).IsEqualTo("== Welcome back");
	}

	[Test]
	public async Task ChoosingTheObjectSetsTheMessagesObject()
	{
		var (cut, api) = RenderPage();

		await cut.Find("[data-source='Object']").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (api.Puts.Count == 0) throw new InvalidOperationException("not sent yet");
		});

		var (path, body) = api.Puts.Single();
		await Assert.That(path).IsEqualTo("api/admin/messages/source");
		await Assert.That(System.Text.Json.JsonSerializer.Deserialize<GameMessageSourceRequest>(body,
			new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!.ObjectDbref)
			.IsEqualTo(GameMessagesApiHandler.PackageObject);
		cut.WaitForAssertion(() =>
		{
			if (cut.Find("[data-source='Object']").GetAttribute("aria-checked") != "true")
				throw new InvalidOperationException("not re-rendered yet");
		});
	}
}
