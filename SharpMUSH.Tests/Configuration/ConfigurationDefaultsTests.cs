using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using System.Collections;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// One shipped default per option, and this is what proves it.
/// </summary>
/// <remarks>
/// A default used to be written down in four places — <c>OptionsService.Default()</c>,
/// <c>ReadPennMushConfig.Create</c>'s fallbacks, the option records' own constructor-parameter
/// defaults and the test harness — and they had diverged on fourteen options, so the same game
/// behaved differently depending on how it was first created: <c>max_depth</c> was 50 in a new world
/// and 10 in one imported from a PennMUSH <c>mush.cnf</c>. Every mismatch is reported in one failure,
/// because the list is the point.
/// </remarks>
public class ConfigurationDefaultsTests
{
	/// <summary>
	/// Options a game imported from a <c>mush.cnf</c> is deliberately given a different value than a
	/// game created from scratch, and why. An entry that no longer differs fails too — a reason kept
	/// for a difference that has gone is a claim about the code that is no longer true.
	/// </summary>
	private static readonly Dictionary<string, string> ImportDifferences = new()
	{
		["BannedNames"] =
			"A new game is seeded with example banned names so the admin page shows what they look " +
			"like; an imported game brings its own names.cnf.",
		["Rules"] =
			"A new game is seeded with example sitelock rules; seeding an imported game with them " +
			"would lock sites nobody asked to lock."
	};

	/// <summary>
	/// Keys the shipped <c>mushcnf.dst</c> sets that SharpMUSH has no option for, and what they
	/// belonged to in PennMUSH. They are read and ignored, which is the intended behaviour — but the
	/// list has to be written down, because the same silence hides a misspelt option name. It hid two:
	/// <c>chkpt_log</c> and <c>ssl_require_clientcert</c>, neither of which PennMUSH ever wrote
	/// (<c>conf.c:251</c>, <c>conf.c:331</c>), so both lines were dropped from every imported game.
	/// </summary>
	private static readonly Dictionary<string, string> PennOnlyKeys = new()
	{
		["input_database"] = "Penn's flatfile store; SharpMUSH has no flatfile.",
		["output_database"] = "Penn's flatfile store.",
		["crash_database"] = "Penn's panic dump.",
		["mail_database"] = "Penn's separate mail file; SharpMUSH stores mail in the world.",
		["chat_database"] = "Penn's separate chat file.",
		["compress_program"] = "Penn compresses its dumps with an external program.",
		["uncompress_program"] = "Penn compresses its dumps with an external program.",
		["compress_suffix"] = "Penn compresses its dumps with an external program.",
		["use_chunk_system"] = "Penn's attribute chunk store.",
		["chunk_swap_file"] = "Penn's attribute chunk store.",
		["chunk_cache_memory"] = "Penn's attribute chunk store.",
		["chunk_swap_initial_size"] = "Penn's attribute chunk store.",
		["attr_compression"] = "Penn's attribute chunk store.",
		["dbck_interval"] = "Penn's database consistency pass.",
		["dump_interval"] = "Penn's flatfile dump timer.",
		["forking_dump"] = "Penn forks to write its flatfile.",
		["help_command"] = "Penn registers help files as commands; SharpMUSH indexes its own helpfiles.",
		["ahelp_command"] = "Penn registers help files as commands.",
		["restrict_command"] = "Read from restrict.cnf instead, as Restriction.CommandRestrictions.",
		["help_db"] = "Penn's help index file.",
		["sendmail_prog"] = "Penn mails @mail out through sendmail.",
		["connlog_db"] = "Penn's connection log database file.",
		["log_wipe_passwd"] = "Penn's @logwipe password; SharpMUSH owns no log sink to wipe.",
		["log_max_size"] = "Penn rotates its own log files.",
		["log_size_policy"] = "Penn rotates its own log files.",
		["mssp"] = "Free-form MSSP fields; SharpMUSH answers MSSP from the game itself.",
		["include"] = "A directive, not an option: alias.cnf and restrict.cnf are read separately."
	};

	private static string ShippedConfig =>
		Path.Join(TestPaths.RepositoryRoot, "SharpMUSH.Configuration", "mushcnf.dst");

	/// <summary>
	/// The configuration a fresh world is created with and the configuration a <c>mush.cnf</c> that
	/// says nothing is read as have to be the same configuration.
	/// </summary>
	[Test]
	public async Task EveryDefaultIsWrittenDownInOnePlace()
	{
		var created = OptionsService.Default();
		var imported = ReadPennMushConfig.Create(EmptyConfigFile());

		var mismatches = ConfigMetadata.PropertyNames
			.Where(name => !ImportDifferences.ContainsKey(name))
			.Select(name => (Name: name, Created: ConfigAccessor.GetValue(created, name),
				Imported: ConfigAccessor.GetValue(imported, name)))
			.Where(x => !Same(x.Created, x.Imported))
			.Select(x => $"{x.Name}: a new world gets {Show(x.Created)}, an import gets {Show(x.Imported)}")
			.ToArray();

		await Assert.That(string.Join("\n", mismatches)).IsEmpty();
	}

