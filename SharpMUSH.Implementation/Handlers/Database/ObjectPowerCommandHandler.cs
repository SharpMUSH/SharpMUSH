using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// A loaded <see cref="SharpObject"/> carries its powers as a snapshot taken with it, so after the
/// write the handler updates the instance the caller holds (as <c>SetLockCommandHandler</c> does
/// for locks); the object's cache key is invalidated by the command, so the next load is fresh.
/// </summary>
public class SetObjectPowerCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectPowerCommand, bool>
{
	public async ValueTask<bool> Handle(SetObjectPowerCommand request, CancellationToken cancellationToken)
	{
		var set = await database.SetObjectPowerAsync(request.Target, request.Flag, cancellationToken);
		if (set)
		{
			var obj = request.Target.Object();
			var powers = await obj.Powers.Value.ToListAsync(cancellationToken);
			if (!powers.Any(p => string.Equals(p.Name, request.Flag.Name, StringComparison.OrdinalIgnoreCase)))
			{
				powers.Add(request.Flag);
			}

			obj.Powers = new(() => powers.ToAsyncEnumerable());
		}

		return set;
	}
}

public class UnsetObjectPowerCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetObjectPowerCommand, bool>
{
	public async ValueTask<bool> Handle(UnsetObjectPowerCommand request, CancellationToken cancellationToken)
	{
		var unset = await database.UnsetObjectPowerAsync(request.Target, request.Flag, cancellationToken);
		if (unset)
		{
			var obj = request.Target.Object();
			var powers = (await obj.Powers.Value.ToListAsync(cancellationToken))
				.Where(p => !string.Equals(p.Name, request.Flag.Name, StringComparison.OrdinalIgnoreCase))
				.ToList();
			obj.Powers = new(() => powers.ToAsyncEnumerable());
		}

		return unset;
	}
}
