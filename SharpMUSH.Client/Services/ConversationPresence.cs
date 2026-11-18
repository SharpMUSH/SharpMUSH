using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Who in a page conversation is connected, by the online list (<see cref="CharacterDirectoryService.ListOnlineAsync"/>):
/// the ring on a page row's portrait and the "online" beside a conversation's title.
/// </summary>
public static class ConversationPresence
{
	/// <summary>Whether the <paramref name="index"/>-th person is connected; null when the list could not be read.</summary>
	public static bool? OnlineAt(IReadOnlyList<CharacterDirectoryService.CharacterSummary>? online, CommConversation c, int index) =>
		index < c.With.Count
			? CharacterDirectoryService.IsAmong(online, c.With[index], index < c.WithObjIds.Count ? c.WithObjIds[index] : null)
			: null;

	/// <summary>"online" or "offline" for one person, how many are on for a group; null when the list could not be read.</summary>
	public static string? Text(IStringLocalizer<SharedResource> loc,
		IReadOnlyList<CharacterDirectoryService.CharacterSummary>? online, CommConversation c)
	{
		if (online is null) return null;
		var on = Enumerable.Range(0, c.With.Count).Count(i => OnlineAt(online, c, i) == true);
		return c.With.Count == 1
			? on == 1 ? loc["NavPlayPresenceOnline"] : loc["NavPlayPresenceOffline"]
			: loc["NavPlayPresenceGroup", on, c.With.Count];
	}
}
