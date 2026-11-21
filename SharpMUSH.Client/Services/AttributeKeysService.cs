using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>Typed client for the attribute table's read and write keys.</summary>
public class AttributeKeysService(IHttpClientFactory httpClientFactory)
{
	private HttpClient Client => httpClientFactory.CreateClient("api");

	public Task<ApiResult<List<AttributeKeyEntryDto>>> ListAsync() =>
		Client.GetApiAsync<List<AttributeKeyEntryDto>>("api/admin/attribute-keys", "The server returned no attribute keys.");

	/// <param name="kind"><c>read</c> or <c>write</c>.</param>
	/// <param name="key">The key; empty takes it off.</param>
	public Task<ApiResult<Success>> SetAsync(string attribute, string kind, string key) =>
		Client.PutApiAsync($"api/admin/attribute-keys/{Uri.EscapeDataString(attribute)}/{kind}", new SetAttributeKeyRequest(key));
}
