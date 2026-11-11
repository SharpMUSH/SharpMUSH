namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The names of every command an <see cref="IHookService"/> holds a hook for, built-in or not, for
/// <c>@hook/list</c> with no command. Kept apart from <see cref="IHookService"/>, whose members are fixed by the
/// plugin contract.
/// </summary>
public interface IHookCatalog
{
	/// <summary>Every command name with at least one hook, upper case.</summary>
	IReadOnlyList<string> HookedCommands();
}
