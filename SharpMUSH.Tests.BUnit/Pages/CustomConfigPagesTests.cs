using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Only ASCII translations load successfully, for field focus; other tests inspect page chrome.</summary>
file sealed class CustomConfigHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(request.RequestUri!.AbsolutePath == "/api/asciitranslations"
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new Dictionary<string, string>()) }
			: new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}

file sealed class DeferredConfigHandler(Task<HttpResponseMessage> response) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response;
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
		var client = Track(new HttpClient(new CustomConfigHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<SitelockService>()
			.AddSingleton<BannedNamesService>()
			.AddSingleton<AsciiTranslationsService>()
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
	public async Task AsciiTranslationsSearchResultIdentifiesAndFocusesItsCustomControl()
	{
		Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/config/asciitranslations#config-option-AsciiTranslations.Translations");
		var cut = Render<AsciiTranslations>();
		cut.WaitForAssertion(() => cut.Find("input"));
		await Assert.That(cut.Find("[data-config-option='AsciiTranslations.Translations']").QuerySelector("input") is not null).IsTrue();
		cut.WaitForState(() => JSInterop.Invocations.Any(call => call.Identifier == "sharpmushLayout.focusConfigurationOption" && Equals(call.Arguments[0], "AsciiTranslations.Translations")));
	}

	[Test]
	[Arguments(typeof(AsciiTranslations), "/admin/config/asciitranslations", "AsciiTranslations.Translations", "{}")]
	[Arguments(typeof(BannedNames), "/admin/config/bannednames", "BannedNames.BannedNames", "[]")]
	[Arguments(typeof(Sitelock), "/admin/config/sitelock", "SitelockRules.Rules", "{}")]
	[Arguments(typeof(Restrictions), "/admin/config/restrictions", "Restriction.CommandRestrictions", "{}")]
	[Arguments(typeof(Restrictions), "/admin/config/restrictions", "Restriction.FunctionRestrictions", "{}")]
	public async Task CustomListFocusWaitsForTheLoadedInput(Type page, string route, string key, string json)
	{
		var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		var client = Track(new HttpClient(new DeferredConfigHandler(pending.Task)) { BaseAddress = new Uri("https://localhost/") });
		Services.GetRequiredService<IHttpClientFactory>().CreateClient(Arg.Any<string>()).Returns(client);
		Services.GetRequiredService<NavigationManager>().NavigateTo(route + "#config-option-" + key);
		var cut = Render<DynamicComponent>(p => p.Add(x => x.Type, page));
		await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
		await Assert.That(JSInterop.Invocations.Count(call => call.Identifier == "sharpmushLayout.focusConfigurationOption")).IsEqualTo(0);
		pending.SetResult(new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
		cut.WaitForState(() => cut.FindAll("input").Count > 0);
		cut.WaitForState(() => JSInterop.Invocations.Any(call => call.Identifier == "sharpmushLayout.focusConfigurationOption" && Equals(call.Arguments[0], key)));
		await Assert.That(cut.Find("[data-config-option='" + key + "']").QuerySelector("input") is not null).IsTrue();
	}

	[Test]
	public async Task Import_UsesThePlainHeader()
	{
		var cut = Render<ImportConfig>();
		await AssertPlainHeader(cut, "Import Configuration");
	}
}
