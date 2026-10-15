using System.Text.Json;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The signed-in account's devices with notifications on (<c>api/push</c>), and turning them on and off for this
/// browser through <see cref="PushInterop"/>.
/// </summary>
public sealed class AccountPushService(IHttpClientFactory httpClientFactory, PushInterop push)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public Task<PushBrowserState> BrowserStateAsync() => push.StateAsync();

	public Task<ApiResult<IReadOnlyList<PushDeviceResponse>>> DevicesAsync() =>
		Client.GetApiAsync<IReadOnlyList<PushDeviceResponse>>("api/push/subscriptions", "The server returned no device list.");

	/// <summary>Asks permission, subscribes this browser and stores the subscription.</summary>
	public async Task<ApiResult<PushDeviceResponse>> TurnOnAsync()
	{
		if (await Client.GetApiAsync<PushKeyResponse>("api/push/key", "The server sent no key.") is not PushKeyResponse key)
		{
			return new ApiFailure(ApiFailureKind.Unexpected, "The server could not be asked for its key.");
		}

		var result = await push.SubscribeAsync(key.PublicKey);
		if (result.Subscription is not { Length: > 0 } subscription
			|| JsonSerializer.Deserialize<PushSubscribeRequest>(subscription, Json) is not { } request)
		{
			return new ApiFailure(ApiFailureKind.Unexpected, result.Error ?? "The browser made no subscription.");
		}

		return await Client.PostApiAsync<PushSubscribeRequest, PushDeviceResponse>("api/push/subscriptions", request,
			"The server stored the subscription but described nothing.");
	}

	/// <summary>Unsubscribes this browser and removes it from the account.</summary>
	public async Task<ApiResult<Success>> TurnOffAsync(IReadOnlyList<PushDeviceResponse> devices)
	{
		var endpoint = await push.UnsubscribeAsync();
		return devices.FirstOrDefault(device => device.Endpoint == endpoint) is { } own
			? await RemoveAsync(own.Id)
			: new Success();
	}

	public Task<ApiResult<Success>> RemoveAsync(string id) =>
		Client.DeleteApiAsync($"api/push/subscriptions/{Uri.EscapeDataString(id)}");

	public Task<ApiResult<Success>> TestAsync(string id) =>
		Client.PostApiAsync($"api/push/subscriptions/{Uri.EscapeDataString(id)}/test");

	public Task<bool> InstallAsync() => push.InstallAsync();
}
