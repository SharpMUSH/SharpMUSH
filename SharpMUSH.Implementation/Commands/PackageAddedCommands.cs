using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// The commands packages declare (<c>commands:</c>), reached through
/// <see cref="Services.RuntimeRegistrationService"/>: an <c>@command/add</c> command hooked with
/// <c>@hook/override/inline/localize/clearregs</c> to the package's attribute.
/// </summary>
public partial class Commands
{
	/// <summary>Whether <paramref name="name"/> can be a package's command: valid, and not a built-in command or another command's alias.</summary>
	internal async ValueTask<Result<Success>> CheckPackageCommandAsync(string name)
	{
		if (!await ValidCommandName(name))
		{
			return new Error<string>($"{name} is not a valid command name.");
		}

		return FindCommand(name) switch
		{
			null => new Success(),
			{ LibraryInformation: var definition } when !IsAddedCommand(definition)
				=> new Error<string>($"{name} is a built-in command."),
			{ LibraryInformation: var definition } when !definition.Attribute.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
				=> new Error<string>($"{name} is another name for {definition.Attribute.Name}."),
			_ => new Success()
		};
	}

	/// <summary>Adds <paramref name="command"/> unless <c>@command/add</c> already made it, and hooks it.</summary>
	internal async ValueTask<Result<Success>> RegisterPackageCommandAsync(PackageCommandRecord command)
	{
		if (!DBRef.TryParse(command.Objid, out var target) || target is not { } holder)
		{
			return new Error<string>($"{command.Name}: '{command.Objid}' is not an object id.");
		}

		if (await CheckPackageCommandAsync(command.Name) is Error<string> refused)
		{
			return refused;
		}

		AddUnhookedCommand(command.Name, CommandBehavior.Default, keepExisting: true);

		await HookService.SetHookAsync(command.Name, "OVERRIDE", holder, command.Attribute,
			inline: true, nobreak: command.NoBreak, localize: true, clearregs: true);
		return new Success();
	}

	/// <summary>
	/// Clears <paramref name="command"/>'s override hook if it still runs the package's object, then removes the command
	/// if it is an added one left with no hook.
	/// </summary>
	internal async ValueTask RemovePackageCommandAsync(PackageCommandRecord command)
	{
		if (DBRef.TryParse(command.Objid, out var target) && target is { } holder
			&& await HookService.GetHookAsync(command.Name, "OVERRIDE") is CommandHook hook
			&& hook.TargetObject.SameObjectAs(holder))
		{
			await HookService.ClearHookAsync(command.Name, "OVERRIDE");
		}

		await ForgetUnhookedAddedCommandsAsync([command.Name]);
	}
}
