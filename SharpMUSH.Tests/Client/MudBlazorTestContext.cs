using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client;

/// <summary>
/// Base class for Blazor component tests that use MudBlazor components.
/// Provides proper setup for MudBlazor services required for testing.
/// </summary>
public abstract class MudBlazorTestContext : BunitContext
{
	protected MudBlazorTestContext()
	{
		// Add MudBlazor services required for component rendering
		Services.AddMudServices();
		// Add localization services required for IStringLocalizer<SharedResource>
		Services.AddLocalization();
		// NavMenu (and other chrome) inject ITerminalService to gate character-scoped
		// links on connection state; a disconnected stub is enough for rendering tests.
		Services.AddSingleton(Substitute.For<ITerminalService>());
		// NavMenu renders <ApplicationNavLinks>, which calls ApplicationRegistryClient.ListAsync()
		// on init. Back it with a stub HTTP factory that returns an empty list so chrome renders
		// without a live API; application-specific tests register their own client.
		Services.AddSingleton(new ApplicationRegistryClient(
			StubFactoryReturningEmptyList(), NullLogger<ApplicationRegistryClient>.Instance));
	}

	private static IHttpClientFactory StubFactoryReturningEmptyList()
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>())
			.Returns(_ => new HttpClient(new EmptyJsonArrayHandler()) { BaseAddress = new Uri("http://localhost/") });
		return factory;
	}

	private sealed class EmptyJsonArrayHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("[]", Encoding.UTF8, "application/json")
			});
	}
}
