using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Adds and removes the commands packages declare (<c>commands:</c>): each one is put in the command table as
/// <c>@command/add</c> would and hooked with <c>@hook/override/inline/localize/clearregs</c> (and <c>/nobreak</c> when
/// asked) to its attribute. Neither is stored, so the server adds them again on every boot from the installed
/// packages' records.
/// </summary>
/// <remarks>
/// The command table is private to the command library, so the engine implements this, beside
/// <see cref="IRuntimeRegistrationService"/>.
/// </remarks>
public interface IPackageCommandRegistrar
{
	/// <summary>
	/// Whether <paramref name="name"/> can be a package's command: a name no built-in or plugin command answers to.
	/// A command <c>@command/add</c> made can be taken over.
	/// </summary>
	/// <returns>The reason it cannot, or <see cref="Success"/>.</returns>
	ValueTask<Result<Success>> CheckAsync(string name);

	/// <summary>
	/// Adds <paramref name="command"/> unless it is already an added command, and hooks it to its attribute, replacing
	/// any override hook it had.
	/// </summary>
	/// <returns>The reason it could not, or <see cref="Success"/>.</returns>
	ValueTask<Result<Success>> RegisterAsync(PackageCommandRecord command);

	/// <summary>
	/// Clears <paramref name="command"/>'s override hook when it still targets the command's object, then removes the
	/// command, under every name it answers to, if <c>@command/add</c> made it and no hook is left on it.
	/// </summary>
	ValueTask RemoveAsync(PackageCommandRecord command);
}
