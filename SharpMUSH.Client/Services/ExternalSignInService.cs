using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Signing in through another service (Discord, Google, ...), and the account's links to services. The browser leaves
/// for the service's page and comes back to <c>/login/external</c>; what the tab needs then is kept in its
/// <c>sessionStorage</c> meanwhile, the binding that proves it started the sign-in among it.
/// </summary>
public sealed class ExternalSignInService(IHttpClientFactory httpClientFactory, IJSRuntime js)
{
	private const string PendingKey = "sharpmush.externalSignIn";

	/// <summary>A sign-in this tab started and has not finished.</summary>
	/// <param name="Binding">The random value the server's flow was started with.</param>
	/// <param name="Provider">The provider's id.</param>
	/// <param name="RememberMe">Keep the browser signed in, as the sign-in page's box said.</param>
	/// <param name="ReturnUrl">Where the visitor was headed, or null.</param>
	/// <param name="Linking">Linking the service to the signed-in account, not signing in.</param>
	public sealed record Pending(string Binding, string Provider, bool RememberMe, string? ReturnUrl, bool Linking);

	private sealed record CompleteRequest(string Flow, string Binding, bool RememberMe, bool CreateAccount, string? Username,
		string? Email);

	private HttpClient Client => httpClientFactory.CreateClient("api");

	/// <summary>The services the sign-in page offers.</summary>
	public Task<ApiResult<List<ExternalSignInProviderInfo>>> ProvidersAsync() =>
		Client.GetApiAsync<List<ExternalSignInProviderInfo>>("api/auth/external/providers", "The server listed no sign-in services.");

	/// <summary>Starts signing in with <paramref name="provider"/>: the address of its page, to send the browser to.</summary>
	public Task<ApiResult<string>> BeginSignInAsync(string provider, bool rememberMe, string? returnUrl) =>
		BeginAsync($"api/auth/external/{Uri.EscapeDataString(provider)}/begin", new Pending(NewBinding(), provider, rememberMe, returnUrl, false));

	/// <summary>Starts linking <paramref name="provider"/> to the signed-in account.</summary>
	public Task<ApiResult<string>> BeginLinkAsync(string provider) =>
		BeginAsync($"api/account/sign-ins/{Uri.EscapeDataString(provider)}/begin", new Pending(NewBinding(), provider, false, null, true));

	private async Task<ApiResult<string>> BeginAsync(string url, Pending pending)
	{
		if (!await js.SetItemAsync(BrowserStore.Session, PendingKey, JsonSerializer.Serialize(pending)))
			return new ApiFailure(ApiFailureKind.Unexpected,
				"This browser tab could not save the sign-in. Allow this site to store data, then try again.");

		return await Client.PostApiAsync<ExternalSignInBeginRequest, ExternalSignInBeginResponse>(url,
				new ExternalSignInBeginRequest(pending.Binding), "The server did not say where to sign in.") switch
		{
			ExternalSignInBeginResponse begun => begun.Url,
			ApiFailure failure => failure,
		};
	}

	/// <summary>The sign-in this tab started, or <see cref="NotFound"/> when it started none.</summary>
	public async Task<Found<Pending>> PendingAsync()
	{
		try
		{
			return await js.GetItemAsync(BrowserStore.Session, PendingKey) is { } stored
				&& JsonSerializer.Deserialize<Pending>(stored) is { } pending
					? pending
					: new NotFound();
		}
		catch (JsonException)
		{
			return new NotFound();
		}
	}

	/// <summary>Forgets the sign-in this tab started, once it is finished or abandoned.</summary>
	public async Task ForgetAsync() => await js.RemoveItemAsync(BrowserStore.Session, PendingKey);

	/// <summary>Finishes the flow <paramref name="flow"/> the service sent the browser back with.</summary>
	public Task<ApiResult<AccountApiClient.ExternalCompleteResponse>> CompleteAsync(string flow, Pending pending,
		bool createAccount = false, string? username = null, string? email = null) =>
		Client.PostApiAsync<CompleteRequest, AccountApiClient.ExternalCompleteResponse>("api/auth/external/complete",
			new CompleteRequest(flow, pending.Binding, pending.RememberMe, createAccount, username,
				string.IsNullOrWhiteSpace(email) ? null : email.Trim()),
			"The server did not say how the sign-in ended.");

	/// <summary>The account's password, service sign-ins, and the services it could link.</summary>
	public Task<ApiResult<AccountSignIns>> SignInsAsync() =>
		Client.GetApiAsync<AccountSignIns>("api/account/sign-ins", "The server did not list the account's sign-ins.");

	/// <summary>Removes the account's sign-in through <paramref name="provider"/>.</summary>
	public Task<ApiResult<Success>> UnlinkAsync(string provider) =>
		Client.DeleteApiAsync($"api/account/sign-ins/{Uri.EscapeDataString(provider)}");

	private static string NewBinding() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
