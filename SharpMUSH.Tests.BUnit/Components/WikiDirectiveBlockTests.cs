using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using System.Net;
using System.Text;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// HttpMessageHandler that returns a canned response for every request,
/// recording the last request path for assertions.
/// </summary>
file sealed class CannedResponseHandler(HttpStatusCode status, string? json = null) : HttpMessageHandler
{
	public string? LastRequestPath { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		LastRequestPath = request.RequestUri?.PathAndQuery;

		var response = new HttpResponseMessage(status);
		if (json is not null)
		{
			response.Content = new StringContent(json, Encoding.UTF8, "application/json");
		}
		return Task.FromResult(response);
	}
}

/// <summary>
/// bUnit tests for <see cref="WikiDirectiveBlock"/> — the live-listing component
/// hydrating wiki directive placeholders (category / tag / pagelist / recent).
/// </summary>
public class WikiDirectiveBlockTests : BunitContext
{
	public WikiDirectiveBlockTests()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private void UseHandler(HttpMessageHandler handler)
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(
			new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		Services.AddSingleton(factory);
	}

	private const string CategoryJson =
		"""
		[
		  {"slug":"dragon_lore","title":"Dragon Lore","namespace":"main","updatedAt":"2026-01-02T10:00:00Z"},
		  {"slug":"old_gods","title":"Old Gods","namespace":"lore","updatedAt":"2026-01-03T11:30:00Z"}
		]
		""";

	[TUnit.Core.Test]
	public async Task CategoryDirective_WithPages_RendersHeaderAndPageLinks()
	{
		var handler = new CannedResponseHandler(HttpStatusCode.OK, CategoryJson);
		UseHandler(handler);

		var cut = Render<WikiDirectiveBlock>(p => p
			.Add(c => c.Directive, "category")
			.Add(c => c.Arg, "lore"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Dragon Lore"))
				throw new InvalidOperationException("Page list not rendered yet.");
		});

		await Assert.That(handler.LastRequestPath).IsEqualTo("/api/wiki/category/lore");

		await Assert.That(cut.Markup).Contains("wiki-directive-block");
		await Assert.That(cut.Markup).Contains("Category: lore");
		await Assert.That(cut.Markup).Contains("wiki-directive-list");

		// Canonical link form: /wiki/{ns}/{category}/{slug} (category defaults to general).
		await Assert.That(cut.Markup).Contains("href=\"/wiki/main/general/dragon_lore\"");
		await Assert.That(cut.Markup).Contains("Dragon Lore");
		await Assert.That(cut.Markup).Contains("href=\"/wiki/lore/general/old_gods\"");
		await Assert.That(cut.Markup).Contains("Old Gods");
	}

	[TUnit.Core.Test]
	public async Task CategoryDirective_WhenEndpointReturns404_ShowsEmptyState()
	{
		UseHandler(new CannedResponseHandler(HttpStatusCode.NotFound));

		var cut = Render<WikiDirectiveBlock>(p => p
			.Add(c => c.Directive, "category")
			.Add(c => c.Arg, "nonexistent"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("No pages yet."))
				throw new InvalidOperationException("Empty state not rendered yet.");
		});

		await Assert.That(cut.Markup).Contains("No pages yet.");
		await Assert.That(cut.Markup).DoesNotContain("wiki-directive-list");
	}
}
