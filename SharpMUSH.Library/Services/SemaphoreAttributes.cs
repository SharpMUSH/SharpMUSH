using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Services;

/// <summary>Metadata for a newly created custom semaphore, before publishing waiting work or credits.</summary>
public static class SemaphoreAttributes
{
	public static IReadOnlyList<string> RequiredFlagNames { get; } = Array.AsReadOnly<string>(["no_inherit", "no_clone", "locked"]);
	public static ValueTask InitializeAsync(IMediator mediator, DBRef target, string[] path)
		=> InitializeAsync(mediator, target, path, null);

	/// <summary>Rechecks a guarded creation before each idempotent metadata write.</summary>
	public static async ValueTask InitializeAsync(IMediator mediator, DBRef target, string[] path,
		Func<SharpAttribute, ValueTask>? validate)
	{
		if (path.Length == 1 && path[0].Equals("SEMAPHORE", StringComparison.OrdinalIgnoreCase)) return;
		var token = ExecutionBudget.CurrentToken;
		var created = await mediator.CreateStream(new GetAttributeQuery(target, path), token).LastAsync(token);
		var flags = await mediator.CreateStream(new GetAttributeFlagsQuery(), token).ToArrayAsync(token);
		foreach (var name in RequiredFlagNames)
		{
			if (validate is not null)
			{
				created = await mediator.CreateStream(new GetAttributeQuery(target, path), token).LastAsync(token);
				await validate(created);
			}
			if (created.Flags.Any(flag => flag.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
			var flag = flags.SingleOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
				?? throw new InvalidOperationException($"Attribute flag '{name}' is not defined; cannot initialize semaphore attribute.");
			if (!await mediator.Send(new SetAttributeFlagCommand(target, created, flag), token))
				throw new InvalidOperationException($"Semaphore flag update failed: {name}.");
		}
	}
}
