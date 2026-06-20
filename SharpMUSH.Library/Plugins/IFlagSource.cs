namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Phase 2a contribution surface for engine object flags. A plugin entry type may implement this to
/// seed additional flags into the database during migration. The pre-build <c>PluginCatalog</c> collects
/// every source's <see cref="Flags"/>; each active database provider appends them to its built-in flag
/// seed set inside <c>Migrate()</c> so the flags exist after migration on every backend.
/// </summary>
public interface IFlagSource
{
	/// <summary>The flags this plugin contributes.</summary>
	IEnumerable<PluginFlag> Flags { get; }
}
