using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Scene.Common;

/// <summary>
/// The scene package's Scene Logger, and the pose-type catalogue it holds: one <c>TYPE`&lt;KEY&gt;</c>
/// attribute of JSON per type (<see cref="PoseTypes.Read"/>). The catalogue is read from the logger itself,
/// never its parents, and only attributes one level deep, so <c>TYPE`IC`FORMAT</c> is not a type.
/// </summary>
public static class SceneLogger
{
	private const string TypePrefix = "TYPE`";

	/// <summary>The Scene Logger, or null when the scene package is not installed.</summary>
	public static async ValueTask<AnySharpObject?> FindAsync(IServiceProvider services)
	{
		if (services.GetService<IPackageRegistryService>() is not { } registry
			|| (await registry.GetPackageObjectsAsync("scene")).FirstOrDefault(o => o.Ref == "logger") is not { } record
			|| !DBRef.TryParse(record.Objid, out var parsed) || parsed is not { } reference)
			return null;

		var mediator = services.GetRequiredService<IMediator>();
		return await mediator.Send(new GetObjectNodeQuery(reference)) is AnySharpObject logger ? logger : null;
	}

	/// <summary>Every pose type the logger defines, in order, and the attributes that do not read as one.</summary>
	public static async ValueTask<PoseTypeCatalogue> CatalogueAsync(IServiceProvider services)
	{
		if (await FindAsync(services) is not AnySharpObject logger) return PoseTypeCatalogue.Empty;

		var attributes = services.GetRequiredService<IAttributeService>();
		if (await attributes.GetAttributePatternAsync(logger, logger, TypePrefix + "*", false, IAttributeService.AttributePatternMode.Wildcard)
			is not SharpAttribute[] found)
			return PoseTypeCatalogue.Empty;

		var types = new List<PoseType>();
		var problems = new List<PoseTypeProblem>();
		foreach (var attribute in found)
		{
			var name = attribute.LongName;
			if (!name.StartsWith(TypePrefix, StringComparison.OrdinalIgnoreCase)) continue;
			var key = name[TypePrefix.Length..];
			if (key.Length == 0 || key.Contains('`')) continue;

			switch (PoseTypes.Read(key, attribute.Value.ToPlainText()))
			{
				case PoseType type:
					types.Add(type);
					break;
				case Error<string> error:
					problems.Add(new PoseTypeProblem(key.ToLowerInvariant(), error.Value));
					break;
			}
		}

		return new PoseTypeCatalogue(
			[.. types.OrderBy(t => t.Order).ThenBy(t => t.Key, StringComparer.Ordinal)],
			[.. problems.OrderBy(p => p.Key, StringComparer.Ordinal)]);
	}

	/// <summary>
	/// The types <paramref name="reader"/> leaves out of their recall and log, as the scene package's
	/// <c>FUN`HIDDEN</c> works it out: the ones in their <c>SCENE`HIDE</c>, and the ones the catalogue starts
	/// hidden that are not in their <c>SCENE`SHOW</c>. A reader with no character gets the catalogue's.
	/// </summary>
	public static async ValueTask<IReadOnlyList<string>> HiddenForAsync(IServiceProvider services, PoseTypeCatalogue catalogue,
		DBRef? reader)
	{
		var mediator = services.GetRequiredService<IMediator>();
		var character = reader is { } who && await mediator.Send(new GetObjectNodeQuery(who)) is AnySharpObject found ? found : null;
		var hide = character is null ? [] : await WordsAsync(services, character, "SCENE`HIDE");
		var show = character is null ? [] : await WordsAsync(services, character, "SCENE`SHOW");
		// A hidden key whose type was removed is left out, as FUN`HIDDEN leaves it out of recall: those poses draw
		// as in character, and no Show menu lists the type to reveal them.
		var listed = catalogue.Types.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
		return [.. hide.Where(listed.Contains).Union(catalogue.Types.Where(t => t.Hidden && !show.Contains(t.Key)).Select(t => t.Key)).Distinct()];
	}

	private static async ValueTask<string[]> WordsAsync(IServiceProvider services, AnySharpObject character, string attribute) =>
		await services.GetRequiredService<IAttributeService>()
				.GetAttributeAsync(character, character, attribute, IAttributeService.AttributeMode.Read, parent: false)
			is SharpAttribute[] { Length: > 0 } chain
			? chain[^1].Value.ToPlainText().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
			: [];

}
