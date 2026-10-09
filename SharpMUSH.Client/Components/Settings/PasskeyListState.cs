using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Components.Settings;

/// <summary>
/// What the account page's passkey list shows and what is typed into it. The page owns it, so the list
/// loads where the page's start-up puts it, after the characters.
/// </summary>
public sealed class PasskeyListState
{
	public IReadOnlyList<AccountApiClient.PasskeySummary>? Passkeys { get; set; }
	public bool Supported { get; set; }
	public bool Adding { get; set; }
	public string Name { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
	public string? Renaming { get; set; }
	public string Rename { get; set; } = string.Empty;
	public string? Error { get; set; }
	public string? Success { get; set; }

	public async Task LoadAsync(AccountPasskeyService passkeys)
	{
		Supported = await passkeys.IsSupportedAsync();
		switch (await passkeys.ListAsync())
		{
			case IReadOnlyList<AccountApiClient.PasskeySummary> held:
				Passkeys = held;
				break;
			case ApiFailure failure:
				Passkeys = [];
				Error = failure.Message;
				break;
		}
	}

	public void ClearMessages() { Error = null; Success = null; }
}
