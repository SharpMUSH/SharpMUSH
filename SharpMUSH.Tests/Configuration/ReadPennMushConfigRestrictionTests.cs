using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// A PennMUSH game's <c>restrict_command</c> lines live in the <c>restrict.cnf</c> its <c>mush.cnf</c>
/// includes (<c>game/mushcnf.dst</c>: <c>include restrict.cnf</c>). PennMUSH's
/// <c>config_file_startup</c> (<c>src/conf.c</c>) follows the include; reading neither the include nor
/// the lines lost every restriction an imported game had.
/// </summary>
public class ReadPennMushConfigRestrictionTests
{
	/// <summary>The restriction lines of PennMUSH's shipped <c>game/restrictcnf.dst</c>.</summary>
	private static readonly string[] ShippedRestrictCnf =
	[
		"# Restrictions on command usage",
		"restrict_command @set noguest",
		"restrict_command ATTRIB_SET noguest",
		"restrict_command @chown noguest",
		"restrict_command @create noguest",
		"restrict_command @power logargs",
		"restrict_command home nofixed \" You can't do that IC!",
		"restrict_command @destroy noplayer \" Use @recycle instead",
		"restrict_command warn_on_missing nobody",
		"",
		"restrict_function lstats noguest"
	];

	private static string GameDirectory()
	{
		var directory = Path.Join(Path.GetTempPath(), $"sharpmush-game-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		return directory;
	}

	private static string Write(string directory, string name, params string[] lines)
	{
		var path = Path.Join(directory, name);
		File.WriteAllLines(path, lines);
		return path;
	}

	[Test]
	public async Task IncludedRestrictCnf_BecomesCommandRestrictions()
	{
		var game = GameDirectory();
		try
		{
			Write(game, "restrict.cnf", ShippedRestrictCnf);
			var mushCnf = Write(game, "mush.cnf", "mud_name Imported", "include restrict.cnf");

			var import = ReadPennMushConfig.Import(mushCnf);
			var commands = import.Options.Restriction.CommandRestrictions;

			await Assert.That(import.Options.Net.MudName).IsEqualTo("Imported");
			await Assert.That(commands.Count).IsEqualTo(8);
			await Assert.That(commands["@set"]).IsEquivalentTo(new[] { "noguest" });
			await Assert.That(commands["ATTRIB_SET"]).IsEquivalentTo(new[] { "noguest" });
			await Assert.That(commands["@power"]).IsEquivalentTo(new[] { "logargs" });
			await Assert.That(commands["home"]).IsEquivalentTo(new[] { "nofixed \" You can't do that IC!" });
			await Assert.That(commands["@destroy"]).IsEquivalentTo(new[] { "noplayer \" Use @recycle instead" });
			await Assert.That(commands["warn_on_missing"]).IsEquivalentTo(new[] { "nobody" });
			await Assert.That(import.Options.Restriction.FunctionRestrictions["lstats"]).IsEquivalentTo(new[] { "noguest" });
			// Applied like restrict_command, so there is nothing to report.
			await Assert.That(import.Skipped).IsEmpty();
		}
		finally
		{
			Directory.Delete(game, true);
		}
	}

	/// <summary>
	/// <c>restrict_command</c> is not only a <c>restrict.cnf</c> line: the shipped <c>mush.cnf</c> itself
	/// restricts <c>ahelp</c> and <c>anews</c> to admin, and an included file may include another.
	/// </summary>
	[Test]
	public async Task RestrictionsInMushCnfAndNestedIncludes_AreAllRead()
	{
		var game = GameDirectory();
		try
		{
			Directory.CreateDirectory(Path.Join(game, "local"));
			Write(game, Path.Join("local", "more.cnf"), "restrict_command @dig builder");
			Write(game, "restrict.cnf", "restrict_command @open noguest", "include local/more.cnf");
			var mushCnf = Write(game, "mush.cnf", "restrict_command ahelp admin", "include restrict.cnf");

			var commands = ReadPennMushConfig.Create(mushCnf).Restriction.CommandRestrictions;

			await Assert.That(commands.Keys).IsEquivalentTo(new[] { "ahelp", "@open", "@dig" });
			await Assert.That(commands["@dig"]).IsEquivalentTo(new[] { "builder" });
		}
		finally
		{
			Directory.Delete(game, true);
		}
	}

	/// <summary>
	/// An uploaded <c>mush.cnf</c> arrives without the <c>restrict.cnf</c> it includes. PennMUSH logs
	/// "Cannot open configuration file" and reads on; the import does the same, and says so.
	/// </summary>
	[Test]
	public async Task MissingInclude_IsReportedAndTheRestIsRead()
	{
		var game = GameDirectory();
		try
		{
			var mushCnf = Write(game, "mush.cnf", "include restrict.cnf", "mud_name StillRead");

			var import = ReadPennMushConfig.Import(mushCnf);

			await Assert.That(import.Options.Net.MudName).IsEqualTo("StillRead");
			await Assert.That(import.Options.Restriction.CommandRestrictions).IsEmpty();
			await Assert.That(import.Skipped.Count).IsEqualTo(1);
			await Assert.That(import.Skipped[0]).StartsWith("include restrict.cnf in ");
			await Assert.That(import.Skipped[0]).Contains("nothing it sets was carried over");
		}
		finally
		{
			Directory.Delete(game, true);
		}
	}

	/// <summary>
	/// Each line that cannot be carried is named with the reason: a restriction with no value (in
	/// PennMUSH's own words), one a later line for the same command replaced, and a
	/// <c>restrict_attribute</c>, which SharpMUSH has nothing for.
	/// </summary>
	[Test]
	public async Task LinesThatCannotBeCarried_AreReported()
	{
		var game = GameDirectory();
		try
		{
			var mushCnf = Write(game, "mush.cnf",
				"restrict_command @wall",
				"restrict_command @pemit noguest",
				"restrict_command @PEMIT wizard",
				"restrict_attribute DESC wizard");

			var import = ReadPennMushConfig.Import(mushCnf);

			await Assert.That(import.Options.Restriction.CommandRestrictions.Keys).IsEquivalentTo(new[] { "@PEMIT" });
			await Assert.That(import.Options.Restriction.CommandRestrictions["@pemit"]).IsEquivalentTo(new[] { "wizard" });
			await Assert.That(import.Skipped).IsEquivalentTo(new[]
			{
				"restrict_command @wall: restrict_command @wall requires a restriction value.",
				"restrict_command @pemit noguest: replaced by the later line restrict_command @PEMIT wizard.",
				"restrict_attribute DESC wizard: SharpMUSH has no equivalent, so it was not carried over."
			});
		}
		finally
		{
			Directory.Delete(game, true);
		}
	}

	/// <summary>
	/// <c>config_file_startup</c> cuts a line at a <c>#</c> not followed by a digit; one followed by a
	/// digit is a dbref, so a lock naming an object survives.
	/// </summary>
	[Test]
	public async Task TrailingCommentIsCut_ButADbrefIsKept()
	{
		var game = GameDirectory();
		try
		{
			var mushCnf = Write(game, "mush.cnf",
				"restrict_command @wall wizard   # only wizards shout",
				"restrict_command @boot =#12");

			var commands = ReadPennMushConfig.Create(mushCnf).Restriction.CommandRestrictions;

			await Assert.That(commands["@wall"]).IsEquivalentTo(new[] { "wizard" });
			await Assert.That(commands["@boot"]).IsEquivalentTo(new[] { "=#12" });
		}
		finally
		{
			Directory.Delete(game, true);
		}
	}

	/// <summary>
	/// A configuration that did not come from the server's disk has its <c>include</c> lines reported,
	/// not followed, so it cannot read a file on the server into the configuration.
	/// </summary>
	[Test]
	public async Task IncludesNotFollowed_AreReportedAndNothingIsRead()
	{
		var game = GameDirectory();
		try
		{
			var serverFile = Write(game, "secret.cnf", "sql_password hunter2", "restrict_command @dig nobody");
			var uploaded = Write(game, "uploaded.cnf", "mud_name Uploaded", $"include {serverFile}");

			var import = ReadPennMushConfig.Import(uploaded, followIncludes: false);

			await Assert.That(import.Options.Net.SqlPassword).IsNotEqualTo("hunter2");
			await Assert.That(import.Options.Restriction.CommandRestrictions).IsEmpty();
			await Assert.That(import.Skipped).IsEquivalentTo(new[]
			{
				$"include {serverFile}: not followed, because an uploaded configuration cannot read files on the server; nothing it sets was carried over."
			});
		}
		finally
		{
			Directory.Delete(game, true);
		}
	}

	/// <summary>A file that includes itself stops ten deep, as PennMUSH's does, rather than never.</summary>
	[Test]
	public async Task SelfInclude_StopsAtTheDepthLimit()
	{
		var game = GameDirectory();
		try
		{
			var mushCnf = Write(game, "mush.cnf", "restrict_command @set noguest", "include mush.cnf");

			var import = ReadPennMushConfig.Import(mushCnf);

			await Assert.That(import.Options.Restriction.CommandRestrictions["@set"]).IsEquivalentTo(new[] { "noguest" });
			await Assert.That(import.Skipped.Any(line => line.Contains("include depth too deep"))).IsTrue();
		}
		finally
		{
			Directory.Delete(game, true);
		}
	}
}
