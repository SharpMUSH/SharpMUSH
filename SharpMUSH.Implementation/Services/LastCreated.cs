using System.Globalization;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// The objects something last created, which <c>lastcreate()</c> reads: one per type and the last of any type, kept in
/// its <c>LASTCREATE</c> attribute as five numbers, a space between each, -1 where there is none. The order is room,
/// thing, exit, player, any, which is TinyMUX's <c>NEWOBJS</c>, so a TinyMUX import keeps that attribute as it is.
/// </summary>
public static class LastCreated
{
	public const string Attribute = "LASTCREATE";

	/// <summary>The slots of the record, in the order it stores them.</summary>
	public enum Slot { Room = 0, Thing = 1, Exit = 2, Player = 3, Any = 4 }

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IMediator, SemaphoreSlim> Gates = new();

	/// <summary>The five dbref numbers kept on <paramref name="creator"/>; -1 for each when there is no record.</summary>
	public static async ValueTask<int[]> StoredAsync(IMediator mediator, DBRef creator)
	{
		var stored = await mediator.CreateStream(new GetAttributeQuery(creator, [Attribute])).LastOrDefaultAsync();
		return Parse(stored?.Value.ToPlainText() ?? string.Empty);
	}

	/// <summary>The five numbers of a record; a missing or unreadable one is -1.</summary>
	public static int[] Parse(string text)
		=> [.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(field => int.TryParse(field.TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
				? number
				: -1)
			.Concat(Enumerable.Repeat(-1, 5))
			.Take(5)];

	/// <summary>
	/// Records <paramref name="created"/> as the last object of its type, and of any type, that <paramref name="creator"/>
	/// made. Written as God: the attribute is wizard-only and the creator is not the one writing it.
	/// </summary>
	public static async ValueTask RecordAsync(IMediator mediator, DBRef creator, Slot slot, DBRef created)
	{
		var gate = Gates.GetValue(mediator, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync();
		try
		{
			if (await HelperFunctions.GetGod(mediator) is not SharpPlayer god) return;

			var record = await StoredAsync(mediator, creator);
			record[(int)slot] = created.Number;
			record[(int)Slot.Any] = created.Number;
			await mediator.Send(new SetAttributeCommand(creator, [Attribute],
				MarkupText.Plain(string.Join(' ', record.Select(number => number.ToString(CultureInfo.InvariantCulture)))), god));
		}
		finally
		{
			gate.Release();
		}
	}
}
