using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Webhooks;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the Webhooks page's API (<c>api/admin/webhooks</c>).</summary>
public class WebhooksAdminService(IHttpClientFactory httpClientFactory)
{
	private const string WhenEmpty = "The server returned no webhooks.";

	private HttpClient Client => httpClientFactory.CreateClient("api");

	private static string Path(string name) => $"api/admin/webhooks/{Uri.EscapeDataString(name)}";

	public Task<ApiResult<List<WebhookView>>> ListAsync() =>
		Client.GetApiAsync<List<WebhookView>>("api/admin/webhooks", WhenEmpty);

	/// <summary>Creates a webhook; the answer carries its secret.</summary>
	public Task<ApiResult<WebhookSecret>> CreateAsync(WebhookCreateRequest request) =>
		Client.PostApiAsync<WebhookCreateRequest, WebhookSecret>("api/admin/webhooks", request, WhenEmpty);

	public Task<ApiResult<WebhookView>> UpdateAsync(string name, WebhookUpdateRequest request) =>
		Client.PatchApiAsync<WebhookUpdateRequest, WebhookView>(Path(name), request, WhenEmpty);

	public Task<ApiResult<WebhookSecret>> SecretAsync(string name) =>
		Client.GetApiAsync<WebhookSecret>($"{Path(name)}/secret", WhenEmpty);

	public Task<ApiResult<WebhookSecret>> RotateAsync(string name) =>
		Client.PostApiAsync<object?, WebhookSecret>($"{Path(name)}/rotate", null, WhenEmpty);

	public Task<ApiResult<WebhookTestSent>> TestAsync(string name) =>
		Client.PostApiAsync<object?, WebhookTestSent>($"{Path(name)}/test", null, WhenEmpty);

	public Task<ApiResult<Success>> DeleteAsync(string name) =>
		Client.DeleteApiAsync(Path(name));
}
