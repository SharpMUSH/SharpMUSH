using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// Regression tests for flag-list parsing: splitting on spaces must not produce
/// empty entries (which rendered as blank chips on the Flags config page), even
/// when the configured value has extra whitespace or the default is empty.
/// </summary>
public class ReadPennMushConfigFlagTests
{
	private static string WriteTempConfig(params string[] lines)
	{
		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-cnf-{Guid.NewGuid():N}.cnf");
		File.WriteAllLines(path, lines);
		return path;
	}

	[Test]
	public async Task PlayerFlags_WithExtraWhitespace_HasNoEmptyEntries()
	{
		var path = WriteTempConfig("player_flags enter_ok  ansi   no_command");
		try
		{
			var options = ReadPennMushConfig.Create(path);

			await Assert.That(options.Flag.PlayerFlags).IsEquivalentTo(new[] { "enter_ok", "ansi", "no_command" });
			await Assert.That(options.Flag.PlayerFlags!).DoesNotContain(string.Empty);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task ThingFlags_WithEmptyDefault_IsEmptyNotASingleBlankEntry()
	{
		// No thing_flags line -> falls back to the empty default, which previously
		// produced [""] (one blank chip) instead of an empty list.
		var path = WriteTempConfig("player_flags enter_ok");
		try
		{
			var options = ReadPennMushConfig.Create(path);

			await Assert.That(options.Flag.ThingFlags!).IsEmpty();
		}
		finally
		{
			File.Delete(path);
		}
	}
}
