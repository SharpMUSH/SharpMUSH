using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Forgets the hooks, global functions and hook-less added commands that target objects which are
/// going (see <see cref="IRuntimeRegistrationService"/>). The command table belongs to the
/// <see cref="Commands.Commands"/> singleton, which is what the command library resolves to. It also adds and removes
/// the commands packages declare (<see cref="IPackageCommandRegistrar"/>).
/// </summary>
public sealed class RuntimeRegistrationService(
	ILibraryProvider<CommandDefinition> commandLibrary,
	IHookService hooks,
	IUserDefinedFunctionService functions) : IRuntimeRegistrationService, IPackageCommandRegistrar
{
	public async ValueTask<ForgottenRegistrations> ForgetRegistrationsOnAsync(IReadOnlyCollection<DBRef> targetObjects)
	{
		if (targetObjects.Count == 0)
		{
			return new ForgottenRegistrations([], [], []);
		}

		var clearedHooks = await hooks.ClearHooksOnAsync(targetObjects);

		// An alias or a clone copies its target's object, so it is matched by the same test.
		var forgottenFunctions = new List<string>();
		foreach (var function in functions.All().Where(function => targetObjects.Any(function.Object.Matches)))
		{
			if (functions.Delete(function.Name))
			{
				forgottenFunctions.Add(function.Name);
			}
		}

		IReadOnlyList<string> forgottenCommands = commandLibrary is Commands.Commands commands
			? await commands.ForgetUnhookedAddedCommandsAsync(clearedHooks.Select(hook => hook.Command))
			: [];

		return new ForgottenRegistrations(clearedHooks, forgottenCommands, forgottenFunctions);
	}

	public async ValueTask<Result<Success>> CheckAsync(string name)
		=> commandLibrary is Commands.Commands commands
			? await commands.CheckPackageCommandAsync(name.ToUpperInvariant())
			: new Error<string>("The command table cannot take package commands.");

	public async ValueTask<Result<Success>> RegisterAsync(PackageCommandRecord command)
		=> commandLibrary is Commands.Commands commands
			? await commands.RegisterPackageCommandAsync(command with { Name = command.Name.ToUpperInvariant(), Attribute = command.Attribute.ToUpperInvariant() })
			: new Error<string>("The command table cannot take package commands.");

	public async ValueTask RemoveAsync(PackageCommandRecord command)
	{
		if (commandLibrary is Commands.Commands commands)
		{
			await commands.RemovePackageCommandAsync(command with { Name = command.Name.ToUpperInvariant() });
		}
	}
}
