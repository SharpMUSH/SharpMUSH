using Bunit;
using Bunit.TestDoubles;
using TUnit.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using NSubstitute;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the WikiDisplay component to verify article rendering with parameters.
/// </summary>
public class WikiDisplayTests : Bunit.TestContext
{
	[Test]
	public async Task WikiDisplay_WithArticle_DisplaysTitle()
	{
		// Arrange
		var wikiService = Substitute.For<WikiService>(
			Substitute.For<ILogger<WikiService>>(),
			Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(wikiService);

		var article = new WikiArticle("Test Article", "This is test content", null);

		// Act
		var cut = RenderComponent<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "test-article")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		// Assert
		var markup = cut.Markup;
		await Assert.That(markup).Contains("Test Article");
	}

	[Test]
	public async Task WikiDisplay_WithArticle_RendersContent()
	{
		// Arrange
		var wikiService = Substitute.For<WikiService>(
			Substitute.For<ILogger<WikiService>>(),
			Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(wikiService);

		var article = new WikiArticle("Test Article", "This is **bold** content", null);

		// Act
		var cut = RenderComponent<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "test-article")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		// Assert
		var markup = cut.Markup;
		// Markdown should be converted to HTML
		await Assert.That(markup).Contains("<strong>bold</strong>");
	}

	[Test]
	public async Task WikiDisplay_WithoutArticle_ShowsInfoMessage()
	{
		// Arrange
		var wikiService = Substitute.For<WikiService>(
			Substitute.For<ILogger<WikiService>>(),
			Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(wikiService);
		this.AddTestAuthorization();

		// Act
		var cut = RenderComponent<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "nonexistent")
			.Add(p => p.Article, null)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		// Assert
		var markup = cut.Markup;
		await Assert.That(markup).Contains("Nothing to see here");
	}

	[Test]
	public async Task WikiDisplay_WithoutArticle_WhenAuthorized_ShowsCreateOption()
	{
		// Arrange
		var wikiService = Substitute.For<WikiService>(
			Substitute.For<ILogger<WikiService>>(),
			Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(wikiService);
		
		var authContext = this.AddTestAuthorization();
		authContext.SetAuthorized("TestUser");

		// Act
		var cut = RenderComponent<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "new-page")
			.Add(p => p.Article, null)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		// Assert
		var markup = cut.Markup;
		await Assert.That(markup).Contains("does not exist yet");
		await Assert.That(markup).Contains("Create this Page");
	}

	[Test]
	public async Task WikiDisplay_WhenAuthorized_ShowsEditButton()
	{
		// Arrange
		var wikiService = Substitute.For<WikiService>(
			Substitute.For<ILogger<WikiService>>(),
			Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(wikiService);

		var authContext = this.AddTestAuthorization();
		authContext.SetAuthorized("TestUser");

		var article = new WikiArticle("Test Article", "Content", null);

		// Act
		var cut = RenderComponent<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "test-article")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		// Assert - Should have edit button (icon button with edit icon)
		var markup = cut.Markup;
		// MudBlazor renders icon buttons, checking for presence in markup
		await Assert.That(cut.FindAll("button").Count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task WikiDisplay_WhenNotAuthorized_DoesNotShowEditButton()
	{
		// Arrange
		var wikiService = Substitute.For<WikiService>(
			Substitute.For<ILogger<WikiService>>(),
			Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(wikiService);

		this.AddTestAuthorization(); // Not authorized

		var article = new WikiArticle("Test Article", "Content", null);

		// Act
		var cut = RenderComponent<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "test-article")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		// Assert - Should not have edit button when not authorized
		var buttons = cut.FindAll("button");
		await Assert.That(buttons.Count).IsEqualTo(0);
	}

	[Test]
	public async Task WikiDisplay_HomeSlug_DisplaysAsHero()
	{
		// Arrange
		var wikiService = Substitute.For<WikiService>(
			Substitute.For<ILogger<WikiService>>(),
			Substitute.For<IHttpClientFactory>());
		Services.AddSingleton(wikiService);

		var article = new WikiArticle("Home", "Welcome", null);

		// Act
		var cut = RenderComponent<WikiDisplay>(parameters => parameters
			.Add(p => p.Slug, "home")
			.Add(p => p.Article, article)
			.Add(p => p.ActivateEditMode, () => Task.CompletedTask));

		// Assert - Hero style should have different styling (background:inherit)
		var markup = cut.Markup;
		await Assert.That(markup).Contains("background:inherit");
	}
}
