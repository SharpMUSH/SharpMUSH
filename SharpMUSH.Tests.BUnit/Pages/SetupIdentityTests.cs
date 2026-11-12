using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using MudBlazor;
using MudBlazor.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Client.Components.Onboarding;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.BUnit.Pages;

public class SetupIdentityTests : BunitContext
{
	public SetupIdentityTests()
	{
		Services.AddMudServices();
		Services.AddLogging();
		// The Home header options in the form read and write the configuration through these.
		Services.AddSingleton<AdminConfigService>();
		Services.AddSingleton<ConfigSchemaService>();
		Services.AddSingleton<WikiAssetService>();
		AddAuthorization().SetAuthorized("staff");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>The form inside a popover provider, which its drop-downs open into.</summary>
	private IRenderedComponent<SharpMUSH.Tests.BUnit.Components.MudHarness> RenderForm()
		=> Render<SharpMUSH.Tests.BUnit.Components.MudHarness>(p => p.AddChildContent<GameIdentityForm>());

	/// <summary>Picks an option of one of the form's drop-downs, as choosing it from the open list does.</summary>
	private static Task ChooseAsync(IRenderedComponent<SharpMUSH.Tests.BUnit.Components.MudHarness> form, string inputId, string value)
	{
		var select = form.FindComponents<MudSelect<string>>().Single(s => s.Instance.InputId == inputId);
		return select.InvokeAsync(() => select.Instance.SelectOption(value));
	}

	[Test]
	[Arguments("Fantasy\nHorror", "High Fantasy")]
	[Arguments("Unlisted genre", "High Fantasy")]
	[Arguments("Fantasy", "High Fantasy\nCyberpunk")]
	public async Task SettingsRejectNewNonCatalogGenresOrMultipleChoices(string genres, string subgenres)
	{
		var writer = NSubstitute.Substitute.For<SharpMUSH.Library.Services.Interfaces.IConfigOptionWriter>();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default());
		var controller = new SharpMUSH.Server.Controllers.OnboardingSettingsController(
			options,
			writer, NSubstitute.Substitute.For<SharpMUSH.Library.Services.Interfaces.IAuditLog>());
		var request = new OnboardingSettings("Test", genres.Split('\n'), subgenres.Split('\n'), "", "", "", "", "minimalist", "");
		await Assert.That(await controller.Put(request, default)).IsTypeOf<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>();
	}

