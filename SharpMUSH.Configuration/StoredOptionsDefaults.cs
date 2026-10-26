using System.Text.Json.Nodes;

namespace SharpMUSH.Configuration;

/// <summary>
/// Brings a stored configuration document up to the current options shape: a section or a setting the
/// document has no entry for takes the shipped default. A world written before a setting existed has no
/// entry for it, and without this a required section fails the read and stops the server.
/// </summary>
/// <remarks>
/// Two levels only, sections and the settings in them. A setting that is itself a table (aliases,
/// restrictions, translations) is never merged into, so an entry an administrator removed stays removed.
/// </remarks>
public static class StoredOptionsDefaults
{
	/// <summary>Fills <paramref name="stored"/> from <paramref name="defaults"/> in place and returns what was filled, as <c>Section</c> or <c>Section.Setting</c>.</summary>
	public static IReadOnlyList<string> Fill(JsonObject stored, JsonObject defaults)
	{
		var filled = new List<string>();

		foreach (var (section, defaultSection) in defaults)
		{
			if (!stored.TryGetPropertyValue(section, out var storedSection))
			{
				stored[section] = defaultSection?.DeepClone();
				filled.Add(section);
				continue;
			}

			if (storedSection is not JsonObject storedSettings || defaultSection is not JsonObject defaultSettings)
			{
				continue;
			}

			foreach (var (setting, defaultValue) in defaultSettings)
			{
				if (storedSettings.ContainsKey(setting)) continue;

				storedSettings[setting] = defaultValue?.DeepClone();
				filled.Add($"{section}.{setting}");
			}
		}

		return filled;
	}
}
