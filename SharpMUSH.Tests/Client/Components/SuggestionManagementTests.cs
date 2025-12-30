using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Library.ExpandedObjectData;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the SuggestionManagement Blazor component.
/// </summary>
public class SuggestionManagementTests : MudBlazorTestContext
{
	[Test]
	public async Task SuggestionManagement_InitialLoad_DisplaysEmptyState()
	{
		// Arrange
		var httpClient = CreateMockHttpClient(new SuggestionData());
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();

		// Assert
		var alert = cut.Find(".mud-alert-info");
		await Assert.That(alert.TextContent).Contains("No suggestion categories defined");
	}

	[Test]
	public async Task SuggestionManagement_WithCategories_DisplaysCategoryList()
	{
		// Arrange
		var suggestionData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create", "@destroy", "@dig" } },
				{ "functions", new HashSet<string> { "add", "sub" } }
			}
		};
		
		var httpClient = CreateMockHttpClient(suggestionData);
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();
		await Task.Delay(100); // Wait for async load

		// Assert
		var heading = cut.Find("h2");
		await Assert.That(heading.TextContent).Contains("Categories (2)");
	}

	[Test]
	public async Task SuggestionManagement_Statistics_DisplaysCorrectCounts()
	{
		// Arrange
		var suggestionData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create", "@destroy", "@dig" } },
				{ "functions", new HashSet<string> { "add", "sub" } }
			}
		};
		
		var httpClient = CreateMockHttpClient(suggestionData);
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();
		await Task.Delay(100); // Wait for async load

		// Assert - Check statistics
		var statValues = cut.FindAll(".stat-value");
		await Assert.That(statValues.Count).IsGreaterThanOrEqualTo(3);
		
		// Should show 2 categories
		await Assert.That(statValues[0].TextContent.Trim()).IsEqualTo("2");
		
		// Should show 5 total words (3 + 2)
		await Assert.That(statValues[1].TextContent.Trim()).IsEqualTo("5");
	}

	[Test]
	public async Task SuggestionManagement_ClickRefresh_ReloadsData()
	{
		// Arrange
		var suggestionData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create" } }
			}
		};
		
		var httpClient = CreateMockHttpClient(suggestionData);
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();
		await Task.Delay(100);
		
		// Find and click refresh button
		var refreshButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Refresh"));
		await Assert.That(refreshButton).IsNotNull();
		
		refreshButton!.Click();
		await Task.Delay(100);

		// Assert - Component should still render
		await Assert.That(cut.Find("h1").TextContent).Contains("Suggestion Management");
	}

	[Test]
	public async Task SuggestionManagement_Title_DisplaysCorrectly()
	{
		// Arrange
		var httpClient = CreateMockHttpClient(new SuggestionData());
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();

		// Assert
		var heading = cut.Find("h1");
		await Assert.That(heading.TextContent).Contains("Suggestion Management");
	}

	[Test]
	public async Task SuggestionManagement_Description_DisplaysCorrectly()
	{
		// Arrange
		var httpClient = CreateMockHttpClient(new SuggestionData());
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();

		// Assert
		var description = cut.Find(".mud-card-content p");
		await Assert.That(description.TextContent).Contains("spell-check and suggestion categories");
	}

	[Test]
	public async Task SuggestionManagement_HasAddCategoryButton()
	{
		// Arrange
		var httpClient = CreateMockHttpClient(new SuggestionData());
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();

		// Assert
		var addButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Add Category"));
		await Assert.That(addButton).IsNotNull();
	}

	[Test]
	public async Task SuggestionManagement_HasRefreshButton()
	{
		// Arrange
		var httpClient = CreateMockHttpClient(new SuggestionData());
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();

		// Assert
		var refreshButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Refresh"));
		await Assert.That(refreshButton).IsNotNull();
	}

	[Test]
	public async Task SuggestionManagement_WithMultipleCategories_DisplaysAllCategories()
	{
		// Arrange
		var suggestionData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create" } },
				{ "functions", new HashSet<string> { "add" } },
				{ "help", new HashSet<string> { "intro" } }
			}
		};
		
		var httpClient = CreateMockHttpClient(suggestionData);
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();
		await Task.Delay(100);

		// Assert
		var panelTitles = cut.FindAll(".panel-title, .mud-expand-panel-text");
		var categoryNames = panelTitles.Select(p => p.TextContent.ToLower()).ToList();
		
		await Assert.That(categoryNames.Any(c => c.Contains("commands"))).IsTrue();
		await Assert.That(categoryNames.Any(c => c.Contains("functions"))).IsTrue();
		await Assert.That(categoryNames.Any(c => c.Contains("help"))).IsTrue();
	}

	[Test]
	public async Task SuggestionManagement_CategoryWithWords_DisplaysWordCount()
	{
		// Arrange
		var suggestionData = new SuggestionData
		{
			Categories = new Dictionary<string, HashSet<string>>
			{
				{ "commands", new HashSet<string> { "@create", "@destroy", "@dig", "@emit" } }
			}
		};
		
		var httpClient = CreateMockHttpClient(suggestionData);
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();
		await Task.Delay(100);

		// Assert
		var chips = cut.FindAll(".mud-chip");
		var wordCountChip = chips.FirstOrDefault(c => c.TextContent.Contains("words"));
		
		await Assert.That(wordCountChip).IsNotNull();
		await Assert.That(wordCountChip!.TextContent).Contains("4");
	}

	[Test]
	public async Task SuggestionManagement_LoadingState_ShowsProgressIndicator()
	{
		// Arrange
		var httpClient = CreateMockHttpClient(new SuggestionData(), delayMs: 1000);
		Services.AddScoped(_ => httpClient);

		// Act
		var cut = Render<SuggestionManagement>();

		// Assert - Should show loading indicator before data loads
		var progressIndicator = cut.FindAll(".mud-progress-circular");
		await Assert.That(progressIndicator.Count).IsGreaterThan(0);
	}

	/// <summary>
	/// Creates a mock HttpClient that returns the specified SuggestionData.
	/// </summary>
	private HttpClient CreateMockHttpClient(SuggestionData data, int delayMs = 0)
	{
		var messageHandler = new MockHttpMessageHandler(data, delayMs);
		return new HttpClient(messageHandler)
		{
			BaseAddress = new Uri("http://localhost/")
		};
	}

	/// <summary>
	/// Mock message handler for HttpClient testing.
	/// </summary>
	private class MockHttpMessageHandler : HttpMessageHandler
	{
		private readonly SuggestionData _data;
		private readonly int _delayMs;

		public MockHttpMessageHandler(SuggestionData data, int delayMs = 0)
		{
			_data = data;
			_delayMs = delayMs;
		}

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			if (_delayMs > 0)
			{
				await Task.Delay(_delayMs, cancellationToken);
			}

			if (request.RequestUri?.AbsolutePath == "/api/suggestion")
			{
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(_data)
				};
			}

			return new HttpResponseMessage(HttpStatusCode.NotFound);
		}
	}
}
