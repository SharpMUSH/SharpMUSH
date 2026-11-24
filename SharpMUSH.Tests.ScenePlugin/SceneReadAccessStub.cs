using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Scene.Web;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>An <see cref="ISceneReadAccess"/> giving every reader the same answers: <c>scene.view</c> held or not, wizard or not.</summary>
internal sealed class SceneReadAccessStub(bool mayRead = true, bool seesEvery = false) : ISceneReadAccess
{
	public ValueTask<bool> MayReadAsync(DBRef? reader, CancellationToken cancellationToken = default) => ValueTask.FromResult(mayRead);

	public ValueTask<bool> SeesEveryAsync(DBRef? reader, CancellationToken cancellationToken = default) => ValueTask.FromResult(seesEvery);
}
