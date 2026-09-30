using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Writes a gallery: the <c>PROFILE`GALLERY</c> JSON, then its mirror into <c>IMAGE</c>,
/// <c>IMAGE`BANNER</c> and <c>IMAGE`ALT</c> (<see cref="GalleryRules.Mirror"/>). The attribute store has
/// no transaction spanning several attributes, so the write is all-or-nothing by compensation: every
/// attribute it touches is read first, and when a step fails the steps already taken are put back in
/// reverse order. The portal's gallery and softcode's image attributes therefore never disagree about
/// which picture is the character's.
/// </summary>
public static class GalleryWriter
{
	public const string GalleryAttribute = "PROFILE`GALLERY";

	/// <summary>The character's attributes, as the writer reads and writes them.</summary>
	public interface IStore
	{
		/// <summary>The attribute's value, or <c>null</c> when the character has no such attribute.</summary>
		ValueTask<MString?> ReadAsync(string attribute);

		ValueTask<Result<Success>> SetAsync(string attribute, MString value);

		ValueTask<Result<Success>> ClearAsync(string attribute);
	}

	public static async Task<Result<Success>> WriteAsync(IStore store, IReadOnlyList<GalleryEntry> entries, ILogger logger)
	{
		// Parents before children: sets run IMAGE first, clears run it last, so a branch attribute is
		// never written under a missing parent or cleared out from under a child. Undoing in reverse
		// keeps the same order.
		var mirror = GalleryRules.Mirror(entries);
		(string Attribute, string Value)[] mirrored = [("IMAGE", mirror.Image), ("IMAGE`BANNER", mirror.Banner), ("IMAGE`ALT", mirror.Alt)];
		var steps = mirrored.Where(m => m.Value.Length > 0)
			.Concat(mirrored.Where(m => m.Value.Length == 0).Reverse())
			.Prepend((GalleryAttribute, JsonSerializer.Serialize(entries)))
			.ToList();

		var before = new Dictionary<string, MString?>(StringComparer.Ordinal);
		foreach (var (attribute, _) in steps)
		{
			before[attribute] = await store.ReadAsync(attribute);
		}

		var taken = new Stack<string>();
		foreach (var (attribute, value) in steps)
		{
			if (await ApplyAsync(store, attribute, value.Length == 0 ? null : MString.Plain(value)) is Error<string> error)
			{
				logger.LogWarning("Writing the gallery stopped at {Attribute}: {Error}; putting back {Count} earlier step(s).",
					attribute, error.Value, taken.Count);
				await UndoAsync(store, taken, before, logger);
				return error;
			}

			taken.Push(attribute);
		}

		return new Success();
	}

	private static ValueTask<Result<Success>> ApplyAsync(IStore store, string attribute, MString? value) =>
		value is null ? store.ClearAsync(attribute) : store.SetAsync(attribute, value);

	private static async Task UndoAsync(IStore store, Stack<string> taken, Dictionary<string, MString?> before, ILogger logger)
	{
		while (taken.TryPop(out var attribute))
		{
			if (await ApplyAsync(store, attribute, before[attribute]) is Error<string> error)
			{
				logger.LogError("Could not put {Attribute} back after a failed gallery write: {Error}.", attribute, error.Value);
			}
		}
	}
}
