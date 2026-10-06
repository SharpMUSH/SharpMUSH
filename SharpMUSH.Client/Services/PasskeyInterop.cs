using System.Text.Json;
using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>The visitor dismissed the browser's passkey prompt, or let it time out.</summary>
public sealed record PasskeyCancelled;

/// <summary>What a passkey step came to: its value, a dismissed prompt, or the failure that stopped it.</summary>
public union PasskeyOutcome<T>(T, PasskeyCancelled, ApiFailure);

/// <summary>
/// The browser's passkey prompts (<c>wwwroot/js/passkeys.js</c>): the server's WebAuthn options in, the
/// new or signing credential out, both as WebAuthn's JSON form.
/// </summary>
public sealed class PasskeyInterop(IJSRuntime js)
{
	/// <summary>What <c>SharpMUSH.Passkeys.create</c>/<c>get</c> resolve to.</summary>
	private sealed record PromptResult(string? Credential, string? Error, bool Cancelled);

	/// <summary>Whether this browser offers passkeys at all.</summary>
	public async Task<bool> IsSupportedAsync()
	{
		try
		{
			return await js.InvokeAsync<bool>("SharpMUSH.Passkeys.isSupported");
		}
		catch (JSException)
		{
			return false;
		}
	}

	/// <summary>Asks the browser to make a new passkey from <paramref name="options"/>.</summary>
	public Task<PasskeyOutcome<JsonElement>> CreateAsync(JsonElement options) => PromptAsync("SharpMUSH.Passkeys.create", options);

	/// <summary>Asks the browser to sign <paramref name="options"/>' challenge with a passkey the visitor picks.</summary>
	public Task<PasskeyOutcome<JsonElement>> GetAsync(JsonElement options) => PromptAsync("SharpMUSH.Passkeys.get", options);

	private async Task<PasskeyOutcome<JsonElement>> PromptAsync(string function, JsonElement options)
	{
		PromptResult result;
		try
		{
			result = await js.InvokeAsync<PromptResult>(function, options.GetRawText());
		}
		catch (JSException ex)
		{
			return new ApiFailure(ApiFailureKind.Unexpected, ex.Message);
		}

		if (result.Cancelled) return new PasskeyCancelled();
		if (result.Credential is not { Length: > 0 } credential)
			return new ApiFailure(ApiFailureKind.Unexpected, result.Error ?? "The browser returned no passkey.");

		using var document = JsonDocument.Parse(credential);
		return document.RootElement.Clone();
	}
}