	/// <summary>An entry in <see cref="ImportDifferences"/> that no longer differs is stale.</summary>
	[Test]
	public async Task EveryRecordedImportDifferenceStillDiffers()
	{
		var created = OptionsService.Default();
		var imported = ReadPennMushConfig.Create(EmptyConfigFile());

		var stale = ImportDifferences.Keys
			.Where(name => Same(ConfigAccessor.GetValue(created, name), ConfigAccessor.GetValue(imported, name)))
			.ToArray();

		await Assert.That(string.Join(", ", stale)).IsEmpty();
	}

	/// <summary>
	/// An option record may still declare a constructor-parameter default, because a parameter default
	/// is a compile-time constant and some of them are wanted as one elsewhere. When it does, it has to
	/// say what <see cref="SharpMUSHOptions.Default"/> says: an argument omitted at the one call site
	/// that matters silently takes the record's answer instead, which is how <c>Net.Mxp</c> came to be
	/// <c>false</c> in a new world and <c>true</c> in an imported one.
	/// </summary>
	[Test]
	public async Task DeclaredParameterDefaultsSayWhatTheDefaultSays()
	{
		var created = OptionsService.Default();

		var mismatches = ConfigMetadata.PropertyNames
			.Select(name => (Name: name, Declared: AsPropertyType(name, ConfigAccessor.GetDeclaredDefault(name)),
				Created: ConfigAccessor.GetValue(created, name)))
			.Where(x => x.Declared is not null && !Same(x.Declared, x.Created))
			.Select(x => $"{x.Name}: the record declares {Show(x.Declared)}, the default is {Show(x.Created)}")
			.ToArray();

		await Assert.That(string.Join("\n", mismatches)).IsEmpty();
	}

	/// <summary>
	/// Every key the shipped configuration sets is either an option SharpMUSH reads or a PennMUSH key
	/// recorded as ignored. A key that is neither is a line the operator wrote and the server dropped.
	/// </summary>
	[Test]
	public async Task TheShippedConfigSetsNothingSharpMUSHSilentlyDrops()
	{
		var unread = ShippedKeys()
			.Where(key => !ConfigMetadata.AttributeToPropertyName.ContainsKey(key))
			.Where(key => !PennOnlyKeys.ContainsKey(key))
			.ToArray();

		await Assert.That(string.Join(", ", unread)).IsEmpty();
	}

	/// <summary>
	/// A <see cref="PennOnlyKeys"/> entry that has become an option, or that the shipped configuration
	/// no longer sets, is a stale claim about what the server ignores.
	/// </summary>
	[Test]
	public async Task EveryRecordedPennOnlyKeyIsStillUnreadAndStillShipped()
	{
		var shipped = ShippedKeys().ToHashSet(StringComparer.Ordinal);

		var stale = PennOnlyKeys.Keys
			.Where(key => ConfigMetadata.AttributeToPropertyName.ContainsKey(key) || !shipped.Contains(key))
			.ToArray();

		await Assert.That(string.Join(", ", stale)).IsEmpty();
	}

	/// <summary>
	/// The generator emits an enum parameter default as its underlying integer, so an enum-typed option
	/// has to be read back as the enum before the two can be compared at all.
	/// </summary>
	private static object? AsPropertyType(string name, object? declared) =>
		declared is not null && ConfigAccessor.GetPropertyType(name) is { IsEnum: true } type
			? Enum.ToObject(type, declared)
			: declared;

	private static IEnumerable<string> ShippedKeys() =>
		File.ReadLines(ShippedConfig)
			.Select(line => line.Trim())
			.Where(line => line.Length > 0 && !line.StartsWith('#'))
			.Select(line => line.Split(' ', '\t')[0])
			.Distinct(StringComparer.Ordinal);

	/// <summary>
	/// Structural equality: several options are arrays or dictionaries, and two equal defaults built
	/// twice are never the same instance.
	/// </summary>
	private static bool Same(object? left, object? right) => (left, right) switch
	{
		(null, null) => true,
		(null, _) or (_, null) => false,
		(IDictionary a, IDictionary b) => a.Count == b.Count && a.Keys.Cast<object>()
			.All(key => b.Contains(key) && Same(a[key], b[key])),
		(string, _) or (_, string) => left.Equals(right),
		(IEnumerable a, IEnumerable b) => SameSequence(a, b),
		_ => left.Equals(right)
	};

	private static bool SameSequence(IEnumerable left, IEnumerable right)
	{
		var a = left.Cast<object?>().ToArray();
		var b = right.Cast<object?>().ToArray();

		return a.Length == b.Length && a.Zip(b).All(pair => Same(pair.First, pair.Second));
	}

	private static string Show(object? value) => value switch
	{
		null => "nothing",
		string text => $"\"{text}\"",
		IDictionary dictionary => $"{dictionary.Count} entries",
		IEnumerable items => $"[{string.Join(" ", items.Cast<object?>())}]",
		_ => value.ToString() ?? "nothing"
	};

	private static string EmptyConfigFile()
	{
		var path = Path.Join(Path.GetTempPath(), $"sharpmush-defaults-{Guid.NewGuid():N}.cnf");
		File.WriteAllText(path, string.Empty);
		return path;
	}
}
