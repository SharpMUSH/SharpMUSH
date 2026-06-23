using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// A delegating decorator over <see cref="ISceneService"/> that layers cross-cutting behavior
/// (caching, audit, NATS broadcast, permission gates, …) around the storage core <b>without</b> touching
/// the core or the storage. Behaviors are composed by <c>AddSceneSystem(...).AddBehavior&lt;T&gt;()</c> in
/// registration order: the net chain is <c>behaviorN → … → behavior1 → storage-backed core</c>
/// (the <see cref="IHttpClientBuilder"/>.AddHttpMessageHandler model). A behavior receives the next
/// <see cref="ISceneService"/> in the chain as the FIRST constructor parameter; any further constructor
/// parameters are resolved from DI (composition is hand-rolled via <c>ActivatorUtilities.CreateInstance</c>,
/// no Scrutor).
/// </summary>
public interface ISceneServiceBehavior : ISceneService;
