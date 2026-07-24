using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
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
		Services.AddMudServices();
		Services.AddLocalization();
		// NavMenu (and other chrome) inject ITerminalService to gate character-scoped links on
		// connection state, and CharacterSwitchService to run the shared switch flow. In production
		// the interface and the concrete TerminalServiceHost facade are the SAME singleton, so mirror
		// that: register the concrete hosts (wrapping disconnected stubs so nothing hits the network)
		// and alias the interfaces to them. Only the concrete hosts expose RecreateAsync, which
		// CharacterSwitchService's constructor requires.
		var terminalHost = new TerminalServiceHost(() => Substitute.For<ITerminalService>());
		var playTerminalHost = new PlayTerminalServiceHost(() => Substitute.For<IPlayTerminalService>());
		Services.AddSingleton(terminalHost);
		Services.AddSingleton(playTerminalHost);
		Services.AddSingleton<ITerminalService>(terminalHost);
		Services.AddSingleton<IPlayTerminalService>(playTerminalHost);
		// NavMenu renders <ApplicationNavLinks>, which calls ApplicationRegistryClient.ListAsync()
		// on init. Back it with a stub HTTP factory that returns an empty list so chrome renders
		// without a live API; application-specific tests register their own client.
		Services.AddSingleton(new ApplicationRegistryClient(
			StubFactoryReturningEmptyList(), NullLogger<ApplicationRegistryClient>.Instance));
		// NavMenu's profile card injects AccountAuthService to show the signed-in display name.
		// Its HTTP/JS dependencies are never exercised during a render (only properties are read),
		// so stubs suffice; Username/Characters default to empty.
		Services.AddSingleton(new AccountAuthService(
			StubFactoryReturningEmptyList(), Substitute.For<IJSRuntime>(), NullLogger<AccountAuthService>.Instance));
		// NavMenu's account panel injects CharacterSwitchService. Render tests never invoke a switch,
		// but it must be resolvable; it depends on the AccountAuthService above and the game-hub
		// connection state (reconnected on a switch).
		Services.AddSingleton(Substitute.For<SharpMUSH.Library.Services.Interfaces.IConnectionStateService>());
		Services.AddSingleton<CharacterSwitchService>();
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
