using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Palette;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Reads an object's <c>PALETTE`&lt;NAME&gt;</c> attributes and its parents', as
/// <see cref="GetCommandAttributesQueryHandler"/> reads <c>$</c>-commands: a child's attribute shadows a
/// parent's of the same name, and a parent's no_inherit attribute is not inherited.
/// </summary>
public class GetPaletteEntriesQueryHandler(
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: IQueryHandler<GetPaletteEntriesQuery, PaletteEntries>
{
	public async ValueTask<PaletteEntries> Handle(GetPaletteEntriesQuery request, CancellationToken cancellationToken)
	{
		var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var current = request.SharpObject.Object();
		await Collect(current, isLocal: true);

		var maxDepth = (int)configuration.CurrentValue.Limit.MaxParents;
		var visited = new HashSet<int> { current.DBRef.Number };
		for (var depth = 0; depth < maxDepth; depth++)
		{
			if (await current.Parent.WithCancellation(cancellationToken) is not AnySharpObject parent) break;
			current = parent.Object();
			if (!visited.Add(current.DBRef.Number)) break;
			await Collect(current, isLocal: false);
		}

		if (found.Count == 0) return PaletteEntries.None;

		var entries = new List<PaletteEntry>();
		var invalid = new List<string>();
		foreach (var (name, value) in found)
		{
			switch (PaletteEntry.Parse(name, value))
			{
				case PaletteEntry entry: entries.Add(entry); break;
				case Error<string> error: invalid.Add($"{name.ToUpperInvariant()}: {error.Value}"); break;
			}
		}

		var kept = entries
			.OrderBy(entry => entry.Order)
			.ThenBy(entry => entry.Name, StringComparer.Ordinal)
			.Take(PaletteEntry.MaxPerObject)
			.ToArray();
		return new PaletteEntries(kept, invalid, entries.Count - kept.Length);

		async ValueTask Collect(SharpObject holder, bool isLocal)
		{
			await foreach (var attribute in holder.AllAttributes.Value.WithCancellation(cancellationToken))
			{
				if (!attribute.LongName.StartsWith(PaletteEntry.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
				var name = attribute.LongName[PaletteEntry.Prefix.Length..];
				if (name.Length == 0 || name.Contains('`')) continue;
				if (!isLocal && attribute.Flags.Any(flag => flag.Name == "no_inherit")) continue;
				found.TryAdd(name, attribute.Value.ToPlainText());
			}
		}
	}
}
