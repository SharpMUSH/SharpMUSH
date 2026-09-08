using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;

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
	public async Task ExitFlags_WithEmptyDefault_IsEmptyNotASingleBlankEntry()
	{
		// No exit_flags line -> falls back to the empty default, which is the one type PennMUSH ships
		// with no default flags at all.
		var path = WriteTempConfig("player_flags enter_ok");
		try
		{
			var options = ReadPennMushConfig.Create(path);

			await Assert.That(options.Flag.ExitFlags!).IsEmpty();
		}
		finally
		{
			File.Delete(path);
		}
	}

	/// <summary>
	/// The default flags for each type, as PennMUSH's own <c>game/mushcnf.dst</c> ships them:
	/// NO_COMMAND on players, rooms and things, and nothing on exits. NO_COMMAND keeps an object out
	/// of the $-command search, which is why it is a default at all — rooms and things were being
	/// created without it, so every one of them was searched on every command.
	/// </summary>
	[Test]
	public async Task OmittedFlagLines_FallBackToThePennMushDefaults()
	{
		var path = WriteTempConfig("mud_name Test");
		try
		{
			var flags = ReadPennMushConfig.Create(path).Flag;

			await Assert.That(flags.PlayerFlags).IsEquivalentTo(new[] { "enter_ok", "ansi", "no_command" });
			await Assert.That(flags.RoomFlags).IsEquivalentTo(new[] { "no_command" });
			await Assert.That(flags.ThingFlags).IsEquivalentTo(new[] { "no_command" });
			await Assert.That(flags.ExitFlags!).IsEmpty();
			await Assert.That(flags.ChannelFlags).IsEquivalentTo(new[] { "player" });
		}
		finally
		{
			File.Delete(path);
		}
	}

	/// <summary>
	/// The empty default must not split into a list holding one flag named "", which is what rooms and
	/// things used to be created with — it rendered as a blank chip on the Flags config page and was
	/// not a flag any object could have.
	/// </summary>
	[Test]
	public async Task TheEmptyDefault_SplitsToNothing()
		=> await Assert.That(FlagOptions.Defaults.Split(FlagOptions.Defaults.Exit)).IsEmpty();
}
