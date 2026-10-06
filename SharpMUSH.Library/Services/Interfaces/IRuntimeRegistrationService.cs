using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The engine's in-memory registrations that name an object as their target: command hooks
/// (<c>@hook</c>), the commands <c>@command/add</c> made for those hooks to run, and global
/// user-defined functions (<c>@function</c>). None of them is stored with the object, so destroying or
/// retiring the object leaves them pointing at it until something forgets them.
/// </summary>
/// <remarks>
/// The command table is private to the command library, so the engine implements this; the package
/// installer and the PennMUSH converter only say which objects are going.
/// </remarks>
public interface IRuntimeRegistrationService
{
	/// <summary>
	/// Forgets every registration that targets one of <paramref name="targetObjects"/>: each hook on
	/// one of them, each global function backed by one of them, and then each command
	/// <c>@command/add</c> made (or cloned from one) that is left with no hook at all, under every name
	/// it answers to. A built-in command loses only its hooks, and an added command still hooked to
	/// another object stays.
	/// </summary>
	/// <param name="targetObjects">
	/// The objects that are going. A full objid matches only registrations on that object, so a
	/// recycled number is left alone; a bare dbref matches any registration on that number.
	/// </param>
	ValueTask<ForgottenRegistrations> ForgetRegistrationsOnAsync(IReadOnlyCollection<DBRef> targetObjects);
}

/// <summary>What <see cref="IRuntimeRegistrationService.ForgetRegistrationsOnAsync"/> forgot.</summary>
/// <param name="Hooks">The hooks cleared, on any command.</param>
/// <param name="Commands">The added commands removed, by name; their aliases went with them.</param>
/// <param name="Functions">The global functions (and function aliases) removed, by name.</param>
public sealed record ForgottenRegistrations(
	IReadOnlyList<ClearedHook> Hooks,
	IReadOnlyList<string> Commands,
	IReadOnlyList<string> Functions)
{
	public bool IsEmpty => Hooks.Count == 0 && Commands.Count == 0 && Functions.Count == 0;

	/// <summary>One line naming what was forgotten, for an operation's notes or warnings.</summary>
	public string Describe()
	{
		var parts = new List<string>();
		if (Hooks.Count > 0)
		{
			parts.Add($"{Hooks.Count} command hook(s) on {string.Join(", ", Hooks.Select(hook => hook.Command).Distinct())}");
		}

		if (Commands.Count > 0)
		{
			parts.Add($"the command(s) {string.Join(", ", Commands)}");
		}

		if (Functions.Count > 0)
		{
			parts.Add($"the global function(s) {string.Join(", ", Functions)}");
		}

		return parts.Count == 0 ? "No runtime registrations were removed." : $"Removed {string.Join("; ", parts)}.";
	}
}
