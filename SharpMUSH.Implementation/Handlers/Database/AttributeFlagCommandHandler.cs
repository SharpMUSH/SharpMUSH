using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributeFlagCommandHandler(ISharpDatabase database) : ICommandHandler<SetAttributeFlagCommand, bool>
{
	public async ValueTask<bool> Handle(SetAttributeFlagCommand request, CancellationToken cancellationToken)
	{
		await database.SetAttributeFlagAsync(request.Target, request.Flag);
		return true;
	}
}

public class UnsetAttributeFlagCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetAttributeFlagCommand, bool>
{
	public async ValueTask<bool> Handle(UnsetAttributeFlagCommand request, CancellationToken cancellationToken)
	{
		await database.UnsetAttributeFlagAsync(request.Target, request.Flag);
		return true;
	}
}