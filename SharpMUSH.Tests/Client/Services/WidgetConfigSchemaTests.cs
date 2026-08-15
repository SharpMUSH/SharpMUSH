using System.Text.Json;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Unit tests for <see cref="WidgetConfigSchema"/> — the reflection that turns a widget's
/// <c>ConfigType</c> into the key reference the layout editor shows admins.
/// </summary>
public class WidgetConfigSchemaTests
{
	[Test]
	public async Task Describe_NullConfigType_IsEmpty()
	{
		await Assert.That(WidgetConfigSchema.Describe(null)).IsEmpty();
	}

	[Test]
	public async Task Describe_UnannotatedType_IsEmpty()
	{
		// Documentation is opt-in: a config model with no attributes yields no reference rows.
		await Assert.That(WidgetConfigSchema.Describe(typeof(ProfilePageContext))).IsEmpty();
	}

	[Test]
	public async Task Describe_UsesCamelCaseJsonKeys_InDeclarationOrder()
	{
		var keys = WidgetConfigSchema.Describe(typeof(WikiBodyConfig)).Select(f => f.Key).ToList();

		await Assert.That(keys).IsEquivalentTo(new[] { "slug", "namespace", "category", "locale", "character" });
	}

	[Test]
	public async Task Describe_LabelsJsonTypes()
	{
		var fields = WidgetConfigSchema.Describe(typeof(WelcomeTextConfig)).ToDictionary(f => f.Key);

		await Assert.That(fields["markdown"].TypeLabel).IsEqualTo("string");
		await Assert.That(fields["showToGuests"].TypeLabel).IsEqualTo("boolean");
		await Assert.That(WidgetConfigSchema.Describe(typeof(SpacerConfig))[0].TypeLabel).IsEqualTo("integer");
		await Assert.That(WidgetConfigSchema.Describe(typeof(QuickLinksConfig))[0].TypeLabel).IsEqualTo("list");
	}

	[Test]
	public async Task Describe_ReadsDefaultsFromThePrimaryConstructor()
	{
		var spacer = WidgetConfigSchema.Describe(typeof(SpacerConfig))[0];
		var welcome = WidgetConfigSchema.Describe(typeof(WelcomeTextConfig)).ToDictionary(f => f.Key);

		await Assert.That(spacer.Default).IsEqualTo("24");
		await Assert.That(welcome["showToGuests"].Default).IsEqualTo("true");
		// A required parameter has no default to show.
		await Assert.That(welcome["markdown"].Default).IsNull();
	}

	[Test]
	public async Task Describe_AttributeDefault_OverridesTheClrDefault()
	{
		// WikiBodyConfig.Namespace is null in the record, but null means the Main namespace.
		var fields = WidgetConfigSchema.Describe(typeof(WikiBodyConfig)).ToDictionary(f => f.Key);

		await Assert.That(fields["namespace"].Default).IsEqualTo("\"main\"");
		await Assert.That(fields["category"].Default).IsEqualTo("\"general\"");
		await Assert.That(fields["slug"].Default).IsNull();
	}

	[Test]
	public async Task Describe_DescendsIntoListElementTypes()
	{
		var links = WidgetConfigSchema.Describe(typeof(QuickLinksConfig))[0];

		await Assert.That(links.Key).IsEqualTo("links");
		await Assert.That(links.Children.Select(c => c.Key))
			.IsEquivalentTo(new[] { "label", "url", "icon", "newTab" });
	}

	[Test]
	public async Task TemplateJson_IsValidJson_CoveringEveryKey()
	{
		var fields = WidgetConfigSchema.Describe(typeof(WikiBodyConfig));

		using var doc = JsonDocument.Parse(WidgetConfigSchema.TemplateJson(fields));

		await Assert.That(doc.RootElement.ValueKind).IsEqualTo(JsonValueKind.Object);
		await Assert.That(doc.RootElement.EnumerateObject().Select(p => p.Name))
			.IsEquivalentTo(new[] { "slug", "namespace", "category", "locale", "character" });
		await Assert.That(doc.RootElement.GetProperty("namespace").GetString()).IsEqualTo("main");
	}

	[Test]
	public async Task TemplateJson_NestsListElements()
	{
		var json = WidgetConfigSchema.TemplateJson(WidgetConfigSchema.Describe(typeof(QuickLinksConfig)));

		using var doc = JsonDocument.Parse(json);
		var links = doc.RootElement.GetProperty("links");

		await Assert.That(links.ValueKind).IsEqualTo(JsonValueKind.Array);
		await Assert.That(links[0].GetProperty("newTab").GetBoolean()).IsFalse();
		await Assert.That(links[0].GetProperty("label").GetString()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task TemplateJson_NoFields_IsAnEmptyObject()
	{
		await Assert.That(WidgetConfigSchema.TemplateJson([])).IsEqualTo("{}");
	}

	[Test]
	public async Task TemplateJson_StringDefaultNeedingEscapes_StaysValidJson()
	{
		var quoted = JsonSerializer.Serialize("say \"hi\"\\now");
		var field = new WidgetConfigField("greeting", "string", quoted, "LayCfgNone", []);

		using var doc = JsonDocument.Parse(WidgetConfigSchema.TemplateJson([field]));

		await Assert.That(doc.RootElement.GetProperty("greeting").GetString()).IsEqualTo("say \"hi\"\\now");
	}

	[Test]
	public async Task TemplateJson_MalformedAttributeDefault_FallsBackToThePlaceholder()
	{
		// A hand-written attribute default that is not a JSON literal must degrade the template
		// rather than emit something the editor cannot parse.
		var field = new WidgetConfigField("greeting", "string", "main", "LayCfgNone", []);

		using var doc = JsonDocument.Parse(WidgetConfigSchema.TemplateJson([field]));

		await Assert.That(doc.RootElement.GetProperty("greeting").GetString()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Describe_OrdersByPrimaryConstructorPosition_NotReflectionOrder()
	{
		// GetProperties() order is unspecified; the reference table and Insert template both depend
		// on declaration order, so it is pinned to the record's parameter positions.
		var links = WidgetConfigSchema.Describe(typeof(QuickLinksConfig))[0];
		var order = string.Join(",", links.Children.Select(c => c.Key));

		await Assert.That(order).IsEqualTo("label,url,icon,newTab");

		var wikiBody = string.Join(",", WidgetConfigSchema.Describe(typeof(WikiBodyConfig)).Select(f => f.Key));
		await Assert.That(wikiBody).IsEqualTo("slug,namespace,category,locale,character");
	}

	[Test]
	public async Task EveryDescriptorThatDeclaresAConfigType_DocumentsAtLeastOneKey()
	{
		IPortalWidget[] descriptors =
		[
			new QuickLinksWidgetDescriptor(), new WelcomeTextWidgetDescriptor(),
			new SpacerWidgetDescriptor(), new WikiBodyWidgetDescriptor(),
			new CharacterGalleryWidgetDescriptor(), new SchemaWidgetDescriptor()
		];

		var undocumented = descriptors
			.Where(d => d.ConfigType is not null && WidgetConfigSchema.Describe(d.ConfigType).Count == 0)
			.Select(d => d.Name)
			.ToList();

		await Assert.That(undocumented).IsEmpty();
	}
}
