using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

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
/// The "Players Online" tile must report connections, not the size of the character roster.
/// </summary>
public class StatsWidgetTests : BunitContext
{
	public StatsWidgetTests()
	{
		var apiClient = new HttpClient(new StatsHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
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
				NullLogger<SceneService>.Instance));

		JSInterop.Mode = JSRuntimeMode.Loose;
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
		var cut = Render<StatsWidget>();
		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("—")) throw new InvalidOperationException("stats not loaded yet");
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		// 3 characters exist; exactly 1 holds a connection. Before the fix both tiles read 3.
		await Assert.That(TileValue(markup, "Characters")).IsEqualTo("3");
		await Assert.That(TileValue(markup, "Players Online")).IsEqualTo("1");
	}
}
