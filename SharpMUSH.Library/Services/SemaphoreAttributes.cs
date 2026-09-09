using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Services;

/// <summary>Metadata for a newly created custom semaphore, before publishing waiting work or credits.</summary>
public static class SemaphoreAttributes
{
	public static async ValueTask InitializeAsync(IMediator mediator, DBRef target, string[] path)
	{
		if (path.Length == 1 && path[0].Equals("SEMAPHORE", StringComparison.OrdinalIgnoreCase)) return;
		var token = ExecutionBudget.CurrentToken;
		var created = await mediator.CreateStream(new GetAttributeQuery(target, path), token).LastAsync(token);
		var flags = await mediator.CreateStream(new GetAttributeFlagsQuery(), token).ToArrayAsync(token);
		foreach (var name in new[] { "no_inherit", "no_clone", "locked" })
		{
			var flag = flags.Single(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
			if (!await mediator.Send(new SetAttributeFlagCommand(target, created, flag), token))
				throw new InvalidOperationException($"Semaphore flag update failed: {name}.");
		}
	}
}
