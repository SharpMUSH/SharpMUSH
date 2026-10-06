using SharpMUSH.Library.DiscriminatedUnions;
using static SharpMUSH.Client.Services.AccountApiClient;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The signed-in account's passkeys: listing them, adding one through the browser's prompt, renaming
/// and removing them. Signing in with one is <see cref="AccountAuthService.LoginWithPasskeyAsync"/>.
/// </summary>
public sealed class AccountPasskeyService(IHttpClientFactory httpClientFactory, PasskeyInterop passkeys)
{
	private readonly AccountApiClient _api = new(httpClientFactory);

	/// <inheritdoc cref="PasskeyInterop.IsSupportedAsync"/>
	public Task<bool> IsSupportedAsync() => passkeys.IsSupportedAsync();

	public Task<ApiResult<IReadOnlyList<PasskeySummary>>> ListAsync() => _api.PasskeysAsync();

	/// <summary>
	/// Adds a passkey named <paramref name="name"/>: the server checks <paramref name="currentPassword"/>,
	/// the browser makes the passkey, and the server stores it.
	/// </summary>
	public async Task<PasskeyOutcome<PasskeySummary>> AddAsync(string currentPassword, string? name) =>
		await _api.PasskeyOptionsAsync(currentPassword) switch
		{
			PasskeyChallenge challenge => await passkeys.CreateAsync(challenge.Options) switch
			{
				System.Text.Json.JsonElement credential => await _api.AddPasskeyAsync(challenge.CeremonyId, name, credential) switch
				{
					PasskeySummary added => added,
					ApiFailure failure => failure,
				},
				PasskeyCancelled cancelled => cancelled,
				ApiFailure failure => failure,
			},
			ApiFailure failure => failure,
		};

	public Task<ApiResult<Success>> RenameAsync(string id, string name) => _api.RenamePasskeyAsync(id, name);

	public Task<ApiResult<Success>> RemoveAsync(string id) => _api.RemovePasskeyAsync(id);
}
