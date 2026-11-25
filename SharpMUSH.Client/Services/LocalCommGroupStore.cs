using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Channel groups kept in this browser's localStorage under <c>play.commgroups</c>, the key the account's Play
/// settings fall back to without a signed-in account.
/// </summary>
public sealed class LocalCommGroupStore(IJSRuntime js) : ICommGroupStore
{
	public const string StorageKey = "play.commgroups";

	public async Task<string?> ReadAsync() => await js.GetItemAsync(BrowserStore.Local, StorageKey);

	public async Task<bool> WriteAsync(string json) => await js.SetItemAsync(BrowserStore.Local, StorageKey, json);

	public event Action? Changed
	{
		add { }
		remove { }
	}
}
