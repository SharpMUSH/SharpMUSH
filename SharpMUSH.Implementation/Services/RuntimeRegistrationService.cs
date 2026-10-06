using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Forgets the hooks, global functions and hook-less added commands that target objects which are
/// going (see <see cref="IRuntimeRegistrationService"/>). The command table belongs to the
/// <see cref="Commands.Commands"/> singleton, which is what the command library resolves to.
/// </summary>
public sealed class RuntimeRegistrationService(
	ILibraryProvider<CommandDefinition> commandLibrary,
	IHookService hooks,
	IUserDefinedFunctionService functions) : IRuntimeRegistrationService
{
	public async ValueTask<ForgottenRegistrations> ForgetRegistrationsOnAsync(IReadOnlySet<int> targetObjects)
	{
		if (targetObjects.Count == 0)
		{
			return new ForgottenRegistrations([], [], []);
		}

		var clearedHooks = await hooks.ClearHooksOnAsync(targetObjects);

		// An alias or a clone copies its target's object, so it is matched by the same test.
		var forgottenFunctions = new List<string>();
		foreach (var function in functions.All().Where(function => targetObjects.Contains(function.Object.Number)))
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
}
