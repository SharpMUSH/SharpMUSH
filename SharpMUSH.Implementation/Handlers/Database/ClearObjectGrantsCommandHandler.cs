using Mediator;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ClearObjectGrantsCommandHandler(IRoleRegistryService roles) : ICommandHandler<ClearObjectGrantsCommand, bool>
{
	public async ValueTask<bool> Handle(ClearObjectGrantsCommand command, CancellationToken cancellationToken)
	{
		var number = command.Target.Object().Key;
		var held = await roles.GetObjectRolesAsync(number, cancellationToken);
		foreach (var slug in held)
			await roles.RemoveRoleFromObjectAsync(number, slug);
		var overrides = await roles.GetObjectOverridesAsync(number, cancellationToken);
		foreach (var scope in overrides.Keys)
			await roles.SetObjectOverrideAsync(number, scope, PermissionState.Inherit);
		return held.Count > 0 || overrides.Count > 0;
	}
}

public class CopyObjectGrantsCommandHandler(IRoleRegistryService roles) : ICommandHandler<CopyObjectGrantsCommand, bool>
{
	public async ValueTask<bool> Handle(CopyObjectGrantsCommand command, CancellationToken cancellationToken)
	{
		var source = command.Source.Object().Key;
		var target = command.Target.Object().Key;
		var held = await roles.GetObjectRolesAsync(source, cancellationToken);
		foreach (var slug in held)
			await roles.AssignRoleToObjectAsync(target, slug);
		var overrides = await roles.GetObjectOverridesAsync(source, cancellationToken);
		foreach (var (scope, state) in overrides)
			await roles.SetObjectOverrideAsync(target, scope, state);
		return held.Count > 0 || overrides.Count > 0;
	}
}
