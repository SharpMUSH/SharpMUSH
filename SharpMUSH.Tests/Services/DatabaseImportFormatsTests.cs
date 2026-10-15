using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>The format the upload API's <c>format</c> field and <c>PENNMUSH_DATABASE_FORMAT</c> name.</summary>
public class DatabaseImportFormatsTests
{
	[Test]
	[Arguments(null, DatabaseImportFormat.Auto)]
	[Arguments("", DatabaseImportFormat.Auto)]
	[Arguments(" ", DatabaseImportFormat.Auto)]
	[Arguments("auto", DatabaseImportFormat.Auto)]
	[Arguments("pennmush", DatabaseImportFormat.PennMUSH)]
	[Arguments("RhostMUSH", DatabaseImportFormat.RhostMUSH)]
	[Arguments("TINYMUX", DatabaseImportFormat.TinyMUX)]
	public async Task ReadsANamedFormat(string? text, DatabaseImportFormat expected)
	{
		await Assert.That(DatabaseImportFormats.TryParse(text, out var format)).IsTrue();
		await Assert.That(format).IsEqualTo(expected);
	}

	/// <summary>Enum.TryParse takes a number; a format is only ever a name.</summary>
	[Test]
	[Arguments("1")]
	[Arguments("9")]
	[Arguments("mux")]
	public async Task RefusesAnythingElse(string text)
	{
		await Assert.That(DatabaseImportFormats.TryParse(text, out _)).IsFalse();
	}
}
