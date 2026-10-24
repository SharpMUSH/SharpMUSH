using System.Collections.Immutable;
using Mediator;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// <c>@wiki/property</c>: the properties a page's facts may state. With no argument it lists them, and
/// <c>&lt;property&gt;</c> shows one. <c>&lt;property&gt;=&lt;type&gt;</c> defines it or changes its type,
/// <c>&lt;property&gt;=</c> removes it, and <c>&lt;property&gt;/&lt;field&gt;=&lt;value&gt;</c> sets its label,
/// description, values or whether it takes several. Changing one needs wiki.admin; pages keep the facts they state.
/// </summary>
public static class PropertyWiki
{
	private const string Usage = "Usage: @wiki/property <property>=<number|keyword|page|boolean>, or <property>/<label|description|values|multiple>=<value>";

	public static async ValueTask<MString> List(IMUSHCodeParser parser, IMediator mediator, IWikiService wikiService,
		INotifyService notifyService)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var properties = (await wikiService.GetFactSchemaAsync()).Properties;
		Block body = properties.IsDefaultOrEmpty
			? new TextBlock(MarkupText.Plain("No properties."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Property")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Type")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Values")) { Wrap = false, Priority = 2 },
					new TableColumn(MarkupText.Plain("Label")) { Min = 10 },
				],
				properties.Select(p => new[] { p.Id, WikiFactProperties.TypeName(p.Type), p.Cardinality == Cardinality.Multiple ? "several" : "one", p.Label }));
		await notifyService.Notify(executor, ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Wiki properties"), body), 78, parser.HouseLook()), executor);
		return MarkupText.Plain(string.Join(' ', properties.IsDefault ? [] : properties.Select(p => p.Id)));
	}

	public static async ValueTask<MString> Show(IMUSHCodeParser parser, IMediator mediator, IWikiService wikiService,
		INotifyService notifyService, MString targetArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var id = targetArg.ToPlainText().Trim();
		if (WikiFactEntry.Definition(await wikiService.GetFactSchemaAsync(), id) is not { } property)
			return await RefusedAsync(notifyService, executor, $"No property {id} is defined.", ErrorMessages.Returns.NoSuchWikiProperty);

		await notifyService.Notify(executor, ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Property: {property.Id}"),
			ServerLayout.KeyValues(Fields(property).Select(field => (field.Label, MarkupText.Plain(field.Value))))), 78, parser.HouseLook()), executor);
		return MarkupText.Plain(property.Id);
	}

	public static async ValueTask<MString> Set(IMUSHCodeParser parser, IMediator mediator, IWikiService wikiService,
		INotifyService notifyService, MString targetArg, MString? valueArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (!(await WikiCommandHelper.ReaderAsync(parser, executor)).Has(PortalPermission.WikiAdmin))
			return await RefusedAsync(notifyService, executor, "Permission denied. That needs the wiki.admin permission.", ErrorMessages.Returns.PermissionDenied);

		var target = targetArg.ToPlainText().Trim();
		var value = valueArg?.ToPlainText().Trim() ?? string.Empty;
		var split = target.IndexOf('/');
		var (id, field) = split < 0 ? (target, null) : (target[..split].Trim(), target[(split + 1)..].Trim().ToLowerInvariant());
		var current = WikiFactEntry.Definition(await wikiService.GetFactSchemaAsync(), id);

		if (field is null && value.Length == 0)
			return await wikiService.DeleteFactPropertyAsync(id) switch
			{
				None => await TellAsync(notifyService, executor, $"Removed {id}. Pages that state it keep their facts.", id),
				NotFound => await RefusedAsync(notifyService, executor, $"No property {id} is defined.", ErrorMessages.Returns.NoSuchWikiProperty),
			};
		if (field is not null && current is null)
			return await RefusedAsync(notifyService, executor, $"No property {id} is defined.", ErrorMessages.Returns.NoSuchWikiProperty);

		return Changed(current, id, field, value) switch
		{
			PropertyDefinition changed => await StoredAsync(wikiService, notifyService, executor, current, changed),
			Error<string> error => await RefusedAsync(notifyService, executor, error.Value, ErrorMessages.Returns.InvalidWikiProperty),
		};
	}

	/// <summary><paramref name="current"/> (or a new property) with <paramref name="field"/> set; no field sets the type.</summary>
	private static Result<PropertyDefinition> Changed(PropertyDefinition? current, string id, string? field, string value)
	{
		var property = current ?? new PropertyDefinition(id, WikiFactProperties.SchemaVersion, SemanticType.Keyword, Cardinality.Single, []);
		return field switch
		{
			null => WikiFactProperties.ParseType(value) is { } type
				? property with { Type = type, AllowedKeywords = type == SemanticType.Keyword ? property.AllowedKeywords : [] }
				: new Error<string>($"'{value}' is not a property type: keyword, text, number, decimal, date, page or boolean."),
			"label" => property with { Label = value },
			"description" => property with { Description = value },
			"values" => property with
			{
				AllowedKeywords = [.. value.Split('|').Select(v => v.Trim()).Where(v => v.Length > 0).Select(v => new Keyword(v))]
			},
			"multiple" => value.ToLowerInvariant() switch
			{
				"yes" => property with { Cardinality = Cardinality.Multiple },
				"no" => property with { Cardinality = Cardinality.Single },
				_ => new Error<string>("Multiple takes yes or no."),
			},
			_ => new Error<string>(Usage),
		};
	}

	private static async ValueTask<MString> StoredAsync(IWikiService wikiService, INotifyService notifyService, AnySharpObject executor,
		PropertyDefinition? before, PropertyDefinition changed)
		=> await wikiService.SetFactPropertyAsync(changed) switch
		{
			PropertyDefinition stored => await TellAsync(notifyService, executor, before is null
				? $"Defined {stored.Id}: {WikiFactProperties.TypeName(stored.Type)}."
				: $"Changed {stored.Id}. Pages that state it keep their facts.", stored.Id),
			Error<string> error => await RefusedAsync(notifyService, executor, error.Value, ErrorMessages.Returns.InvalidWikiProperty),
		};

	private static IEnumerable<(string Label, string Value)> Fields(PropertyDefinition property)
	{
		yield return ("Type", WikiFactProperties.TypeName(property.Type));
		yield return ("Values", property.Cardinality == Cardinality.Multiple ? "several" : "one");
		if (!property.AllowedKeywords.IsDefaultOrEmpty)
			yield return ("Allowed", string.Join(" | ", property.AllowedKeywords.Select(k => k.Value)));
		if (property.Label.Length > 0) yield return ("Label", property.Label);
		if (property.Description.Length > 0) yield return ("Description", property.Description);
	}

	private static async ValueTask<MString> TellAsync(INotifyService notifyService, AnySharpObject executor, string message, string returned)
	{
		await notifyService.Notify(executor, $"WIKI: {message}", executor);
		return MarkupText.Plain(returned);
	}

	private static async ValueTask<MString> RefusedAsync(INotifyService notifyService, AnySharpObject executor, string why, string returned)
	{
		await notifyService.Notify(executor, $"WIKI: {why}", executor);
		return MarkupText.Plain(returned);
	}
}
