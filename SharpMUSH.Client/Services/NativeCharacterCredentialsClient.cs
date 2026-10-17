using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Client.Services;

public sealed class NativeCharacterCredentialsClient(IHttpClientFactory clients)
{
	private HttpClient Client => clients.CreateClient("api");
	private sealed record MintRequest(long CharacterCreationTime, string ProofGrant);
	private sealed record PasswordRequest(long CharacterCreationTime, string NewPassword, string Confirmation, string ProofGrant, bool DisconnectOtherClients);
	private sealed record CreateRequest(string Name, string? Password);

	public Task<ApiResult<NativeLoginSecretResponse>> MintAsync(int key, long creationTime, string grant)
		=> Client.PostApiAsync<MintRequest, NativeLoginSecretResponse>($"api/account/characters/{key}/native-login-secrets",
			new(creationTime, grant), "The server returned no native login.");

	public Task<ApiResult<IReadOnlyList<NativeLoginStatusResponse>>> ListAsync()
		=> Client.GetApiAsync<IReadOnlyList<NativeLoginStatusResponse>>("api/account/native-login-secrets", "The server returned no native login status.");

	public Task<ApiResult<Success>> RevokeAsync(string id)
		=> Client.DeleteApiAsync($"api/account/native-login-secrets/{Uri.EscapeDataString(id)}");

	public Task<ApiResult<Success>> SetPasswordAsync(int key, long creationTime, string password, string confirmation, string grant, bool disconnectOtherClients = false)
		=> Client.PutApiAsync($"api/account/characters/{key}/password", new PasswordRequest(creationTime, password, confirmation, grant, disconnectOtherClients));

	public Task<ApiResult<AccountApiClient.CreateCharacterResponse>> CreateWithoutPasswordAsync(string name)
		=> Client.PostApiAsync<CreateRequest, AccountApiClient.CreateCharacterResponse>("api/account/characters", new(name, null), "The server returned no created character.");
}
