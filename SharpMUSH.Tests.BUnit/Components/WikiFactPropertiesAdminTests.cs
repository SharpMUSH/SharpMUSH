using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The fact properties card on <c>/admin/wiki</c>: each property listed, a new one defined through the dialog with
/// the whole definition sent, and one removed once confirmed.
/// </summary>
public class WikiFactPropertiesAdminTests : TrackingBunitContext
{
	/// <summary>Serves the properties, stores what a PUT sends, and forgets what a DELETE names.</summary>
	private sealed class Handler : HttpMessageHandler
	{
		public List<WikiFactPropertyDto> Properties { get; } =
		[
			new("pf2e:level", "number", false, [], "Level"),
			new("pf2e:tradition", "keyword", true, ["arcane", "divine"], "Tradition", "The magic a spell draws on."),
		];

		public List<(string Id, SetFactPropertyRequest Body)> Puts { get; } = [];
		public List<string> Deletes { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
			if (request.Method == HttpMethod.Get && path == "/api/wiki/fact-properties")
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Properties) };
			var id = path["/api/wiki/fact-properties/".Length..];
			if (request.Method == HttpMethod.Put)
			{
				var body = (await request.Content!.ReadFromJsonAsync<SetFactPropertyRequest>(cancellationToken))!;
				Puts.Add((id, body));
				var stored = new WikiFactPropertyDto(id, body.Type, body.Multiple, body.Keywords ?? [], body.Label ?? "", body.Description ?? "");
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(stored) };
			}

			Deletes.Add(id);
			return new HttpResponseMessage(HttpStatusCode.NoContent);
		}
	}

	private readonly Handler _handler = new();

	public WikiFactPropertiesAdminTests()
	{
		var http = Track(new HttpClient(_handler) { BaseAddress = new Uri("https://localhost/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		Services.AddMudServices();
		Services.AddSingleton(new WikiService(factory, NullLogger<WikiService>.Instance));
		Services.AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<IComponent> RenderCard()
	{
		var cut = Render(builder =>
		{
			builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
			builder.CloseComponent();
			builder.OpenComponent<MudBlazor.MudDialogProvider>(1);
			builder.CloseComponent();
			builder.OpenComponent<WikiFactPropertiesAdmin>(2);
			builder.CloseComponent();
		});
		cut.WaitForAssertion(() => cut.Find(".wikiprops-table"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task EachPropertyIsListed()
	{
		var cut = RenderCard();

		await Assert.That(cut.FindAll(".wikiprops-key").Select(c => c.TextContent)).IsEquivalentTo(["pf2e:level", "pf2e:tradition"]);
		await Assert.That(cut.FindAll(".wikiprops-values").Select(c => c.TextContent.Trim())).IsEquivalentTo(["", "arcane, divine"]);
		await Assert.That(cut.Find(".wikiprops-desc").TextContent).IsEqualTo("The magic a spell draws on.");
	}

	[Test]
	public async Task ANewPropertyIsSentWhole()
	{
		var cut = RenderCard();

		await cut.Find("button[aria-label='WikiPropNew']").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".wikiprops-form"), TimeSpan.FromSeconds(5));
		await cut.Find(".wikiprops-form input").InputAsync(new ChangeEventArgs { Value = "faction:rank" });
		await cut.FindAll(".mud-dialog button").Single(b => b.TextContent.Trim() == "Save").ClickAsync();

		cut.WaitForState(() => cut.FindAll(".wikiprops-key").Select(c => c.TextContent).Contains("faction:rank"), TimeSpan.FromSeconds(5));
		var (id, body) = _handler.Puts.Single();
		await Assert.That(id).IsEqualTo("faction:rank");
		await Assert.That(body with { Keywords = null }).IsEqualTo(new SetFactPropertyRequest("keyword", false, null, "", ""));
		await Assert.That(body.Keywords!).IsEmpty();
	}

	[Test]
	public async Task APropertyIsRemovedOnceConfirmed()
	{
		var cut = RenderCard();

		await cut.Find("button[aria-label='WikiPropRemove']").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".mud-dialog"), TimeSpan.FromSeconds(5));
		await Assert.That(_handler.Deletes).IsEmpty();
		await cut.FindAll(".mud-dialog button").Single(b => b.TextContent.Trim() == "WikiPropRemoveConfirm").ClickAsync();

		cut.WaitForState(() => cut.FindAll(".wikiprops-key").Count == 1, TimeSpan.FromSeconds(5));
		await Assert.That(_handler.Deletes).IsEquivalentTo(["pf2e:level"]);
	}
}
