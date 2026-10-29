using Mediator;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Web;

/// <summary>
/// Whether a web reader holds <see cref="ScenePermissions.View"/>, the permission that lets them read scenes they are
/// not in. The scene REST controller, the portal controller and the live hub ask it once per request and hand the
/// answer to <see cref="SceneVisibility.CanSeeAsync"/>.
/// </summary>
public interface ISceneReadAccess
{
	/// <summary>
	/// True when <paramref name="reader"/>, the acting character, holds <c>scene.view</c>. A request with no character
	/// is answered for the <c>everyone</c> role.
	/// </summary>
	ValueTask<bool> MayReadAsync(DBRef? reader, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class SceneReadAccess(IMediator mediator, IRoleRegistryService roles, IPermissionResolver resolver)
	: ISceneReadAccess
{
	public async ValueTask<bool> MayReadAsync(DBRef? reader, CancellationToken cancellationToken = default)
		=> reader is { } character && await mediator.Send(new GetObjectNodeQuery(character), cancellationToken) is AnySharpObject found
			? await ScenePermissions.AllowsAsync(found, ScenePermissions.View, cancellationToken)
			: await ScenePermissions.EveryoneAllowsAsync(roles, resolver, ScenePermissions.View, cancellationToken);
}
