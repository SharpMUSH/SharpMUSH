using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Whether an object may run a built-in command at all, before its arguments are looked at.</summary>
public interface ICommandPermission
{
	/// <summary>True when <paramref name="command"/> names a built-in command or one of its aliases.</summary>
	bool IsBuiltin(string command);

	/// <summary>
	/// True when <paramref name="command"/> (a name or alias) is a built-in that is not internal or
	/// disabled, whose <c>@command</c> restrictions let <paramref name="executor"/> through, and whose
	/// command lock passes for it.
	/// </summary>
	ValueTask<bool> CanRunAsync(AnySharpObject executor, string command);

	/// <summary>The names of every built-in <paramref name="executor"/> may run, by <see cref="CanRunAsync"/>.</summary>
	ValueTask<IReadOnlyList<string>> RunnableAsync(AnySharpObject executor);
}
