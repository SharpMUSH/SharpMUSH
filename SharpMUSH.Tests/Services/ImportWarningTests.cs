using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

public class ImportWarningTests
{
	[Test]
	public async Task WarningsGroupByKind_TheKindsThatNeedActionFirst()
	{
		var result = new ConversionResult
		{
			Warnings =
			[
				new ImportWarning(ImportWarningKind.Flags, "Flag A"),
				new ImportWarning(ImportWarningKind.Players, "Player B"),
				new ImportWarning(ImportWarningKind.Mail, "Mail C"),
				new ImportWarning(ImportWarningKind.Mail, "Mail D", NeedsAction: true)
			]
		};

		var groups = result.WarningsByKind().ToList();

		await Assert.That(groups.Select(group => group.Key))
			.IsEquivalentTo([ImportWarningKind.Mail, ImportWarningKind.Players, ImportWarningKind.Flags], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(groups[0].Select(warning => warning.Text)).IsEquivalentTo(["Mail D", "Mail C"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task AWarningKeptAsPlainTextReadsAsOther()
	{
		var warning = System.Text.Json.JsonSerializer.Deserialize<ImportWarning>("\"An old warning\"");

		await Assert.That(warning).IsEqualTo(new ImportWarning(ImportWarningKind.Other, "An old warning"));
	}

	[Test]
	public async Task AWarningRoundTrips()
	{
		var warning = new ImportWarning(ImportWarningKind.Players, "Passwords stored as bcrypt", NeedsAction: true);

		var json = System.Text.Json.JsonSerializer.Serialize(warning);

		await Assert.That(System.Text.Json.JsonSerializer.Deserialize<ImportWarning>(json)).IsEqualTo(warning);
		await Assert.That(json).Contains("\"Players\"");
	}
}
