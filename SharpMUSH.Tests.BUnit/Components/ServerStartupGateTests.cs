using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using System.Net;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// A GET handler that fails (simulating an unreachable/not-yet-up server) for its first
/// <see cref="FailCount"/> calls, then answers 200 forever after — mirroring a server that
/// finishes booting partway through the client's polling.
/// </summary>
file sealed class FlakyThenHealthyHandler(int failCount) : HttpMessageHandler
{
	public int CallCount { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		CallCount++;
		var response = CallCount > failCount
			? new HttpResponseMessage(HttpStatusCode.OK)
			: new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
		return Task.FromResult(response);
	}
}

/// <summary>Always fails — the server never comes up during the test.</summary>
file sealed class AlwaysFailingHandler : HttpMessageHandler
{
	public int CallCount { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		CallCount++;
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
	}
}

/// <summary>
/// Coverage for the "Game is starting up" gate wrapped around the app in App.razor (inside
/// ThemeProvider, around CascadingAuthenticationState — see <see cref="ServerStartupGate"/>'s
/// own doc comment for why that nesting matters). No fabricated identity should ever be visible
/// through it: while the server is unreachable, ChildContent (which is where an
/// AuthenticationStateProvider would eventually run) must not render at all.
/// </summary>
public class ServerStartupGateTests : BunitContext
{
	private const string ChildMarker = "child-content-rendered";

	public ServerStartupGateTests()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private void RegisterApiClient(HttpMessageHandler handler)
	{
		var apiClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		Services.AddSingleton(factory);
	}

	[TUnit.Core.Test]
	public async Task WhileServerUnreachable_RendersStartingView_NotChildContent()
	{
		var handler = new AlwaysFailingHandler();
		RegisterApiClient(handler);

		var cut = Render<ServerStartupGate>(p => p
			.Add(x => x.PollInterval, TimeSpan.FromMilliseconds(10))
			.Add(x => x.ChildContent, (RenderFragment)(builder =>
			{
				builder.OpenElement(0, "div");
				builder.AddContent(1, ChildMarker);
				builder.CloseElement();
			})));

		// Give the poll loop a few iterations to run — it must keep failing and keep showing
		// the startup screen throughout. A failed probe never calls StateHasChanged (nothing
		// visually changes), so bUnit's render-driven WaitForAssertion has nothing to react to
		// here — a plain delay is the right tool.
		await Task.Delay(TimeSpan.FromMilliseconds(300));

		await Assert.That(handler.CallCount).IsGreaterThanOrEqualTo(3);
		await Assert.That(cut.Markup).DoesNotContain(ChildMarker);
		await Assert.That(cut.Markup).Contains("Game is starting up");
	}

	[TUnit.Core.Test]
	public async Task FailsTwiceThenSucceeds_ChildContentRendersAfterPolls()
	{
		var handler = new FlakyThenHealthyHandler(failCount: 2);
		RegisterApiClient(handler);

		var cut = Render<ServerStartupGate>(p => p
			.Add(x => x.PollInterval, TimeSpan.FromMilliseconds(10))
			.Add(x => x.ChildContent, (RenderFragment)(builder =>
			{
				builder.OpenElement(0, "div");
				builder.AddContent(1, ChildMarker);
				builder.CloseElement();
			})));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains(ChildMarker)) throw new InvalidOperationException("not healthy yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains(ChildMarker);
		await Assert.That(cut.Markup).DoesNotContain("Game is starting up");
		// Two failures, then the third call is the success that flips it healthy.
		await Assert.That(handler.CallCount).IsGreaterThanOrEqualTo(3);
	}

	[TUnit.Core.Test]
	public async Task OnceHealthy_LaterHandlerFailureDoesNotReGate()
	{
		// Succeeds immediately, then flips to failing — once the gate has gone healthy it must
		// stop probing entirely, so it can never observe (and act on) that later failure.
		var handler = new FlakyThenHealthyHandler(failCount: 0);
		RegisterApiClient(handler);

		var cut = Render<ServerStartupGate>(p => p
			.Add(x => x.PollInterval, TimeSpan.FromMilliseconds(10))
			.Add(x => x.ChildContent, (RenderFragment)(builder =>
			{
				builder.OpenElement(0, "div");
				builder.AddContent(1, ChildMarker);
				builder.CloseElement();
			})));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains(ChildMarker)) throw new InvalidOperationException("not healthy yet");
		}, TimeSpan.FromSeconds(5));

		var callsWhenHealthy = handler.CallCount;

		// Force a re-render (e.g. as a parent StateHasChanged would) well past another poll
		// interval — a re-gating implementation would drop back to the startup screen here.
		await Task.Delay(100);
		cut.Render();

		await Assert.That(cut.Markup).Contains(ChildMarker);
		await Assert.That(cut.Markup).DoesNotContain("Game is starting up");
		// No further probing happened once healthy — the loop truly stopped, not just "happened
		// to keep succeeding".
		await Assert.That(handler.CallCount).IsEqualTo(callsWhenHealthy);
	}
}
