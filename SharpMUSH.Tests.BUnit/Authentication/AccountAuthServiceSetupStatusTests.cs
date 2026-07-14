using System.Net;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

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
/// bug: <see cref="AccountAuthService.NeedsSetupAsync"/> must return <c>null</c> (never a false
/// negative) whenever the server call fails or the body can't be parsed, so callers like
/// MainLayout's EnsureAccountRoutingAsync know to retry on the next navigation instead of
/// latching a stale "setup already done."
/// </summary>
public class AccountAuthServiceSetupStatusTests : BunitContext
{
	[TUnit.Core.Test]
	public async Task NeedsSetupAsync_ServerError_ReturnsNullNotFalse()
	{
		using var http = new HttpClient(new SequencedSetupStatusHandler(
			new HttpResponseMessage(HttpStatusCode.InternalServerError)))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		};
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);

		var result = await service.NeedsSetupAsync();

		await Assert.That(result).IsNull();
	}

	[TUnit.Core.Test]
	public async Task NeedsSetupAsync_NonJsonSpaFallbackBody_ReturnsNullNotFalse()
	{
		// Simulates a stale dev server / proxy returning the SPA's index.html for the API route
		// instead of JSON — the historical field observation behind this bug.
		using var http = new HttpClient(new SequencedSetupStatusHandler(
			new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<!doctype html><html>...</html>") }))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		};
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);

		var result = await service.NeedsSetupAsync();

		await Assert.That(result).IsNull();
	}

	/// <summary>
	/// The essential regression pin: a failing first attempt (null) followed by a succeeding
	/// second attempt (true) — mirroring one transient hiccup during EnsureAccountRoutingAsync's
	/// retry loop, which never caches anything but a definitive result. Before the fix, the first
	/// failed call would have returned `false` and gotten cached forever, so the wizard would
	/// never be reached on the second, successful attempt.
	/// </summary>
	[TUnit.Core.Test]
	public async Task NeedsSetupAsync_FailsThenSucceeds_NullThenTrue_RetryReachesWizard()
	{
		using var http = new HttpClient(new SequencedSetupStatusHandler(
			new HttpResponseMessage(HttpStatusCode.InternalServerError),
			new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContentOf(needsSetup: true) }))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		};
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);

		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);

		// Simulates MainLayout's EnsureAccountRoutingAsync guard: only a non-null result is
		// ever cached, so a null result on the first navigation leaves the next navigation free
		// to retry.
		bool? cachedNeedsSetup = null;

		var first = await service.NeedsSetupAsync();
		if (first is not null) cachedNeedsSetup = first;
		await Assert.That(first).IsNull();
		await Assert.That(cachedNeedsSetup).IsNull();

		var second = await service.NeedsSetupAsync();
		if (second is not null) cachedNeedsSetup = second;
		await Assert.That(second).IsTrue();
		await Assert.That(cachedNeedsSetup).IsTrue();
	}

	private static System.Net.Http.Json.JsonContent JsonContentOf(bool needsSetup) =>
		System.Net.Http.Json.JsonContent.Create(new { needsSetup });
}
