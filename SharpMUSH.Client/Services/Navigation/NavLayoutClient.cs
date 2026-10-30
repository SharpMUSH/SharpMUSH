using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Navigation;

namespace SharpMUSH.Client.Services.Navigation;

/// <summary>
/// Client for the game's navigation layout (<c>api/navigation</c>). The read is shared by the rail, the drawer, the
/// tab bar and the palette behind a short memo; a save or a reset through this client drops it and raises
/// <see cref="Changed"/>, so this tab redraws its navigation at once.
/// </summary>
public class NavLayoutClient(IHttpClientFactory httpClientFactory)
{
	private readonly ShortMemo<(bool Ok, Found<NavLayout> Layout)> _layout = new(TimeSpan.FromSeconds(60), r => r.Ok);

	/// <summary>Raised after a save or a reset from this tab.</summary>
	public event Action? Changed;

	/// <summary>The stored layout, or <see cref="NotFound"/> while staff never saved one or the server did not answer.</summary>
	public async Task<Found<NavLayout>> GetAsync() => (await _layout.GetAsync(FetchAsync)).Layout;

	/// <summary>The stored layout as the server has it now, past the memo: what an editor starts from.</summary>
	public Task<Found<NavLayout>> GetFreshAsync()
	{
		_layout.Forget();
		return GetAsync();
	}

	/// <summary>Saves <paramref name="layout"/> over <paramref name="expectedVersion"/>; a 409 failure means another save landed first.</summary>
	public Task<ApiResult<NavLayout>> SaveAsync(NavLayout layout, long expectedVersion) =>
		ChangingAsync(Client.PutApiAsync<SaveNavLayoutRequest, NavLayout>("api/navigation",
			new SaveNavLayoutRequest(layout, expectedVersion), "The server did not return the saved layout."));

	/// <summary>Puts the navigation back to its default.</summary>
	public Task<ApiResult<Success>> ResetAsync() => ChangingAsync(Client.DeleteApiAsync("api/navigation"));

	private async Task<(bool Ok, Found<NavLayout> Layout)> FetchAsync()
	{
		try
		{
			using var response = await Client.GetAsync("api/navigation");
			if (response.StatusCode == HttpStatusCode.NoContent) return (true, new NotFound());
			if (!response.IsSuccessStatusCode) return (false, new NotFound());
			return await response.Content.ReadFromJsonAsync<NavLayout>() is { } layout ? (true, layout) : (false, new NotFound());
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException)
		{
			return (false, new NotFound());
		}
	}

	/// <summary>Awaits a write, then drops the memo, whether or not it worked: it may have landed before the answer was lost.</summary>
	private async Task<T> ChangingAsync<T>(Task<T> write)
	{
		try
		{
			return await write;
		}
		finally
		{
			_layout.Forget();
			Changed?.Invoke();
		}
	}

	private HttpClient Client => httpClientFactory.CreateClient("api");
}
