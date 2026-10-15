using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <inheritdoc cref="ICommandPermission"/>
/// <remarks>
/// The library is reached lazily: the command library is built by <c>Commands</c>, which itself
/// depends on services that may depend on this one.
/// </remarks>
public sealed class CommandPermission(Lazy<LibraryService<string, CommandDefinition>> library, ILockService locks)
	: ICommandPermission
{
	public bool IsBuiltin(string command) => library.Value.ContainsKey(command);

	public async ValueTask<bool> CanRunAsync(AnySharpObject executor, string command)
		=> library.Value.TryGetValue(command, out var definition)
			&& await PermitsAsync(executor, definition.LibraryInformation.Attribute);

	public async ValueTask<IReadOnlyList<string>> RunnableAsync(AnySharpObject executor)
	{
		var runnable = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (_, (definition, _)) in library.Value)
		{
			var attribute = definition.Attribute;
			if (!runnable.Contains(attribute.Name) && await PermitsAsync(executor, attribute)) runnable.Add(attribute.Name);
		}

		return [.. runnable];
	}

	private async ValueTask<bool> PermitsAsync(AnySharpObject executor, SharpCommandAttribute attribute)
	{
		if (attribute.Behavior.HasFlag(CommandBehavior.NoOp) || attribute.Behavior.HasFlag(CommandBehavior.Internal)) return false;
		if (!await CommandRestrictions.PermitsAsync(attribute, executor)) return false;
		return string.IsNullOrEmpty(attribute.CommandLock) || await locks.Evaluate(attribute.CommandLock, executor, executor);
	}
}
