using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>Default <see cref="ISceneSystemBuilder"/>; records behavior types in registration order.</summary>
internal sealed class SceneSystemBuilder(IServiceCollection services) : ISceneSystemBuilder
{
	private readonly List<Type> _behaviorTypes = [];

	public IServiceCollection Services => services;

	/// <summary>Behavior types in registration order (storage-nearest first); composed innermost→outermost.</summary>
	public IReadOnlyList<Type> BehaviorTypes => _behaviorTypes;

	public ISceneSystemBuilder AddBehavior<T>() where T : class, ISceneServiceBehavior
	{
		_behaviorTypes.Add(typeof(T));
		return this;
	}
}
