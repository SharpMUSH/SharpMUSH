using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Web;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>An <see cref="ISceneReadAccess"/> giving every reader the same answer: <c>scene.view</c> held, or not.</summary>
internal sealed class SceneReadAccessStub(bool mayRead = true) : ISceneReadAccess
{
	public ValueTask<bool> MayReadAsync(DBRef? reader, CancellationToken cancellationToken = default) => ValueTask.FromResult(mayRead);
}
