using System.Globalization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Plugins.World.Common;

/// <summary>A place on a stored map: its column, row and level.</summary>
public readonly record struct WorldPosition(int X, int Y, int Z)
{
	public override string ToString() => FormattableString.Invariant($"{X},{Y},{Z}");

	/// <summary><c>x,y</c> or <c>x,y,z</c>, each a whole number within reach; null otherwise.</summary>
	public static WorldPosition? Parse(string text)
	{
		var parts = text.Split(',', StringSplitOptions.TrimEntries);
		if (parts.Length is < 2 or > 3) return null;
		var numbers = new int[3];
		for (var i = 0; i < parts.Length; i++)
		{
			if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out numbers[i])) return null;
		}
		return WorldGrid.InReach(numbers[0], numbers[1], numbers[2]) ? new WorldPosition(numbers[0], numbers[1], numbers[2]) : null;
	}

	/// <summary>The place one step to <paramref name="side"/>.</summary>
	public WorldPosition Toward(WorldSide side)
	{
		var (dx, dy, dz) = WorldGrid.Step(side);
		return new WorldPosition(X + dx, Y + dy, Z + dz);
	}
}

/// <summary>Finds the map an argument names, as the executor sees it.</summary>
public static class WorldMaps
{
	/// <summary>The object <paramref name="name"/> names, telling the executor when nothing does.</summary>
	public static async ValueTask<AnySharpObject?> LocateAsync(IMUSHCodeParser parser, AnySharpObject executor, string name)
	{
		var locate = parser.ServiceProvider.GetRequiredService<ILocateService>();
		return await locate.LocateAndNotifyIfInvalid(parser, executor, executor, name, LocateFlags.All) is AnySharpObject found
			? found
			: null;
	}

	public static ValueTask<AnySharpObject> ExecutorAsync(IMUSHCodeParser parser) =>
		parser.CurrentState.KnownExecutorObject(parser.ServiceProvider.GetRequiredService<IMediator>());
}
