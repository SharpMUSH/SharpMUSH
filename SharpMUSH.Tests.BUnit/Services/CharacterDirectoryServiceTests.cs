using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>Never answers, so the request only ends when something cancels it.</summary>
file sealed class HangingHandler : HttpMessageHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		throw new UnreachableException();
	}
}

/// <summary>Serves a two-character roster on http/characters, for the name-resolution cases.</summary>
file sealed class RosterHandler : HttpMessageHandler
{
	private record Row(string Name, string Objid, long Created, string Category);

	private static readonly Row[] Roster =
	[
		new("Castor", "#12:1200", 1200, ""),
		new("Solitaire", "#11:1100", 1100, ""),
	];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(request.RequestUri!.AbsolutePath == "/http/characters"
			? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Roster) }
			: new HttpResponseMessage(HttpStatusCode.NotFound));
}

/// <summary>Serves a roster where one character has a picture, counting the reads.</summary>
file sealed class PictureRosterHandler : HttpMessageHandler
{
	private record Row(string Name, string Objid, long Created, string Category, string? Image);

	public int Reads { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Reads++;
		Row[] roster =
		[
			new("Castor", "#12:1200", 1200, "", "/api/wiki-assets/c/castor.jpg"),
			new("Solitaire", "#11:1100", 1100, "", ""),
		];
		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(roster) });
	}
}

/// <summary>A redefined handler's answer with a null row and a row missing its objid among real ones.</summary>
file sealed class MalformedRowsHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""[null, {"name":"Ghost"}, {"name":"Castor","objid":"#12:1200","created":1200,"image":"/c.jpg"}]""",
				System.Text.Encoding.UTF8, "application/json")
		});
}

/// <summary>Answers immediately with an empty list, for the caller-cancellation cases.</summary>
file sealed class EmptyListHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) });
}

/// <summary>
/// Directory outcomes that are different facts and must not share an answer — the service's whole
/// reason for returning a discriminated union.
///
/// The two ways a read can end in an <see cref="OperationCanceledException"/> are the sharpest case.
///
/// A read that outlives <see cref="HttpClient.Timeout"/> is a failed request — the game is slow or
/// gone — and belongs in the failed arm with every other failed request. It reaches the catch as a
/// TaskCanceledException, which is not an InvalidOperationException and was not covered by the
/// exception filter, so it escaped out of a component's OnInitializedAsync and took the page down:
/// exactly the "unavailable renders as broken" failure this service exists to prevent.
///
/// A cancellation the caller asked for is not a statement about the game at all — the caller
/// stopped wanting an answer (a navigation away, a superseded render) — and reporting "unavailable"
/// for it would be the same class of lie in the other direction. It propagates.
/// </summary>
public class CharacterDirectoryServiceTests : TrackingTestContext
{
	private CharacterDirectoryService Build(HttpMessageHandler handler, TimeSpan? timeout = null)
	{
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		if (timeout is { } value)
		{
			client.Timeout = value;
		}

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		return new CharacterDirectoryService(factory, NullLogger<CharacterDirectoryService>.Instance);
	}

	[TUnit.Core.Test]
	public async Task ListAsync_RequestTimeout_ReturnsTheFailedArm()
	{
		var service = Build(new HangingHandler(), TimeSpan.FromMilliseconds(50));

		var result = await service.ListAsync();

		await Assert.That(result.Expect<ApiFailure>().Kind).IsEqualTo(ApiFailureKind.Transport);
	}

	[TUnit.Core.Test]
	public async Task ListOnlineAsync_RequestTimeout_ReturnsTheFailedArm()
	{
		var service = Build(new HangingHandler(), TimeSpan.FromMilliseconds(50));

		var result = await service.ListOnlineAsync();

		await Assert.That(result.Expect<ApiFailure>().Kind).IsEqualTo(ApiFailureKind.Transport);
	}

	[TUnit.Core.Test]
	public async Task ResolveObjidAsync_RequestTimeout_ReportsTheFailedRead()
	{
		var service = Build(new HangingHandler(), TimeSpan.FromMilliseconds(50));

		var result = await service.ResolveObjidAsync("Solitaire");

		// The failed arm, not "no such character": a read that failed says nothing about the name.
		await Assert.That(result.Expect<ApiFailure>().Kind).IsEqualTo(ApiFailureKind.Transport);
	}

	[TUnit.Core.Test]
	public async Task ResolveObjidAsync_KnownName_ReturnsTheObjid()
	{
		var service = Build(new RosterHandler());

		var result = await service.ResolveObjidAsync("sOlItAiRe");

		await Assert.That(result.Value).IsEqualTo("#11:1100");
	}

	[TUnit.Core.Test]
	public async Task ResolveObjidAsync_UnknownName_ReportsNotFound()
	{
		var service = Build(new RosterHandler());

		var result = await service.ResolveObjidAsync("Nobody");

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	[TUnit.Core.Test]
	public async Task ListAsync_CallerCancellation_Propagates()
	{
		var service = Build(new EmptyListHandler());
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await Assert.That(async () => await service.ListAsync(cts.Token)).Throws<OperationCanceledException>();
	}

	[TUnit.Core.Test]
	public async Task ListOnlineAsync_CallerCancellation_Propagates()
	{
		var service = Build(new EmptyListHandler());
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		await Assert.That(async () => await service.ListOnlineAsync(cts.Token)).Throws<OperationCanceledException>();
	}

	[TUnit.Core.Test]
	[Arguments("#12:1200")]
	[Arguments("#12")]
	[Arguments("castor")]
	public async Task PictureOfAsync_FindsTheCharacter_ByObjidDbrefOrName(string character)
	{
		var service = Build(new PictureRosterHandler());
		await Assert.That(await service.PictureOfAsync(character)).IsEqualTo("/api/wiki-assets/c/castor.jpg");
	}

	[TUnit.Core.Test]
	[Arguments("Solitaire")]
	[Arguments("#12:9999")]
	[Arguments("Nobody")]
	public async Task PictureOfAsync_IsNull_WithoutAPictureOrAMatch(string character)
	{
		var service = Build(new PictureRosterHandler());
		await Assert.That(await service.PictureOfAsync(character)).IsNull();
	}

	[TUnit.Core.Test]
	public async Task PicturesChanged_ReadsAgain_AndTellsTheAvatars()
	{
		var handler = new PictureRosterHandler();
		var service = Build(handler);
		var told = 0;
		service.Changed += () => told++;

		await service.PictureOfAsync("Castor");
		await service.PictureOfAsync("Castor");
		await Assert.That(handler.Reads).IsEqualTo(1);

		service.PicturesChanged();
		await service.PictureOfAsync("Castor");
		await Assert.That(handler.Reads).IsEqualTo(2);
		await Assert.That(told).IsEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task RowsThatAreNotCharacters_AreLeftOut_NotThrown()
	{
		var service = Build(new MalformedRowsHandler());
		var rows = (await service.ListAsync()).Expect<IReadOnlyList<CharacterDirectoryService.CharacterSummary>>();
		await Assert.That(rows.Select(r => r.Name)).IsEquivalentTo(["Castor"]);
		await Assert.That(await service.PictureOfAsync("Castor")).IsEqualTo("/c.jpg");
	}
}
