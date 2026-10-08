namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Where plugins are loaded from. <see cref="BuiltIn"/> holds the plugins the server image ships (the Scene
/// System), beside the server binary; nothing writes there. <see cref="Installed"/> holds the plugins that
/// plugin packages deposit, one folder per package id, and sits on the data volume beside the world so an
/// image update keeps them.
/// </summary>
/// <param name="BuiltIn">The shipped plugins' directory.</param>
/// <param name="Installed">The installed plugins' directory.</param>
public sealed record PluginDirectories(string BuiltIn, string Installed)
{
	/// <summary>Names the installed plugins' directory; unset, it is the world path plus <c>.plugins</c>.</summary>
	public const string PathVariable = "SHARPMUSH_PLUGINS_PATH";

	/// <summary>
	/// Whether the portal may install plugin packages, from a remote or by upload: <c>false</c> turns it off.
	/// On by default; whoever runs the server is answerable for the code they install.
	/// </summary>
	public const string InstallVariable = "SHARPMUSH_PLUGIN_INSTALL";

	/// <summary>The directory beside the server binary that shipped plugins live in.</summary>
	public static string DefaultBuiltIn => Path.Combine(AppContext.BaseDirectory, "plugins");

	/// <summary>
	/// The file that records which plugins an administrator turned off. It is read before the world is open,
	/// which is why it is a file rather than a world record.
	/// </summary>
	public string StateFile => Path.Combine(Installed, PluginState.FileName);

	/// <summary>Where an uploaded plugin package waits between upload and apply.</summary>
	public string UploadStaging => Path.Combine(Installed, ".uploads");

	/// <summary>The shipped directory and an installed directory beside <paramref name="worldPath"/>, or <paramref name="configured"/>.</summary>
	public static PluginDirectories ForWorld(string worldPath, string? configured) =>
		new(DefaultBuiltIn, Path.GetFullPath(configured is { Length: > 0 }
			? configured
			: worldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".plugins"));
}
