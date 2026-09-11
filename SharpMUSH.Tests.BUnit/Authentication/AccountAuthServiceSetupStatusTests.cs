using System.Net;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit.Authentication;

/// <summary>
/// Fakes api/setup/status returning a sequence of pre-scripted responses, one per call, so a
/// test can simulate a transient failure (500, or an HTML SPA-fallback body masquerading as the
/// API route) followed by a well-formed success. Falls back to the last response if called more
/// times than scripted.
/// </summary>
file sealed class SequencedSetupStatusHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
	private int _callCount;

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var index = Math.Min(_callCount, responses.Length - 1);
		_callCount++;
		return Task.FromResult(responses[index]);
	}
}

/// <summary>
/// Pins the fix for the "transient setup-status failure permanently hides the first-run wizard"
/// bug: <see cref="AccountAuthService.NeedsSetupAsync"/> must answer with its failed arm (never a
/// false negative) whenever the server call fails or the body can't be parsed, so callers like
/// MainLayout's EnsureAccountRoutingAsync know to retry on the next navigation instead of
/// latching a stale "setup already done."
/// </summary>
public class AccountAuthServiceSetupStatusTests : TrackingBunitContext
{
	[TUnit.Core.Test]
	public async Task NeedsSetupAsync_ServerError_ReturnsFailureNotFalse()
	{
		using var http = new HttpClient(new SequencedSetupStatusHandler(
			new HttpResponseMessage(HttpStatusCode.InternalServerError)))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		};
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());

		var result = await service.NeedsSetupAsync();

		await Assert.That(result.Value).IsTypeOf<Error>();
	}

	[TUnit.Core.Test]
	public async Task NeedsSetupAsync_NonJsonSpaFallbackBody_ReturnsFailureNotFalse()
	{
		// Simulates a stale dev server / proxy returning the SPA's index.html for the API route
		// instead of JSON, as has been seen in the field.
		using var http = new HttpClient(new SequencedSetupStatusHandler(
			new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<!doctype html><html>...</html>") }))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		};
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());

		var result = await service.NeedsSetupAsync();

		await Assert.That(result.Value).IsTypeOf<Error>();
	}

	/// <summary>
	/// A failing first attempt followed by a succeeding second one — mirroring one transient hiccup
	/// during EnsureAccountRoutingAsync's retry loop, which never caches anything but a definitive
	/// result. A failed call that answered `false` would be cached forever, and the wizard would never
	/// be reached on the second, successful attempt.
	/// </summary>
	[TUnit.Core.Test]
	public async Task NeedsSetupAsync_FailsThenSucceeds_RetryReachesWizard()
	{
		using var http = new HttpClient(new SequencedSetupStatusHandler(
			new HttpResponseMessage(HttpStatusCode.InternalServerError),
			new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContentOf(needsSetup: true) }))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		};
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());

		// Simulates MainLayout's EnsureAccountRoutingAsync guard: only the success arm is ever
		// cached, so a failure on the first navigation leaves the next navigation free to retry.
		bool? cachedNeedsSetup = null;

		var first = await service.NeedsSetupAsync();
		if (first is bool firstAnswer) cachedNeedsSetup = firstAnswer;
		await Assert.That(first.Value).IsTypeOf<Error>();
		await Assert.That(cachedNeedsSetup).IsNull();

		var second = await service.NeedsSetupAsync();
		if (second is bool secondAnswer) cachedNeedsSetup = secondAnswer;
		await Assert.That(second.Value).IsTypeOf<bool>();
		await Assert.That(second is true).IsTrue();
		await Assert.That(cachedNeedsSetup).IsTrue();
	}

	private static System.Net.Http.Json.JsonContent JsonContentOf(bool needsSetup) =>
		System.Net.Http.Json.JsonContent.Create(new { needsSetup });
}
