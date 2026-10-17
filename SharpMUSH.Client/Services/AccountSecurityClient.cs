using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
namespace SharpMUSH.Client.Services;

/// <summary>Provider tokens never reach this client. Recent proof grants live only in this tab's memory.</summary>
public sealed class AccountSecurityClient(IHttpClientFactory factory, IJSRuntime js, AccountAuthService auth)
{
	public record Provider(string Id, string DisplayName);
	private sealed record Csrf(string Token, string FieldName);
	private readonly Dictionary<AccountSecurityOperation, string> grants = new();
	public string? TakeGrant(AccountSecurityOperation operation) => grants.Remove(operation, out var grant) ? grant : null;
	public void HoldGrant(AccountSecurityOperation operation, string grant) => grants[operation] = grant;
	public Task<ApiResult<IReadOnlyList<Provider>>> ProvidersAsync() => factory.CreateClient("api").GetApiAsync<IReadOnlyList<Provider>>("api/auth/external/providers", "No providers were returned.");
	public Task<ApiResult<JsonElement>> SecurityAsync() => factory.CreateClient("api").GetApiAsync<JsonElement>("api/account/security", "Account security is unavailable.");
	public Task<ApiResult<JsonElement>> RequestAsync(string path, object body, string method = "POST", bool csrf = false)
	 => SendAsync(path, body, method, csrf);
	private async Task<ApiResult<JsonElement>> SendAsync(string path, object body, string method, bool csrf)
	{
		var client = factory.CreateClient("api");
		using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(body) };
		if (csrf)
		{
			if (await client.GetApiAsync<Csrf>("api/auth/external/csrf", "Browser verification is unavailable.") is not Csrf token)
				return new ApiFailure(ApiFailureKind.Unexpected, "Browser verification is unavailable. Reload and try again.");
			request.Headers.Add("X-SharpMUSH-CSRF", token.Token);
		}
		try
		{
			using var response = await client.SendAsync(request);
			if (!response.IsSuccessStatusCode) return ApiFailure.FromStatus(response.StatusCode, await response.Content.ReadAsStringAsync());
			if (response.Headers.TryGetValues("X-SharpMUSH-Replacement-Session", out var replacement) && replacement.SingleOrDefault() is { } token)
				if (!await auth.AdoptSecuritySessionAsync(token)) return new ApiFailure(ApiFailureKind.Unexpected, "The replacement session could not be saved. Sign in again.");
			if (response.Headers.Contains("X-SharpMUSH-Signed-Out")) await auth.LogoutAsync();
			var text = await response.Content.ReadAsStringAsync();
			using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
			return document.RootElement.Clone();
		}
		catch (Exception ex) { return ApiFailure.Transport(ex); }
	}
	public async Task<ApiResult<Success>> StartAsync(string provider, string returnPath, bool remember, int purpose = 0,
	 string? proofGrant = null, AccountSecurityOperation? operation = null, string? identityId = null)
	{
		if (await SendAsync("api/auth/external/" + Uri.EscapeDataString(provider) + "/start", new { returnPath, rememberMe = remember, purpose, proofGrant, operation, identityId }, "POST", true) is not JsonElement started)
			return new ApiFailure(ApiFailureKind.Unexpected, "Sign-in could not be started. Check the provider settings or try again.");
		var client = factory.CreateClient("api");
		if (await client.GetApiAsync<Csrf>("api/auth/external/csrf", "Browser verification is unavailable.") is not Csrf token)
			return new ApiFailure(ApiFailureKind.Unexpected, "Browser verification is unavailable.");
		try
		{
			var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/external-login.js");
			await module.InvokeVoidAsync("challenge", started.GetProperty("transactionId").GetString(), token.FieldName, token.Token);
			return new Success();
		}
		catch (JSException) { return new ApiFailure(ApiFailureKind.Unexpected, "The browser could not start sign-in."); }
	}
	public async Task<ApiResult<IReadOnlyList<AccountAuthService.CharacterSummary>>> AdoptAsync(JsonElement response)
	{
		var login = response.Deserialize<AccountApiClient.LoginResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
		if (login is null || string.IsNullOrEmpty(login.AccountSessionToken) || login.Characters is null || string.IsNullOrEmpty(login.Username))
			return new ApiFailure(ApiFailureKind.Unexpected, "The server's sign-in response was incomplete.");
		return await auth.AdoptExternalSessionAsync(login);
	}
}
