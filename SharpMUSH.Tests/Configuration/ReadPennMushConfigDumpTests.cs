using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// The dump messages a <c>mush.cnf</c> sets. PennMUSH shows none for a <c>dump_*</c> line with nothing after it, so
/// an empty line turns the message off; only a missing line keeps SharpMUSH's default.
/// </summary>
public class ReadPennMushConfigDumpTests
{
	private static PennMushConfigImport Import(params string[] lines)
	{
		var path = Path.Join(Path.GetTempPath(), $"sharpmush-cnf-{Guid.NewGuid():N}.cnf");
		File.WriteAllLines(path, lines);
		try
		{
			return ReadPennMushConfig.Import(path);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task AnEmptyDumpLineTurnsItsMessageOff()
	{
		var dump = Import("dump_message", "dump_complete   ", "dump_warning_5min Saving soon.").Options.Dump;
		var defaults = SharpMUSHOptions.Default().Dump;

		await Assert.That(dump.DumpMessage).IsEqualTo("");
		await Assert.That(dump.DumpComplete).IsEqualTo("");
		await Assert.That(dump.DumpWarning5Min).IsEqualTo("Saving soon.");
		await Assert.That(dump.DumpWarning1Min).IsEqualTo(defaults.DumpWarning1Min);
	}

	[Test]
	public async Task TheLastDumpLineWins()
	{
		var dump = Import("dump_message First.", "dump_message Second. # a comment").Options.Dump;

		await Assert.That(dump.DumpMessage).IsEqualTo("Second.");
	}
}
