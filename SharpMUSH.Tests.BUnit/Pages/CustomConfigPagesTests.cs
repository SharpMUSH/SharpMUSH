using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Every request is refused; these tests look only at the page chrome.</summary>
file sealed class RefusingHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}

/// <summary>
/// The four config pages with their own markup (Sitelock, Banned names, Restrictions, Import) share
/// the D1 plain page header with the resx kicker, instead of four copies of the same header CSS
/// and a literal kicker.
/// </summary>
public class CustomConfigPagesTests : TrackingBunitContext
{
	public CustomConfigPagesTests()
	{
		var client = Track(new HttpClient(new RefusingHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<SitelockService>()
			.AddSingleton<BannedNamesService>()
			.AddSingleton<RestrictionsService>()
			.AddSingleton<AdminConfigService>()
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private async Task AssertPlainHeader<T>(IRenderedComponent<T> cut, string title) where T : Microsoft.AspNetCore.Components.IComponent
	{
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo(title);
		await Assert.That(cut.Find(".kit-page-kicker").TextContent).IsEqualTo("Server · Configuration");
		await Assert.That(cut.FindAll(".config-section-header").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Sitelock_UsesThePlainHeader_WithItsCodeList()
	{
		var cut = Render<Sitelock>();
		await AssertPlainHeader(cut, "Sitelock Rules");
		await Assert.That(cut.FindAll(".kit-page-desc code").Count).IsGreaterThan(0);
	}

	[Test]
	public async Task BannedNames_UsesThePlainHeader()
	{
		var cut = Render<BannedNames>();
		await AssertPlainHeader(cut, "Banned Player Names");
	}

	[Test]
	public async Task Restrictions_UsesThePlainHeader()
	{
		var cut = Render<Restrictions>();
		await AssertPlainHeader(cut, "Command & Function Restrictions");
	}

	[Test]
	public async Task Import_UsesThePlainHeader()
	{
		var cut = Render<ImportConfig>();
		await AssertPlainHeader(cut, "Import Configuration");
	}
}
