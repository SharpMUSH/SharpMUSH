using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The restrictions a command's behaviour carries. PennMUSH folds them into the command's lock
/// (<c>src/command.c</c>): <c>=#GOD</c>, <c>!POWER^GUEST</c> and <c>!FLAG^GAGGED</c>. Guest is a power,
/// and gagging is read from the owner, as <c>Gagged()</c> does.
/// </summary>
public static class CommandRestrictions
{
	/// <summary>
	/// Which restriction turns the executor away. <see cref="RefusalAsync"/> answers the behaviour ones;
	/// <see cref="Lock"/> is the command's CommandLock, which the dispatcher evaluates.
	/// </summary>
	public enum Refusal { None, God, App, Guest, Gagged, Lock }

	public static async ValueTask<bool> PermitsAsync(SharpCommandAttribute attribute, AnySharpObject executor)
		=> await RefusalAsync(attribute, executor) == Refusal.None;

	public static async ValueTask<Refusal> RefusalAsync(SharpCommandAttribute attribute, AnySharpObject executor)
	{
		var behavior = attribute.Behavior;
		if (behavior.HasFlag(CommandBehavior.God) && !executor.IsGod()) return Refusal.God;
		if (behavior.HasFlag(CommandBehavior.NoApp) && DelegatedApp.ActingAs(executor.Object().DBRef) is not null) return Refusal.App;
		if (behavior.HasFlag(CommandBehavior.NoGuest) && await executor.IsGuest()) return Refusal.Guest;
		if (!behavior.HasFlag(CommandBehavior.NoGagged)) return Refusal.None;
		AnySharpObject owner = await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
		return await owner.HasFlag("GAGGED") ? Refusal.Gagged : Refusal.None;
	}

	/// <summary>
	/// The permissions a command lock asks for, when it asks for nothing else: a lock of
	/// <c>PERM^</c>, <c>POWER^</c>, <c>FLAG^WIZARD</c> and <c>FLAG^ROYALTY</c> keys joined by <c>|</c>, maybe in one
	/// pair of parentheses (as <c>@command/restrict</c> words become), which holding any one of those scopes passes.
	/// Any other lock gives none, and a refusal names the lock instead.
	/// </summary>
	public static string[] PermissionsOf(string commandLock)
	{
		var keys = commandLock.Trim() is ['(', .. var inner, ')'] ? inner : commandLock;
		var scopes = keys.Split('|', StringSplitOptions.TrimEntries)
			.Select(key => key.IndexOfAny(['(', ')', '&', '!', ' ']) < 0 ? ScopeOf(key) : null)
			.ToArray();
		return scopes.Length > 0 && scopes.All(scope => scope is not null) ? scopes.OfType<string>().ToArray() : [];
	}

	private static string? ScopeOf(string key)
		=> key.Split('^', 2) switch
		{
			[var type, var name] when name.Length > 0 && type.Equals("PERM", StringComparison.OrdinalIgnoreCase) => name,
			[var type, var name] when name.Length > 0 && type.Equals("POWER", StringComparison.OrdinalIgnoreCase) => PortalPermission.GamePower(name),
			[var type, var name] when type.Equals("FLAG", StringComparison.OrdinalIgnoreCase) && name.Equals("WIZARD", StringComparison.OrdinalIgnoreCase)
				=> PortalPermission.GameWizard,
			[var type, var name] when type.Equals("FLAG", StringComparison.OrdinalIgnoreCase) && name.Equals("ROYALTY", StringComparison.OrdinalIgnoreCase)
				=> PortalPermission.GameRoyalty,
			_ => null
		};
}
