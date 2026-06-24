using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the WikiDisplay component to verify article rendering with parameters.
/// </summary>
public class WikiDisplayTests
{
	[Test]
	public async Task WikiDisplay_WithArticle_DisplaysTitle()
	{
		await using var ctx = new BunitContext();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.AddAuthorization();
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		// Add WikiMarkdigPipeline + WikiService required by component. WikiService is only
		// invoked for redlink checks when the rendered HTML contains /wiki/ links (none in
		// these fixtures), so a never-called substitute IHttpClientFactory suffices.
		ctx.Services.AddSingleton<WikiMarkdigPipeline>();
		ctx.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		ctx.Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));

		var article = new WikiArticle("Test Article", "This is test content", null);

		var cut = ctx.Render<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "test-article")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Test Article");
	}

	[Test]
	public async Task WikiDisplay_WithArticle_RendersContent()
	{
		await using var ctx = new BunitContext();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.AddAuthorization();
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		// Add WikiMarkdigPipeline + WikiService required by component. WikiService is only
		// invoked for redlink checks when the rendered HTML contains /wiki/ links (none in
		// these fixtures), so a never-called substitute IHttpClientFactory suffices.
		ctx.Services.AddSingleton<WikiMarkdigPipeline>();
		ctx.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		ctx.Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));

		var article = new WikiArticle("Test Article", "This is **bold** content", null);

		var cut = ctx.Render<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "test-article")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("<strong>bold</strong>");
	}

	[Test]
	public async Task WikiDisplay_WithoutArticle_ShowsInfoMessage()
	{
		await using var ctx = new BunitContext();
		ctx.AddAuthorization();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		// Add WikiMarkdigPipeline + WikiService required by component. WikiService is only
		// invoked for redlink checks when the rendered HTML contains /wiki/ links (none in
		// these fixtures), so a never-called substitute IHttpClientFactory suffices.
		ctx.Services.AddSingleton<WikiMarkdigPipeline>();
		ctx.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		ctx.Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));

		var cut = ctx.Render<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "nonexistent")
			.Add(p => p.Article, null)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Nothing to see here");
	}

	[Test]
	public async Task WikiDisplay_WithoutArticle_WhenAuthorized_ShowsCreateOption()
	{
		await using var ctx = new BunitContext();
		var authContext = ctx.AddAuthorization();
		authContext.SetAuthorized("TestUser");
		authContext.SetPolicies("wiki.create"); // the Create-page block is gated on wiki.create
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		// Add WikiMarkdigPipeline + WikiService required by component. WikiService is only
		// invoked for redlink checks when the rendered HTML contains /wiki/ links (none in
		// these fixtures), so a never-called substitute IHttpClientFactory suffices.
		ctx.Services.AddSingleton<WikiMarkdigPipeline>();
		ctx.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		ctx.Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));

		var cut = ctx.Render<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "new-page")
			.Add(p => p.Article, null)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("does not exist yet");
		await Assert.That(markup).Contains("Create this Page");
	}

	[Test]
	public async Task WikiDisplay_WhenAuthorized_ShowsEditButton()
	{
		await using var ctx = new BunitContext();
		var authContext = ctx.AddAuthorization();
		authContext.SetAuthorized("TestUser");
		authContext.SetPolicies("wiki.edit"); // the Edit button is gated on wiki.edit
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		// Add WikiMarkdigPipeline + WikiService required by component. WikiService is only
		// invoked for redlink checks when the rendered HTML contains /wiki/ links (none in
		// these fixtures), so a never-called substitute IHttpClientFactory suffices.
		ctx.Services.AddSingleton<WikiMarkdigPipeline>();
		ctx.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		ctx.Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));

		var article = new WikiArticle("Test Article", "Content", null);

		var cut = ctx.Render<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "test-article")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		var markup = cut.Markup;
		await Assert.That(cut.FindAll("button").Count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task WikiDisplay_WhenNotAuthorized_DoesNotShowEditButton()
	{
		await using var ctx = new BunitContext();
		ctx.AddAuthorization();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		// Add WikiMarkdigPipeline + WikiService required by component. WikiService is only
		// invoked for redlink checks when the rendered HTML contains /wiki/ links (none in
		// these fixtures), so a never-called substitute IHttpClientFactory suffices.
		ctx.Services.AddSingleton<WikiMarkdigPipeline>();
		ctx.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		ctx.Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));

		var article = new WikiArticle("Test Article", "Content", null);

		var cut = ctx.Render<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "test-article")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		var buttons = cut.FindAll("button");
		await Assert.That(buttons.Count).IsEqualTo(0);
	}

	[Test]
	public async Task WikiDisplay_HomeSlug_DisplaysAsHero()
	{
		await using var ctx = new BunitContext();
		ctx.AddAuthorization();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		// Add WikiMarkdigPipeline + WikiService required by component. WikiService is only
		// invoked for redlink checks when the rendered HTML contains /wiki/ links (none in
		// these fixtures), so a never-called substitute IHttpClientFactory suffices.
		ctx.Services.AddSingleton<WikiMarkdigPipeline>();
		ctx.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
		ctx.Services.AddSingleton(sp => new WikiService(
			sp.GetRequiredService<IHttpClientFactory>(), NullLogger<WikiService>.Instance));

		var article = new WikiArticle("Home", "Welcome", null);

		var cut = ctx.Render<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "home")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("WikiContent--hero");
	}
}
