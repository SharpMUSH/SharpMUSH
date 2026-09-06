using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// A loaded <see cref="SharpObject"/> carries its flags as a snapshot taken with it, so after the
/// write the handler updates the instance the caller holds (as <c>SetLockCommandHandler</c> does
/// for locks); the object's cache key is invalidated by the command, so the next load is fresh.
/// </summary>
public class SetObjectFlagCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectFlagCommand, bool>
{
	public async ValueTask<bool> Handle(SetObjectFlagCommand request, CancellationToken cancellationToken)
	{
		var set = await database.SetObjectFlagAsync(request.Target, request.Flag, cancellationToken);
		if (set)
		{
			var obj = request.Target.Object();
			var flags = await obj.Flags.Value.ToListAsync(cancellationToken);
			if (!flags.Any(f => f.Name.Equals(request.Flag.Name, StringComparison.OrdinalIgnoreCase)))
			{
				flags.Add(request.Flag);
			}

			obj.Flags = new(() => flags.ToAsyncEnumerable());
		}

		return set;
	}
}

public class UnsetObjectFlagCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetObjectFlagCommand, bool>
{
	public async ValueTask<bool> Handle(UnsetObjectFlagCommand request, CancellationToken cancellationToken)
	{
		var unset = await database.UnsetObjectFlagAsync(request.Target, request.Flag, cancellationToken);
		if (unset)
		{
			var obj = request.Target.Object();
			var flags = (await obj.Flags.Value.ToListAsync(cancellationToken))
				.Where(f => !f.Name.Equals(request.Flag.Name, StringComparison.OrdinalIgnoreCase))
				.ToList();
			obj.Flags = new(() => flags.ToAsyncEnumerable());
		}

		return unset;
	}
}