	private sealed class Handler : HttpMessageHandler
	{
		public int Writes;
		public OnboardingSettings? Saved;
		public TaskCompletionSource? PendingSave;
		public OnboardingSettings Settings = new("Imported world", ["Fantasy"], ["High Fantasy"], "An existing introduction", "", "", "", "chibi", "");
		public OnboardingSettings? SaveResponse;
		public bool FailNextSave;
		/// <summary>The configuration api/configuration answers with.</summary>
		public readonly SharpMUSHOptions Options = SharpMUSHOptions.Default();
		/// <summary>The changes each configuration save sent, in order.</summary>
		public readonly List<Dictionary<string, JsonElement>> ConfigSaves = [];
		/// <summary>Whether a configuration save is refused, as the server refuses an invalid value.</summary>
		public bool RefuseConfig;
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri!.AbsolutePath == "/api/configuration")
			{
				if (request.Method == HttpMethod.Patch)
				{
					ConfigSaves.Add((await request.Content!.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken))!);
					if (RefuseConfig)
						return new(HttpStatusCode.BadRequest)
						{
							Content = JsonContent.Create(new { errors = new Dictionary<string, string> { ["Cosmetic.PortalSideArt"] = "Not a picture address." } })
						};
				}
				return new(HttpStatusCode.OK) { Content = JsonContent.Create(SharpMUSH.Library.API.OptionHelper.OptionsToConfigurationResponse(Options)) };
			}
			var result = Settings;
			if (request.Method == HttpMethod.Put)
			{
				Writes++;
				result = (await request.Content!.ReadFromJsonAsync<OnboardingSettings>(cancellationToken))!;
				Saved = result;
				if (PendingSave is not null) await PendingSave.Task;
				if (FailNextSave)
				{
					FailNextSave = false;
					return new(HttpStatusCode.BadRequest) { Content = JsonContent.Create("Save failed") };
				}
				result = SaveResponse ?? result;
				Settings = result;
			}
			return new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
		}
	}

	[Test]
	[Arguments("Fantasy", "Space Opera")]
	[Arguments("Fantasy", "High Fantasy\nCyberpunk")]
	[Arguments("Custom genre", "Space Opera / Detective")]
	[Arguments("Fantasy\nHorror", "Space Opera\nCyberpunk")]
	[Arguments("", "")]
	public async Task UnrelatedIdentityEditsPreserveStoredMetadataAcrossSaves(string genres, string subgenres)
	{
		using var handler = new Handler();
		var expectedGenres = genres == "" ? Array.Empty<string>() : genres.Split('\n');
		var expectedSubgenres = subgenres == "" ? Array.Empty<string>() : subgenres.Split('\n');
		handler.Settings = handler.Settings with { Genres = expectedGenres, Subgenres = expectedSubgenres };
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		form.WaitForElement("#guide-game-name");
		await form.Find("#guide-game-name").InputAsync("Renamed world");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Genres).IsEquivalentTo(expectedGenres, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(handler.Saved.Subgenres).IsEquivalentTo(expectedSubgenres, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await form.Find("#guide-introduction").InputAsync("A new introduction");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Genres).IsEquivalentTo(expectedGenres, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(handler.Saved.Subgenres).IsEquivalentTo(expectedSubgenres, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(handler.Writes).IsEqualTo(2);
	}

	[Test]
	public async Task ExplicitSelectorChangesReplaceOnlyThatFieldAndStaySaved()
	{
		using var handler = new Handler();
		handler.Settings = handler.Settings with { Genres = ["Custom genre"], Subgenres = ["Space Opera", "Cyberpunk"] };
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		form.WaitForElement("#guide-genres");
		await ChooseAsync(form, "guide-genres", "Horror");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Genres).IsEquivalentTo(new[] { "Horror" });
		await Assert.That(handler.Saved.Subgenres).IsEquivalentTo(new[] { "Space Opera", "Cyberpunk" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await form.Find("#guide-subgenres").InputAsync("None");
		await form.Find(".identity-save").ClickAsync();
		await form.Find("#guide-introduction").InputAsync("After replacing the subgenre");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Genres).IsEquivalentTo(new[] { "Horror" });
		await Assert.That(handler.Saved.Subgenres).IsEquivalentTo(new[] { "None" });
	}

	[Test]
	public async Task LaterSavesUseMetadataReturnedByTheServer()
	{
		using var handler = new Handler();
		handler.SaveResponse = handler.Settings with { Subgenres = ["Space Opera", "Cyberpunk"] };
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		form.WaitForElement("#guide-game-name");
		await form.Find("#guide-game-name").InputAsync("Renamed world");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(form.Find("#guide-subgenres").GetAttribute("value")).IsEqualTo("Space Opera / Cyberpunk");
		await form.Find("#guide-introduction").InputAsync("New introduction");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Subgenres).IsEquivalentTo(new[] { "Space Opera", "Cyberpunk" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task FailedSubgenreSaveRetainsTheChoiceAndUntouchedGenreForRetry()
	{
		using var handler = new Handler { FailNextSave = true };
		handler.Settings = handler.Settings with { Genres = ["Custom genre", "Horror"], Subgenres = ["Space Opera"] };
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		form.WaitForElement("#guide-subgenres");
		await form.Find("#guide-subgenres").InputAsync("Space Western");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(form.FindAll("[role=alert]").Count).IsEqualTo(1);
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Genres).IsEquivalentTo(new[] { "Custom genre", "Horror" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(handler.Saved.Subgenres).IsEquivalentTo(new[] { "Space Western" });
		await Assert.That(form.FindAll("[role=alert]").Count).IsEqualTo(0);
		await form.Find("#guide-introduction").InputAsync("After retrying");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Genres).IsEquivalentTo(new[] { "Custom genre", "Horror" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(handler.Saved.Subgenres).IsEquivalentTo(new[] { "Space Western" });
	}

	[Test]
	public async Task CustomSubgenreNoticeIsAdvisoryAndTheValueSurvivesSavesAndReload()
	{
		using var handler = new Handler();
		handler.Settings = handler.Settings with { Genres = ["Custom genre", "Horror"] };
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		var input = form.WaitForElement("#guide-subgenres");
		await Assert.That(input.TagName).IsEqualTo("INPUT");
		await Assert.That(input.GetAttribute("maxlength")).IsEqualTo("512");
		// The catalog's subgenres are suggested as the admin types; any other value is still accepted.
		var suggest = form.FindComponents<MudAutocomplete<string>>().Single().Instance.SearchFunc!;
		await Assert.That(await suggest("", default)).Contains("High Fantasy");
		await Assert.That(await suggest("fantasy", default)).Contains("High Fantasy");
		await Assert.That(await suggest("fantasy", default)).DoesNotContain("Cyberpunk");
		await Assert.That(form.FindAll("#guide-subgenre-custom").Count).IsEqualTo(0);
		await input.InputAsync("Space Opera / Detective");
		await Assert.That(form.Find("#guide-subgenre-custom").TextContent).Contains("Custom values are allowed");
		await Assert.That(input.GetAttribute("aria-describedby")).IsEqualTo("guide-subgenre-custom");
		await Assert.That(form.FindAll("[role=alert]").Count).IsEqualTo(0);
		await Assert.That(form.Find(".identity-save").GetAttribute("aria-disabled")).IsEqualTo("false");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Subgenres).IsEquivalentTo(new[] { "Space Opera / Detective" });
		await Assert.That(handler.Saved.Genres).IsEquivalentTo(new[] { "Custom genre", "Horror" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await form.Find("#guide-introduction").InputAsync("An unrelated edit after saving");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Subgenres).IsEquivalentTo(new[] { "Space Opera / Detective" });
		await DisposeComponentsAsync();
		var reloaded = RenderForm();
		input = reloaded.WaitForElement("#guide-subgenres");
		await Assert.That(input.GetAttribute("value")).IsEqualTo("Space Opera / Detective");
		await Assert.That(reloaded.FindAll("#guide-subgenre-custom").Count).IsEqualTo(1);
		await input.InputAsync("High Fantasy");
		await Assert.That(reloaded.FindAll("#guide-subgenre-custom").Count).IsEqualTo(0);
		await Assert.That(input.GetAttribute("aria-describedby")).IsNull();
		await reloaded.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Saved!.Subgenres).IsEquivalentTo(new[] { "High Fantasy" });
	}

	[Test]
	[Arguments("High Fantasy\nCyberpunk", false)]
	[Arguments("Space Opera\nCyberpunk", true)]
	[Arguments("", false)]
	public async Task CustomSubgenreNoticeChecksStoredValuesRatherThanTheirJoinedLabel(string subgenres, bool expectedNotice)
	{
		using var handler = new Handler();
		handler.Settings = handler.Settings with { Subgenres = subgenres == "" ? [] : subgenres.Split('\n') };
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		form.WaitForElement("#guide-subgenres");
		await Assert.That(form.FindAll("#guide-subgenre-custom").Count > 0).IsEqualTo(expectedNotice);
	}
	private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new("https://example.test/") };
	}
	[Test]
	public async Task IdentityPreviewUsesTheWelcomeArtworkMoment()
	{
		using var handler = new Handler();
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		form.WaitForElement(".identity-preview img");
		var expected = SharpieArtCatalog.Resolve("chibi", "Fantasy", "High Fantasy", null, GuideImagery.Full, SharpieArtMoment.Welcome);
		await Assert.That(form.Find(".identity-preview img").GetAttribute("src")).Contains(expected!.Url);
		await ChooseAsync(form, "guide-art-style", "minimalist");
		expected = SharpieArtCatalog.Resolve("minimalist", "Fantasy", "High Fantasy", null, GuideImagery.Full, SharpieArtMoment.Welcome);
		await Assert.That(form.Find(".identity-preview img").GetAttribute("src")).Contains(expected!.Url);
		await Assert.That(handler.Writes).IsEqualTo(0);
	}

	[Test]
	public async Task ImportedIdentityIsPrefilledAndOnlySavedExplicitly()
	{
		using var handler = new Handler();
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		form.WaitForElement("#guide-game-name");
		// The genre and the art style are kit drop-downs, like the rest of setup's fields; no native select is left.
		await Assert.That(form.FindAll("select").Count).IsEqualTo(0);
		var genres = form.FindComponents<MudSelect<string>>().Single(s => s.Instance.InputId == "guide-genres");
		await Assert.That(genres.FindComponents<MudSelectItem<string>>().Select(item => item.Instance.Value)).Contains("Science Fiction");
		await Assert.That(form.FindComponents<MudAutocomplete<string>>().Single().Instance.InputId).IsEqualTo("guide-subgenres");
		await Assert.That(form.Find("#guide-game-name").GetAttribute("value")).IsEqualTo("Imported world");
		await Assert.That(handler.Writes).IsEqualTo(0);
		await form.Find("#guide-game-name").InputAsync("My new world");
		await Assert.That(form.Find(".identity-preview").TextContent.Contains("My new world", StringComparison.Ordinal)).IsTrue();
		await Assert.That(handler.Writes).IsEqualTo(0);
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Writes).IsEqualTo(1);
		await Assert.That(form.Markup.Contains("Saved", StringComparison.Ordinal)).IsTrue();
		await form.Find("#guide-introduction").InputAsync("Unsaved introduction");
		await Assert.That(form.FindAll("[role=status]").Any(node => node.TextContent.Contains("Saved", StringComparison.Ordinal))).IsFalse();
		await Assert.That(handler.Writes).IsEqualTo(1);
	}
	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task WelcomeStarterOnlyReplacesDraftWhenChosenAndEditsSave(int starter)
	{
		using var handler = new Handler();
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		form.WaitForElement("#guide-introduction");
		await Assert.That(form.Find("textarea").GetAttribute("value")).IsEqualTo("An existing introduction");
		await form.Find("#guide-game-name").InputAsync("Our world");
		await Assert.That(form.Find(".identity-preview-intro").TextContent).IsEqualTo("An existing introduction");
		await form.FindAll(".identity-starters button")[starter].ClickAsync();
		var chosen = form.Find(".identity-preview-intro").TextContent;
		await Assert.That(chosen.StartsWith("Welcome to Our world.", StringComparison.Ordinal)).IsTrue();
		await Assert.That(form.Find("textarea").GetAttribute("value")).IsEqualTo(chosen);
		await Assert.That(handler.Writes).IsEqualTo(0);
		await form.Find("#guide-game-name").InputAsync("Another name");
		await Assert.That(form.Find(".identity-preview-intro").TextContent).IsEqualTo(chosen);
		var edited = chosen + " A personal addition.";
		await form.Find("#guide-introduction").InputAsync(edited);
		await Assert.That(form.Find(".identity-preview-intro").TextContent).IsEqualTo(edited);
		await Assert.That(handler.Writes).IsEqualTo(0);
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Writes).IsEqualTo(1);
		await Assert.That(handler.Saved!.Introduction).IsEqualTo(edited);
	}

	[Test]
	public async Task SaveRemainsFocusableAndRejectsDuplicateClicksWhilePending()
	{
		using var handler = new Handler { PendingSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		var button = form.WaitForElement(".identity-save");
		var saving = button.ClickAsync();
		form.WaitForAssertion(() =>
		{
			if (button.GetAttribute("aria-disabled") != "true") throw new InvalidOperationException("Save has not started.");
		});
		await Assert.That(form.Find("fieldset").HasAttribute("disabled")).IsTrue();
		await Assert.That(button.HasAttribute("disabled")).IsFalse();
		await Assert.That(button.Closest("fieldset")).IsNull();
		await button.ClickAsync();
		await Assert.That(handler.Writes).IsEqualTo(1);
		handler.PendingSave.SetResult();
		await saving;
		await Assert.That(button.GetAttribute("aria-disabled")).IsEqualTo("false");
		await Assert.That(form.Find("fieldset").HasAttribute("disabled")).IsFalse();
		await Assert.That(JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus")).IsEqualTo(0);
	}


	[Test]
	public async Task HomeHeaderOptionsShowExamplesPreviewAndSaveWithTheIdentity()
	{
		using var handler = new Handler();
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		var tagline = form.WaitForElement("input[name='Cosmetic.PortalTagline']");
		// An empty field shows the option's example, greyed.
		await Assert.That(tagline.GetAttribute("placeholder")).IsEqualTo("Gaslight intrigue in a city that never sleeps.");
		await Assert.That(form.FindAll("[data-config-option]").Select(row => row.GetAttribute("data-config-option")))
			.IsEquivalentTo(SharpMUSH.Client.Components.Onboarding.HomeHeaderFields.Options, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await tagline.ChangeAsync("Rain, guilds and secrets.");
		await Assert.That(form.Find(".home-header-preview .game-hero-tagline").TextContent).IsEqualTo("Rain, guilds and secrets.");
		await Assert.That(handler.ConfigSaves.Count).IsEqualTo(0);
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.Writes).IsEqualTo(1);
		await Assert.That(handler.ConfigSaves.Count).IsEqualTo(1);
		await Assert.That(handler.ConfigSaves[0].Keys).IsEquivalentTo(new[] { "Cosmetic.PortalTagline" });
		await Assert.That(handler.ConfigSaves[0]["Cosmetic.PortalTagline"].GetString()).IsEqualTo("Rain, guilds and secrets.");
		// Saved: a second save sends the identity again but no unchanged option.
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.ConfigSaves.Count).IsEqualTo(1);
	}

	[Test]
	public async Task ARefusedHeaderOptionLeavesTheIdentityUnsaved()
	{
		using var handler = new Handler { RefuseConfig = true };
		Services.AddSingleton<IHttpClientFactory>(new Factory(handler));
		Services.AddSingleton<ServerInfoService>();
		Services.AddLocalization();
		var form = RenderForm();
		var tagline = form.WaitForElement("input[name='Cosmetic.PortalTagline']");
		await form.Find("#guide-game-name").InputAsync("Renamed world");
		await tagline.ChangeAsync("Rain, guilds and secrets.");
		await form.Find(".identity-save").ClickAsync();
		await Assert.That(handler.ConfigSaves.Count).IsEqualTo(1);
		await Assert.That(handler.Writes).IsEqualTo(0).Because("the identity is saved only once the header's options are");
		await Assert.That(form.FindAll("[role=alert]").Count).IsGreaterThan(0);
		await Assert.That(form.FindAll("[role=status]").Any(node => node.TextContent.Contains("Saved", StringComparison.Ordinal))).IsFalse();
	}
}
