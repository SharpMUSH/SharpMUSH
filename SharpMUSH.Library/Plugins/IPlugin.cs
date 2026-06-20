namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Identity + lifecycle contract for a SharpMUSH C# plugin.
/// A plugin assembly exposes exactly one type marked with <see cref="Attributes.SharpPluginAttribute"/>
/// that implements this interface (usually by deriving from <see cref="PluginBase"/>).
/// </summary>
public interface IPlugin
{
	/// <summary>Stable machine identity. Used as the dependency key for load order.</summary>
	string Id { get; }

	/// <summary>Semantic version string of this plugin.</summary>
	string Version { get; }

	/// <summary>Ids of other plugins this plugin must load after. Drives the topological load order.</summary>
	IReadOnlyList<string> Dependencies { get; }

	/// <summary>Tie-break ordering among plugins with no dependency relationship. Lower loads first.</summary>
	int Priority { get; }

	/// <summary>
	/// Called once, before the plugin's commands/functions are registered, so the plugin can resolve
	/// engine services from the root <paramref name="services"/> provider. Default is a no-op.
	/// Plugin commands/functions should resolve services at call time via
	/// <c>parser.ServiceProvider.GetRequiredService&lt;T&gt;()</c>; this hook exists for any one-time setup.
	/// </summary>
	void Initialize(IServiceProvider services);
}
