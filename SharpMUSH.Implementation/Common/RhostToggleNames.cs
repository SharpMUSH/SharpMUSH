using Mediator;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;

namespace SharpMUSH.Implementation.Common;

/// <summary>RhostMUSH's toggle names as the flags they are here (<see cref="RhostNameTables.Toggles"/>).</summary>
public static class RhostToggleNames
{
	/// <summary>The names of the flags this game defines, which decide which toggles it has.</summary>
	public static async ValueTask<HashSet<string>> DefinedFlagsAsync(IMediator mediator)
		=> await mediator.CreateStream(new GetAllObjectFlagsQuery())
			.Select(flag => flag.Name).ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

	/// <summary>The flag a toggle name stands for on this game, or null when the game has no such toggle.</summary>
	public static string? FlagOf(string name, IReadOnlySet<string> defined)
		=> RhostNameTables.Toggle(name) is { Flag: { } flag } && defined.Contains(flag) ? flag : null;

	/// <summary>
	/// A list of <c>[!]toggle</c> words as the <c>[!]flag</c> words <c>@set</c> takes, and the words that name no toggle
	/// this game has.
	/// </summary>
	public static (List<string> Flags, List<string> Unknown) Translate(string list, IReadOnlySet<string> defined)
	{
		var flags = new List<string>();
		var unknown = new List<string>();
		foreach (var word in list.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var negate = word.StartsWith('!');
			var name = negate ? word[1..] : word;
			if (FlagOf(name, defined) is { } flag)
			{
				flags.Add(negate ? $"!{flag}" : flag);
			}
			else
			{
				unknown.Add(name);
			}
		}

		return (flags, unknown);
	}

	/// <summary>The flags of <paramref name="target"/> that <paramref name="viewer"/> may see.</summary>
	public static async ValueTask<IEnumerable<string>> VisibleFlagNamesAsync(AnySharpObject viewer, AnySharpObject target,
		IConnectionService connections)
		=> (await MessageFormatting.VisibleFlagsAsync(target.Object(), await FlagView.ForAsync(viewer, connections)))
			.Select(flag => flag.Name);

	/// <summary>The toggles among <paramref name="flagNames"/>, under RhostMUSH's names, in its order.</summary>
	public static IEnumerable<string> ToggleNamesOf(IEnumerable<string> flagNames)
	{
		var held = flagNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return RhostNameTables.Toggles.Where(toggle => toggle.Flag is { } flag && held.Contains(flag)).Select(toggle => toggle.Name);
	}
}
