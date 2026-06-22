using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// The storage-backed core of the Scene System: a provider-native <see cref="ISceneService"/>
/// implementation (<see cref="ArangoSceneStorage"/> / <see cref="MemgraphSceneStorage"/> /
/// <see cref="SurrealSceneStorage"/>). Registered as a KEYED service per provider
/// (<c>"arangodb"</c>/<c>"memgraph"</c>/<c>"surrealdb"</c>); the active one is resolved by
/// <c>AddSceneSystem</c> and wrapped with any registered <see cref="ISceneServiceBehavior"/>s.
/// </summary>
public interface ISceneStorage : ISceneService;
