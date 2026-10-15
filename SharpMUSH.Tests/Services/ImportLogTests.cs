using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

public class ImportLogTests
{
	private static readonly DateTimeOffset Finished = new(2026, 10, 15, 9, 30, 5, TimeSpan.Zero);

	[Test]
	public async Task NamesTheFileForWhenTheImportFinished()
		=> await Assert.That(ImportLog.FileName(Finished)).IsEqualTo("sharpmush-import-20261015-093005.log");

	[Test]
	public async Task WritesTheWholeReport()
	{
		var result = new ConversionResult
		{
			SourceName = "TinyMUX",
			PlayersConverted = 2,
			AttributesConverted = 7,
			Duration = TimeSpan.FromSeconds(83),
			Warnings = ["A warning"],
			SoftcodeRewrites =
			[
				new SoftcodeRewrite("#9/DESCRIBE", "%c/%x colour written as ansi()", "%crRed%cn\nline two", "ansi(r,Red)\nline two")
			]
		};

		var log = ImportLog.Write(result, Finished).ReplaceLineEndings("\n");

		await Assert.That(log).IsEqualTo("""
			SharpMUSH database import log
			Source: TinyMUX
			Finished: 2026-10-15 09:30:05 UTC
			Duration: 00:01:23
			Result: completed

			Converted
			  Players: 2
			  Rooms: 0
			  Things: 0
			  Exits: 0
			  Attributes: 7
			  Locks: 0
			  Mail aliases: 0
			  Mail messages: 0
			  Channels: 0
			  Channel members: 0

			Warnings (1)
			  A warning

			Rewritten softcode (1)

			#9/DESCRIBE: %c/%x colour written as ansi()
			  Before:
			    %crRed%cn
			    line two
			  After:
			    ansi(r,Red)
			    line two

			""");
	}

	[Test]
	public async Task SaysWhenTheImportFailed()
	{
		var log = ImportLog.Write(new ConversionResult { Errors = ["Bad object #4"] }, Finished);

		await Assert.That(log).Contains("Result: completed with errors").And.Contains("Errors (1)").And.Contains("  Bad object #4")
			.And.DoesNotContain("Source:");
	}
}
