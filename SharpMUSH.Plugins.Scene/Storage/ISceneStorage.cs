using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// The storage-backed core of the Scene System: the <see cref="ISceneService"/> implementation that
/// reads and writes the world (<see cref="LightningSceneStorage"/>). Registered by
/// <c>AddSceneSystem</c> and wrapped with any registered <see cref="ISceneServiceBehavior"/>s.
/// </summary>
public interface ISceneStorage : ISceneService;
