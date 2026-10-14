using SharpMUSH.Client.Models;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The scene details the game defines (<c>GET api/scenes/details</c>), read once and kept: what each is called, what
/// goes in it and whether scene cards show it. A scene's values are in its <c>Meta</c>; owners set them with
/// <c>+scene/set</c>, and staff change the catalogue with <c>+scene/detail/add|set|remove</c>.
/// </summary>
/// <remarks>A game without the scene package answers empty lists, and a scene's details then show by their keys.</remarks>
public class SceneDetailService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	private readonly SingleFlight<string, ApiResult<SceneDetailCatalogue>> _flight = new(StringComparer.Ordinal);

	/// <summary>The details in display order; empty until <see cref="LoadAsync"/> has answered.</summary>
	public SceneDetailCatalogue Catalogue { get; private set; } = SceneDetailCatalogue.Empty;

	private bool _loaded;

	/// <summary>The catalogue, read from the server the first time and kept after. A failed read keeps what was there.</summary>
	public async Task<SceneDetailCatalogue> LoadAsync()
	{
		if (_loaded) return Catalogue;

		if (await _flight.RunAsync("", FetchAsync) is SceneDetailCatalogue catalogue)
		{
			Catalogue = catalogue;
			_loaded = true;
		}
		return Catalogue;
	}

	private async Task<ApiResult<SceneDetailCatalogue>> FetchAsync()
	{
		var result = await Client.GetApiAsync<SceneDetailCatalogue>("api/scenes/details", "The server returned no scene details.");
		return result switch
		{
			SceneDetailCatalogue catalogue => new SceneDetailCatalogue(
				[.. (catalogue.Details ?? []).Select(d => d with { Choices = d.Choices ?? [] })], catalogue.Problems ?? []),
			ApiFailure failure => failure,
		};
	}
}
