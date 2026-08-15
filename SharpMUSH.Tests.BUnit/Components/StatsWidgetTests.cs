using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Serves three characters but only one connection, so "Players Online" and "Characters" must
/// disagree. They used to be rendered from the same field and could never differ.
/// </summary>
file sealed class StatsHandler : HttpMessageHandler
{
	private record Row(string Name, string Objid, long Created, string Category);

	private static readonly Row[] Roster =
	[
		new("Ada", "#10:1000", 1000, ""),
		new("Bree", "#11:1100", 1100, ""),
		new("Package Manager", "#7:700", 700, "Wizard"),
	];

	private static readonly Row[] Online = [new("Bree", "#11:1100", 1100, "")];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath;
		object? payload = path switch
		{
			"/http/characters" => Roster,
			"/http/online" => Online,
			"/api/wiki/recent" => Array.Empty<object>(),
			_ when path.StartsWith("/api/scenes", StringComparison.Ordinal) => Array.Empty<object>(),
			_ => null
		};

		return Task.FromResult(payload is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
	}
}

/// <summary>
/// Serves the same character on several connection rows plus one other character, so a tile that
/// counts rows reads 4 where a tile that counts people reads 2.
/// </summary>
file sealed class DoubledConnectionStatsHandler : HttpMessageHandler
{
	private record Row(string Name, string Objid, long Created, string Category);

	private static readonly Row[] Roster =
	[
		new("Solitaire", "#11:1100", 1100, ""),
		new("Castor", "#12:1200", 1200, ""),
	];

	private static readonly Row[] Online =
	[
		new("Solitaire", "#11:1100", 1100, ""),
		new("Solitaire", "#11:1100", 1100, ""),
		new("Solitaire", "#11:1100", 1100, ""),
		new("Castor", "#12:1200", 1200, ""),
	];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath;
		object? payload = path switch
		{
			"/http/characters" => Roster,
			"/http/online" => Online,
			"/api/wiki/recent" => Array.Empty<object>(),
			_ when path.StartsWith("/api/scenes", StringComparison.Ordinal) => Array.Empty<object>(),
			_ => null
		};

		return Task.FromResult(payload is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
	}
}

/// <summary>
/// Answers the online route but fails the roster route, so the widget finishes loading with one
/// count known and the other unknowable — the state that used to render as a confident zero.
/// </summary>
file sealed class RosterFailsHandler : HttpMessageHandler
{
	private record Row(string Name, string Objid, long Created, string Category);

	private static readonly Row[] Online = [new("Bree", "#11:1100", 1100, "")];

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath;
		return Task.FromResult(path switch
		{
			"/http/characters" => new HttpResponseMessage(HttpStatusCode.InternalServerError),
			"/http/online" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Online) },
			_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) }
		});
	}
}

/// <summary>
/// The "Players Online" tile must report who is connected — as people, one per character — rather
/// than the size of the character roster or the number of open connections. And no tile may state
/// a count it never received: a request that failed shows the unknown placeholder, not zero.
/// </summary>
public class StatsWidgetTests : TrackingBunitContext
{
	private static void Wire(TrackingBunitContext ctx, HttpMessageHandler handler)
	{
		var apiClient = ctx.Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		ctx.Services
			.AddSingleton(apiClient)
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new CharacterDirectoryService(
				sp.GetRequiredService<IHttpClientFactory>(),
				NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton(sp => new WikiService(
				sp.GetRequiredService<IHttpClientFactory>(),
				NullLogger<WikiService>.Instance))
			.AddSingleton(sp => new SceneService(
				sp.GetRequiredService<IHttpClientFactory>(),
				NullLogger<SceneService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>Reads the number rendered in the tile whose label is <paramref name="label"/>.</summary>
	private static string TileValue(string markup, string label)
	{
		var labelAt = markup.IndexOf($">{label}<", StringComparison.Ordinal);
		if (labelAt < 0)
		{
			throw new InvalidOperationException($"No '{label}' tile in markup.");
		}

		var match = Regex.Match(markup[labelAt..], @">(\d+|—)<");
		return match.Success ? match.Groups[1].Value : throw new InvalidOperationException($"No value for '{label}'.");
	}

	[TUnit.Core.Test]
	public async Task PlayersOnline_CountsConnections_NotTheRoster()
	{
		Wire(this, new StatsHandler());

		var cut = Render<StatsWidget>();
		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("—")) throw new InvalidOperationException("stats not loaded yet");
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		// 3 characters exist; exactly 1 holds a connection. Before the fix both tiles read 3.
		await Assert.That(TileValue(markup, "Characters")).IsEqualTo("3");
		await Assert.That(TileValue(markup, "WidPlayersOnline")).IsEqualTo("1");
	}

	[TUnit.Core.Test]
	public async Task PlayersOnline_CountsPeople_NotConnections()
	{
		Wire(this, new DoubledConnectionStatsHandler());

		var cut = Render<StatsWidget>();
		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("—")) throw new InvalidOperationException("stats not loaded yet");
		}, TimeSpan.FromSeconds(5));

		// Four rows, two people: one of them is logged in three times. The tile says "Players
		// Online", so it must say 2 — the live portal read 14 for a world with four characters.
		await Assert.That(TileValue(cut.Markup, "WidPlayersOnline")).IsEqualTo("2");
	}

	[TUnit.Core.Test]
	public async Task AFailedRosterFetch_LeavesTheTileUnknown_RatherThanZero()
	{
		Wire(this, new RosterFailsHandler());

		var cut = Render<StatsWidget>();
		// The online route still answers, so waiting on its tile proves the widget finished loading;
		// the Characters tile keeping its dash is then a decision, not an unfinished fetch.
		cut.WaitForAssertion(() =>
		{
			if (TileValue(cut.Markup, "WidPlayersOnline") == "—") throw new InvalidOperationException("stats not loaded yet");
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		await Assert.That(TileValue(markup, "WidPlayersOnline")).IsEqualTo("1");
		await Assert.That(TileValue(markup, "Characters")).IsEqualTo("—");
		// And the dash is explained rather than left looking like a value still in flight.
		await Assert.That(markup).Contains("WidUnavailable");
	}
}
