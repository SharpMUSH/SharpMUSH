using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Documentation;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// The shipped helpfiles against the registered function and command surface.
///
/// <para>Nothing linked the two before: <c>sharpfunc.md</c> and <c>sharpcmd.md</c> carried
/// hand-written signatures for hundreds of names with no way to notice that a function had shipped
/// undocumented, that a topic described a function nobody registered, or that a signature promised
/// an argument the engine rejects. All three were true in quantity — the channel readers, the MOTD
/// readers and <c>zfind()</c> had no help at all, and thirty-odd signatures disagreed with the
/// arity the parser enforces.</para>
/// </summary>
public class HelpRegistryParityTests
{
	private static Helpfiles Index()
	{
		var helpfiles = new Helpfiles(TestPaths.Helpfiles);
		helpfiles.Index();
		return helpfiles;
	}

	/// <summary>
	/// Signatures whose shape cannot be compared to the declared arity, with the reason.
	/// </summary>
	private static readonly Dictionary<string, string> SignatureExceptions = new(StringComparer.OrdinalIgnoreCase)
	{
		["lit"] = "the literal flag hands the whole call one argument, so the declared MaxArgs is never reached"
	};

	[Test]
	public async Task EveryRegisteredFunctionHasAHelpTopic()
	{
		var help = Index();

		var undocumented = RegistryInventory.FunctionNamesWithAliases()
			.Where(name => help.FindEntry($"{name}()") is null && help.FindEntry(name) is null)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(undocumented).IsEmpty();
	}

	[Test]
	public async Task EveryRegisteredCommandHasAHelpTopic()
	{
		var help = Index();

		var undocumented = RegistryInventory.CommandNamesWithAliases()
			.Where(name => help.FindEntry(name) is null)
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(undocumented).IsEmpty();
	}

	[Test]
	public async Task EveryFunctionHelpTopicNamesARegisteredFunction()
	{
		var help = Index();
		var registered = RegistryInventory.FunctionNamesWithAliases();

		var orphans = help.IndexedHelp.Keys
			.Where(topic => topic.EndsWith("()", StringComparison.Ordinal))
			.Where(topic => !registered.Contains(topic[..^2]))
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToList();

		await Assert.That(orphans).IsEmpty();
	}

	/// <summary>
	/// The aliases this repository ships are written down once, in <see cref="AliasOptions.Default"/>.
	/// They were written down three times — there, in <c>Configurable</c>'s field initializer and in
	/// <c>ReadPennMushConfig.Create</c> — and the three had diverged: eight aliases the help documents
	/// in so many words were registered by none of them. The imported copy wins at startup, so a game
	/// that read a PennMUSH <c>mush.cnf</c> got the oldest of the three.
	/// </summary>
	[Test]
	public async Task TheShippedAliasesAreOneTable()
	{
		var imported = ReadPennMushConfig.Create(EmptyPennMushConfig()).Alias;

		await Assert.That(Flatten(Configurable.DefaultFunctionAliases))
			.IsEquivalentTo(Flatten(AliasOptions.Default.FunctionAliases));
		await Assert.That(Flatten(Configurable.DefaultCommandAliases))
			.IsEquivalentTo(Flatten(AliasOptions.Default.CommandAliases));
		await Assert.That(Flatten(imported.FunctionAliases))
			.IsEquivalentTo(Flatten(AliasOptions.Default.FunctionAliases));
		await Assert.That(Flatten(imported.CommandAliases))
			.IsEquivalentTo(Flatten(AliasOptions.Default.CommandAliases));
	}

	/// <summary>A config file that sets nothing, so every option comes back at its default.</summary>
	private static string EmptyPennMushConfig()
	{
		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-alias-parity-{Guid.NewGuid():N}.cnf");
		File.WriteAllText(path, string.Empty);
		return path;
	}

	private static List<string> Flatten(Dictionary<string, string[]> aliases) =>
		aliases.SelectMany(pair => pair.Value.Select(alias => $"{pair.Key.ToLowerInvariant()}={alias.ToLowerInvariant()}"))
			.Order(StringComparer.Ordinal)
			.ToList();

	/// <summary>
	/// The signature line and the attribute have to agree about how many arguments a call may carry.
	/// They disagreed for 38 functions: the time family's <c>&lt;precision&gt;</c> argument and the
	/// vector family's <c>&lt;osep&gt;</c> were undocumented, <c>pcreate()</c> and
	/// <c>textentries()</c> promised an argument the engine rejects, and four signatures had
	/// unbalanced brackets and so described no arity at all.
	/// </summary>
	[Test]
	public async Task EveryFunctionSignatureMatchesItsDeclaredArity()
	{
		var signatures = HelpSignature.ReadAll(TestPaths.Helpfiles);
		var offenders = new List<string>();

		foreach (var entry in RegistryInventory.Functions.Where(f => !SignatureExceptions.ContainsKey(f.Name)))
		{
			if (!signatures.TryGetValue(entry.Name, out var lines)) continue;

			var parsed = lines
				.Select(line => (line, Arity: HelpSignature.Parse(entry.Name, line.Signature)))
				.ToList();

			foreach (var (line, arity) in parsed.Where(p => p.Arity is null))
			{
				offenders.Add($"{entry.Name}: unparseable signature `{line.Signature}` ({line.File}:{line.Line})");
			}

			var arities = parsed.Where(p => p.Arity is not null).Select(p => p.Arity!.Value).ToList();
			if (arities.Count == 0) continue;

			// A topic may show several forms of the same call; the loosest one is the contract.
			var required = arities.Min(a => a.Required);
			var maximum = arities.Max(a => a.Maximum);
			var variadic = arities.Any(a => a.Variadic);
			var where = $"({lines[0].File}:{lines[0].Line})";

			if (required != entry.Attribute.MinArgs)
			{
				offenders.Add(
					$"{entry.Name}: help requires {required}, MinArgs is {entry.Attribute.MinArgs} {where}");
			}

			if (variadic ? entry.Attribute.MaxArgs <= maximum : entry.Attribute.MaxArgs != maximum)
			{
				offenders.Add(
					$"{entry.Name}: help allows {(variadic ? "any number" : maximum.ToString())}, "
					+ $"MaxArgs is {entry.Attribute.MaxArgs} {where}");
			}
		}

		await Assert.That(offenders).IsEmpty();
	}

	/// <summary>Every name in the exception table must still be a registered function.</summary>
	[Test]
	public async Task NoSignatureExceptionNamesAFunctionThatIsGone()
	{
		var registered = RegistryInventory.Functions.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

		await Assert.That(SignatureExceptions.Keys.Where(name => !registered.Contains(name))).IsEmpty();
	}
}
