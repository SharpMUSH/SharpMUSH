using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// Fluent builder returned by <c>AddSceneSystem</c> for layering behaviors over the Scene storage core,
/// mirroring <c>IHttpClientBuilder</c>. Each <see cref="AddBehavior{T}"/> wraps the chain one level
/// further out (last added = outermost).
/// </summary>
public interface ISceneSystemBuilder
{
	/// <summary>The host service collection (so callers can register a behavior's own dependencies).</summary>
	IServiceCollection Services { get; }

	/// <summary>
	/// Layers an <see cref="ISceneServiceBehavior"/> decorator over the current chain. The behavior type's
	/// first constructor parameter receives the next <c>ISceneService</c> in the chain; any further
	/// parameters resolve from DI at composition time.
	/// </summary>
	ISceneSystemBuilder AddBehavior<T>() where T : class, ISceneServiceBehavior;
}
