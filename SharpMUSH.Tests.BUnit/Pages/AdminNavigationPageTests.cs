using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin.Navigation;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Services.Navigation;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Serves <c>api/applications</c> and <c>api/navigation</c>, keeping what each save sent.</summary>
internal sealed class NavigationApiHandler : HttpMessageHandler
{
	public NavLayout? Stored { get; set; }

	/// <summary>Answer saves with 409, as when another save landed first.</summary>
	public bool Conflict { get; set; }

	public ConcurrentQueue<SaveNavLayoutRequest> Saves { get; } = new();

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (path == "api/applications")
		{
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new[]
				{
					new
					{
						slug = "boards", displayName = "Bulletin boards", icon = (string?)null, kind = "Page", schemaUrl = "http/boards/schema",
						minimumRole = "Guest", navPlacement = "Community", zones = Array.Empty<string>(), order = 0,
					},
				}),
			};
		}
		if (path != "api/navigation") return new HttpResponseMessage(HttpStatusCode.NotFound);
		if (request.Method == HttpMethod.Get)
		{
			return Stored is null ? new HttpResponseMessage(HttpStatusCode.NoContent) : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Stored) };
		}
		var save = JsonSerializer.Deserialize<SaveNavLayoutRequest>(await request.Content!.ReadAsStringAsync(cancellationToken), JsonSerializerOptions.Web)!;
		Saves.Enqueue(save);
		if (Conflict) return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { currentVersion = 9 }) };
		Stored = save.Layout with { Version = save.ExpectedVersion + 1 };
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Stored) };
	}
}

/// <summary>
/// The navigation editor: it starts from what players get, moves a destination with the keyboard-reachable controls,
/// saves the whole layout over the version it read, and says so when another save landed first.
/// </summary>
public class AdminNavigationPageTests : TrackingBunitContext
{
	private readonly NavigationApiHandler _api = new();

	public AdminNavigationPageTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(new NavLayoutClient(factory))
			.AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
		AddAuthorization().SetAuthorized("staff");
	}

	private IRenderedComponent<MudHarness> RenderEditor()
	{
		var cut = Render<MudHarness>(p => p.AddChildContent<AdminNavigation>());
		cut.WaitForElement("#nav-save");
		return cut;
	}

	private static AngleSharp.Dom.IElement MoveSelect(IRenderedComponent<MudHarness> cut, string key) =>
		cut.FindAll("li.adm-nav-item").Single(li => li.QuerySelector(".adm-nav-item-key")!.TextContent == key).QuerySelector("select.adm-nav-move")!;

	[Test]
	public async Task ANewGame_ShowsTheDefaultGroups_WithTheGamesApplications()
	{
		var cut = RenderEditor();

		var keys = cut.FindAll(".adm-nav-item-key").Select(k => k.TextContent).ToList();
		await Assert.That(keys).Contains("core:play");
		await Assert.That(keys).Contains("app:boards");
		await Assert.That(cut.FindAll(".adm-nav-pill--new")).IsEmpty().Because("nothing is new before staff ever saved");
	}

	[Test]
	public async Task MovingAnItem_ThenSaving_SendsTheWholeLayoutOverTheVersionRead()
	{
		var cut = RenderEditor();

		await MoveSelect(cut, "core:help").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "play" });
		await cut.Find("#nav-save").ClickAsync();
		cut.WaitForState(() => !_api.Saves.IsEmpty);

		var save = _api.Saves.Single();
		await Assert.That(save.ExpectedVersion).IsEqualTo(0);
		await Assert.That(save.Layout.Groups.Single(g => g.Id == "play").Items.Select(i => i.Key)).Contains("core:help");
		await Assert.That(save.Layout.Groups.Single(g => g.Id == "world").Items.Select(i => i.Key)).DoesNotContain("core:help");
		await Assert.That(save.Layout.Groups.Single(g => g.Id == "Community").Items.Select(i => i.Key)).Contains("app:boards");
	}

	[Test]
	public async Task HidingAnItem_PutsItUnderHidden()
	{
		var cut = RenderEditor();

		await MoveSelect(cut, "core:map").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "#hidden" });

		var hidden = cut.FindAll("ul.adm-nav-items").Last();
		await Assert.That(hidden.TextContent).Contains("core:map");
	}

	[Test]
	public async Task ASaveThatLostTheRace_SaysSo_AndOffersAReload()
	{
		_api.Conflict = true;
		var cut = RenderEditor();

		await cut.Find("#nav-save").ClickAsync();

		cut.WaitForElement("#nav-reload");
		await Assert.That(cut.Markup).Contains("AdmNavConflict");
	}

	[Test]
	public async Task WhatTheSavedLayoutNeverPlaced_IsMarkedNew()
	{
		_api.Stored = new NavLayout(NavLayout.CurrentFormat, 4,
			[new("play", null, NavGroupModes.Inline, null, [new("core:play"), new("core:scenes"), new("core:mail")])],
			[], []);

		var cut = RenderEditor();

		var boards = cut.FindAll("li.adm-nav-item").Single(li => li.QuerySelector(".adm-nav-item-key")!.TextContent == "app:boards");
		await Assert.That(boards.QuerySelector(".adm-nav-pill--new")).IsNotNull();
	}
}
