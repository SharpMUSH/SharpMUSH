using System.Text.Json;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Client.Components.Widgets;

/// <summary>
/// Which character a profile widget is about: the <c>character</c> config key when an admin placed
/// it elsewhere, else the profile page's character; and that character's dbref from the directory.
/// </summary>
internal static class ProfileCharacter
{
	public static string? NameFrom(JsonElement? config, ProfilePageContext? context)
	{
		if (config is { ValueKind: JsonValueKind.Object } cfg
			&& cfg.TryGetProperty("character", out var prop) && prop.ValueKind == JsonValueKind.String
			&& !string.IsNullOrWhiteSpace(prop.GetString()))
		{
			return prop.GetString()!.Trim();
		}

		return string.IsNullOrWhiteSpace(context?.CharacterName) ? null : context.CharacterName;
	}

	/// <summary>
	/// The bare dbref (<c>#42</c>) of <paramref name="name"/>, or null when it cannot be found: the
	/// profile page's own answer when it is about that character, else the directory's.
	/// </summary>
	public static async Task<string?> DbrefOfAsync(CharacterDirectoryService directory, string name, ProfilePageContext? context = null)
	{
		if (context?.Dbref is { } known && string.Equals(context.CharacterName, name, StringComparison.OrdinalIgnoreCase))
		{
			return known;
		}

		return await directory.ResolveObjidAsync(name) is string objid ? objid.Split(':')[0] : null;
	}
}
