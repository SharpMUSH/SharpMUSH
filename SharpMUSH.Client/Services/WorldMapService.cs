using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The World map's reads (<c>GET api/maps</c>, <c>GET api/maps/{dbref}</c>, <c>GET api/maps/route/{dbref}</c>), as the acting character. Nothing is kept:
/// a builder's change shows on the next read.
/// </summary>
public class WorldMapService(IHttpClientFactory httpClientFactory, ServerInfoService? serverInfo = null)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public async Task<ApiResult<IReadOnlyList<MapSummaryDto>>> ListAsync()
		=> await Client.GetFeatureApiAsync<List<MapSummaryDto>>(serverInfo, GameFeatures.WorldMap, "api/maps",
				"The server returned no map list.") switch
		{
			List<MapSummaryDto> maps => (IReadOnlyList<MapSummaryDto>)maps,
			ApiFailure failure => failure,
		};

	public Task<ApiResult<MapDto>> GetAsync(int dbref)
		=> Client.GetFeatureApiAsync<MapDto>(serverInfo, GameFeatures.WorldMap, $"api/maps/{dbref}", "The server returned no map.");

	public Task<ApiResult<MapRouteDto>> RouteAsync(string room)
		=> Client.GetFeatureApiAsync<MapRouteDto>(serverInfo, GameFeatures.WorldMap, $"api/maps/route/{room.TrimStart('#')}",
			"The server returned no route.");
}
