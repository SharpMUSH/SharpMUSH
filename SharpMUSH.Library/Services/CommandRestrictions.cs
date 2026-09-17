using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The restrictions a command's behaviour carries. PennMUSH folds them into the command's lock
/// (<c>src/command.c</c>): <c>=#GOD</c>, <c>!POWER^GUEST</c> and <c>!FLAG^GAGGED</c>. Guest is a power,
/// and gagging is read from the owner, as <c>Gagged()</c> does.
/// </summary>
public static class CommandRestrictions
{
	public static async ValueTask<bool> PermitsAsync(SharpCommandAttribute attribute, AnySharpObject executor)
	{
		var behavior = attribute.Behavior;
		if (behavior.HasFlag(CommandBehavior.God) && !executor.IsGod()) return false;
		if (behavior.HasFlag(CommandBehavior.NoGuest) && await executor.IsGuest()) return false;
		if (!behavior.HasFlag(CommandBehavior.NoGagged)) return true;
		AnySharpObject owner = await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
		return !await owner.HasFlag("GAGGED");
	}
}
