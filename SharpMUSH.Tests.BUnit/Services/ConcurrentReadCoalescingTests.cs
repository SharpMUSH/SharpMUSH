using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The home page renders several widgets that read the same endpoints on the same frame; each pair
/// below used to put two identical requests on the wire. The services are singletons, so while one
/// read is in flight another for the same thing joins it — and a read after it has finished goes
/// back to the network, because nothing is cached.
/// </summary>
public class ConcurrentReadCoalescingTests : TrackingTestContext
{
	private static HttpResponseMessage EmptyList() =>
		new(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) };

	private IHttpClientFactory Factory(HttpMessageHandler handler)
	{
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		return factory;
	}

	[TUnit.Core.Test]
	public async Task SceneService_ConcurrentActiveSceneReads_FetchOnce()
	{
		var handler = new GatedHttpHandler(_ => EmptyList());
		var service = new SceneService(Factory(handler));

		var first = service.GetActiveScenesAsync();
		var second = service.GetActiveScenesAsync();
		handler.Release();
		await Task.WhenAll(first, second);

		await Assert.That(handler.CallsTo("/api/scenes?filter=active&count=50")).IsEqualTo(1);

		await service.GetActiveScenesAsync();
		await Assert.That(handler.CallsTo("/api/scenes?filter=active&count=50")).IsEqualTo(2);
	}

	/// <summary>
	/// A read asked for after a reported change does not join one that started before it. That earlier
	/// request may have been answered before the change, so sharing it handed the sidebar's reload the
	/// list without the scene that had just been started. Reads on the same side of a change still share.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneService_AReadAfterAReportedChange_DoesNotJoinOneFromBefore()
	{
		var handler = new GatedHttpHandler(_ => EmptyList());
		var service = new SceneService(Factory(handler));

		var before = service.GetActiveScenesAsync();
		service.ReportChanged();
		var after = service.GetActiveScenesAsync();
		var alsoAfter = service.GetActiveScenesAsync();
		handler.Release();
		await Task.WhenAll(before, after, alsoAfter);

		await Assert.That(handler.CallsTo("/api/scenes?filter=active&count=50")).IsEqualTo(2);
	}

	[TUnit.Core.Test]
	public async Task SceneService_DifferentFilters_AreNotCoalesced()
	{
		var handler = new GatedHttpHandler(_ => EmptyList());
		var service = new SceneService(Factory(handler));

		var active = service.GetActiveScenesAsync();
		var recent = service.GetRecentScenesAsync();
		handler.Release();
		await Task.WhenAll(active, recent);

		await Assert.That(handler.Calls).IsEqualTo(2);
	}

	[TUnit.Core.Test]
	public async Task CharacterDirectory_ConcurrentOnlineReads_FetchOnce()
	{
		var handler = new GatedHttpHandler(_ => EmptyList());
		var service = new CharacterDirectoryService(Factory(handler), NullLogger<CharacterDirectoryService>.Instance);

		var first = service.ListOnlineAsync();
		var second = service.ListOnlineAsync();
		handler.Release();
		await Task.WhenAll(first, second);

		await Assert.That(handler.CallsTo("/http/online")).IsEqualTo(1);

		// Within its short memo (README §6.3's several readers per page) a later read is answered from it.
		await service.ListOnlineAsync();
		await Assert.That(handler.CallsTo("/http/online")).IsEqualTo(1);
	}

	/// <summary>
	/// A caller navigating away cancels its own wait, not the read another widget is still waiting on.
	/// </summary>
	[TUnit.Core.Test]
	public async Task CharacterDirectory_OneCallerCancelling_LeavesTheOtherItsAnswer()
	{
		var handler = new GatedHttpHandler(_ => EmptyList());
		var service = new CharacterDirectoryService(Factory(handler), NullLogger<CharacterDirectoryService>.Instance);
		using var cts = new CancellationTokenSource();

		var abandoned = service.ListOnlineAsync(cts.Token);
		var kept = service.ListOnlineAsync();
		await cts.CancelAsync();
		await Assert.That(async () => await abandoned).Throws<OperationCanceledException>();

		handler.Release();
		var result = await kept;

		await Assert.That(result is IReadOnlyList<CharacterDirectoryService.CharacterSummary>).IsTrue();
		await Assert.That(handler.CallsTo("/http/online")).IsEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task CharacterDirectory_RosterAndOnline_AreNotCoalesced()
	{
		var handler = new GatedHttpHandler(_ => EmptyList());
		var service = new CharacterDirectoryService(Factory(handler), NullLogger<CharacterDirectoryService>.Instance);

		var roster = service.ListAsync();
		var online = service.ListOnlineAsync();
		handler.Release();
		await Task.WhenAll(roster, online);

		await Assert.That(handler.CallsTo("/http/characters")).IsEqualTo(1);
		await Assert.That(handler.CallsTo("/http/online")).IsEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task ApplicationRegistry_ConcurrentLists_FetchOnce()
	{
		var handler = new GatedHttpHandler(_ => EmptyList());
		var client = new ApplicationRegistryClient(Factory(handler), NullLogger<ApplicationRegistryClient>.Instance);

		var first = client.ListAsync();
		var second = client.ListAsync();
		handler.Release();
		await Task.WhenAll(first, second);

		await Assert.That(handler.CallsTo("/api/applications")).IsEqualTo(1);

		// The rail, the drawer and the section sidebars re-read on render; within the list's short memo a
		// later read is answered from it, and a write through the client clears it.
		await client.ListAsync();
		await Assert.That(handler.CallsTo("/api/applications")).IsEqualTo(1);
	}

	/// <summary>
	/// MainLayout, the global terminal and the quickstart widget each ask for the roster on first
	/// render, and their "roster still empty" guards all pass before any answer lands.
	/// </summary>
	[TUnit.Core.Test]
	public async Task AccountAuth_ConcurrentRosterReads_FetchOnce()
	{
		var handler = new GatedHttpHandler(
			request => request.RequestUri!.AbsolutePath == "/api/auth/account-login"
				? new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(new
					{
						accountId = "acct-1",
						username = "headwiz",
						characters = Array.Empty<object>(),
						accountSessionToken = "session-token-1",
						mustChangePassword = false,
						role = (string?)null,
						permissions = Array.Empty<string>(),
					})
				}
				: new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(new[]
					{
						new { dbrefNumber = 7, creationTime = 1000L, name = "Wizard", flags = "" },
					})
				},
			hold: request => request.RequestUri!.AbsolutePath == "/api/account/characters");
		var service = new AccountAuthService(Factory(handler), Substitute.For<IJSRuntime>(), NullLogger<AccountAuthService>.Instance, []);
		await service.InitAsync();
		await service.LoginAsync("headwiz", "password");

		var reads = new[] { service.GetCharactersAsync(), service.GetCharactersAsync(), service.GetCharactersAsync() };
		handler.Release();
		await Task.WhenAll(reads);

		await Assert.That(handler.CallsTo("/api/account/characters")).IsEqualTo(1);
		await Assert.That(service.Characters.Count).IsEqualTo(1);

		await service.GetCharactersAsync();
		await Assert.That(handler.CallsTo("/api/account/characters")).IsEqualTo(2);
	}
}
