using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Onboarding;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Tests.BUnit.Pages;

public class SetupIdentityTests : BunitContext
{
	[Test]
	[Arguments("Fantasy\nHorror", "High Fantasy")]
	[Arguments("Unlisted genre", "High Fantasy")]
	[Arguments("Fantasy", "High Fantasy\nCyberpunk")]
	[Arguments("Fantasy", "Unlisted subgenre")]
	public async Task SettingsRejectNonCatalogOrMultipleChoices(string genres, string subgenres)
	{
		var writer = NSubstitute.Substitute.For<SharpMUSH.Library.Services.Interfaces.IConfigOptionWriter>();
		var controller = new SharpMUSH.Server.Controllers.OnboardingSettingsController(
			NSubstitute.Substitute.For<SharpMUSH.Library.Services.Interfaces.IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>>(),
			writer, NSubstitute.Substitute.For<SharpMUSH.Library.Services.Interfaces.IAuditLog>());
		var request = new OnboardingSettings("Test", genres.Split('\n'), subgenres.Split('\n'), "", "", "", "", "minimalist", "");
		await Assert.That(await controller.Put(request, default)).IsTypeOf<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>();
	}

	private sealed class Handler : HttpMessageHandler
	{
		public int Writes;
		public OnboardingSettings? Saved;
		public TaskCompletionSource? PendingSave;
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var result = new OnboardingSettings("Imported world", ["Fantasy"], ["High Fantasy"], "An existing introduction", "", "", "", "chibi", "");
			if (request.Method == HttpMethod.Put) { Writes++; result = (await request.Content!.ReadFromJsonAsync<OnboardingSettings>(cancellationToken))!; Saved = result; if (PendingSave is not null) await PendingSave.Task; }
			return new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
		}
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
		var form = Render<GameIdentityForm>();
		form.WaitForElement(".identity-preview img");
		var expected = SharpieArtCatalog.Resolve("chibi", "Fantasy", "High Fantasy", null, GuideImagery.Full, SharpieArtMoment.Welcome);
		await Assert.That(form.Find(".identity-preview img").GetAttribute("src")).Contains(expected!.Url);
		await form.Find("#guide-art-style").ChangeAsync("minimalist");
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
		var form = Render<GameIdentityForm>();
		form.WaitForElement("#guide-game-name");
		await Assert.That(form.Find("#guide-genres").TagName).IsEqualTo("SELECT");
		await Assert.That(form.Find("#guide-subgenres").TagName).IsEqualTo("SELECT");
		await Assert.That(form.FindAll("#guide-genres option").Select(option => option.TextContent)).Contains("Science Fiction");
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
		var form = Render<GameIdentityForm>();
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
		var form = Render<GameIdentityForm>();
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

}
