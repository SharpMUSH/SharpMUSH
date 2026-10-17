using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAccountEmailCommandHandler(IAccountStore store) : ICommandHandler<SetAccountEmailCommand, Result<Success>>
{
	public async ValueTask<Result<Success>> Handle(SetAccountEmailCommand command, CancellationToken ct)
	{
		if (await store.GetAccountByIdAsync(command.AccountId, ct) is null) return new Error<string>("Account not found.");
		if (command.Email is { Length: > 320 }) return new Error<string>("Email is too long.");
		if (command.Email is not null && await store.GetAccountByEmailAsync(command.Email, ct) is { } owner && owner.Id != command.AccountId)
			return new Error<string>("Email is already registered.");
		try { await store.UpdateAccountEmailAsync(command.AccountId, command.Email, ct); }
		catch (InvalidOperationException) { return new Error<string>("Email is already registered."); }
		return new Success();
	}
}
