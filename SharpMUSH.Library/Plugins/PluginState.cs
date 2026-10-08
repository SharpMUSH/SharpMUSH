using System.Text.Json;

namespace SharpMUSH.Library.Plugins;

/// <summary>The plugins an administrator turned off. The loader skips them at the next start.</summary>
/// <param name="Disabled">Plugin ids, compared without case.</param>
public sealed record PluginState(IReadOnlyList<string> Disabled)
{
	/// <summary>The state file's name inside the installed plugins' directory.</summary>
	public const string FileName = "plugins.state.json";

	private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

	/// <summary>Nothing turned off.</summary>
	public static PluginState Empty { get; } = new([]);

	/// <summary>Whether <paramref name="pluginId"/> is turned off.</summary>
	public bool IsDisabled(string pluginId) => Disabled.Contains(pluginId, StringComparer.OrdinalIgnoreCase);

	/// <summary>This state with <paramref name="pluginId"/> turned on or off.</summary>
	public PluginState With(string pluginId, bool enabled) => enabled
		? new(Disabled.Where(id => !string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase)).ToList())
		: IsDisabled(pluginId) ? this : new([.. Disabled, pluginId]);

	/// <summary>
	/// The state recorded at <paramref name="path"/>, or <see cref="Empty"/> when there is none. A file that does not
	/// parse is an error, not an empty state: starting every plugin an administrator turned off would be worse.
	/// </summary>
	public static PluginState Read(string path)
	{
		if (!File.Exists(path))
		{
			return Empty;
		}

		var state = JsonSerializer.Deserialize<PluginState>(File.ReadAllText(path), Json)
			?? throw new InvalidDataException($"{path} is empty.");
		return state with { Disabled = state.Disabled ?? [] };
	}

	/// <summary>Writes this state to <paramref name="path"/> through a temporary file, so a crash leaves the old one.</summary>
	public void Write(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(this, Json));
		File.Move(temporary, path, overwrite: true);
	}
}
