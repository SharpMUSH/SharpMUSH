using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The World map's reads (<c>GET api/maps</c>, <c>GET api/maps/{dbref}</c>, <c>GET api/maps/route/{dbref}</c>, <c>GET api/maps/here</c>, <c>GET api/maps/description/{dbref}</c>), as the acting character. Nothing is kept:
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

	public Task<ApiResult<MapHereDto>> HereAsync()
		=> Client.GetFeatureApiAsync<MapHereDto>(serverInfo, GameFeatures.WorldMap, "api/maps/here", "The server returned no location.");

	public Task<ApiResult<MapRouteDto>> RouteAsync(string room)
		=> Client.GetFeatureApiAsync<MapRouteDto>(serverInfo, GameFeatures.WorldMap, $"api/maps/route/{room.TrimStart('#')}",
			"The server returned no route.");

	public Task<ApiResult<MapDescriptionDto>> DescriptionAsync(string room)
		=> Client.GetFeatureApiAsync<MapDescriptionDto>(serverInfo, GameFeatures.WorldMap,
			$"api/maps/description/{room.TrimStart('#')}", "The server returned no description.");
}
