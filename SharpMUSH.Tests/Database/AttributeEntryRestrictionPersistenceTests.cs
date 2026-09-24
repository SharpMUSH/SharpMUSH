using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// <c>@attribute/limit</c> and <c>@attribute/enum</c> are attribute-table state, and the table
/// outlives the process: an enum whose choices came back without their delimiter, or a limit that
/// came back null, would silently stop refusing anything after a restart. The other half of the
/// same story is the regexp bound — a limit is player-supplied, so a catastrophic one must refuse
/// the value rather than hang the write.
/// </summary>
public class AttributeEntryRestrictionPersistenceTests
{
	/// <summary>
	/// The entry is written, the environment is closed, and a second <see cref="LightningDatabase"/>
	/// is opened on the same directory — a real reload, not the same handle read twice.
	/// <see cref="DefinitionCreationContract.Open"/> deletes its world on dispose, so this opens its
	/// own.
	/// </summary>
	[Test]
	public async Task LimitAndEnumSurviveAReload()
	{
		var path = Path.Join(Path.GetTempPath(), "attr-entry-" + Guid.NewGuid().ToString("N"));
		try
		{
			await using (var db = OpenAt(path))
			{
				await db.Migrate();
				await db.CreateOrUpdateAttributeEntryAsync("COLOUR", ["no_command"], limit: @"^\d+$");
				await db.CreateOrUpdateAttributeEntryAsync("MOOD", ["no_command"],
					enumValues: ["Quietly Pleased", "Put Out"], enumDelimiter: '|');
			}

			await using var reopened = OpenAt(path);
			await reopened.Migrate();

			var limited = await reopened.GetSharpAttributeEntry("COLOUR");
			await Assert.That(limited).IsNotNull();
			await Assert.That(limited!.Limit).IsEqualTo(@"^\d+$");

			var enumerated = await reopened.GetSharpAttributeEntry("MOOD");
			await Assert.That(enumerated).IsNotNull();
			await Assert.That(enumerated!.Enum).IsEquivalentTo(new[] { "Quietly Pleased", "Put Out" });
			await Assert.That(enumerated.EnumDelimiter).IsEqualTo('|');

			// The delimiter is what makes a multi-word choice reachable; losing it across the reload
			// would leave a choice nothing can ever name.
			await Assert.That(AttributeValueRestriction.Check(enumerated, "Quietly Pleased") is "Quietly Pleased").IsTrue();
			await Assert.That(AttributeValueRestriction.Check(limited, "abc") is Error<string>).IsTrue();
			await Assert.That(AttributeValueRestriction.Check(limited, "42") is "42").IsTrue();
		}
		finally
		{
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

	private static LightningDatabase OpenAt(string path)
		=> new(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 },
			Substitute.For<IPasswordService>(), relations: null);

	/// <summary>
	/// A limit is a player-supplied regexp, so it can be a catastrophic one.
	/// <see cref="SoftcodeRegex.IsMatch"/> turns the <see cref="RegexMatchTimeoutException"/> that
	/// <see cref="SoftcodeRegex.MatchTimeout"/> raises into "no match", which makes a timed-out limit
	/// refuse the value rather than hang the set. Run outside an <see cref="ExecutionBudget"/>, where
	/// the full 100 ms bound applies.
	/// </summary>
	[Test]
	public async Task ACatastrophicLimitRefusesTheValueRatherThanHanging()
	{
		var entry = new SharpAttributeEntry
		{
			Name = "BACKTRACK",
			DefaultFlags = [],
			Limit = "^(a+)+$"
		};
		var value = new string('a', 40) + "b";

		var started = System.Diagnostics.Stopwatch.StartNew();
		var result = AttributeValueRestriction.Check(entry, value);
		started.Stop();

		await Assert.That(result is Error<string>).IsTrue();
		await Assert.That(started.Elapsed).IsLessThan(TimeSpan.FromSeconds(5))
			.Because($"the match is bounded at {SoftcodeRegex.MatchTimeout.TotalMilliseconds}ms");
	}

	/// <summary>The same limit under a near-exhausted budget still refuses, and still returns.</summary>
	[Test]
	public async Task ACatastrophicLimitRefusesUnderAnExecutionBudgetToo()
	{
		var entry = new SharpAttributeEntry
		{
			Name = "BACKTRACK_BUDGETED",
			DefaultFlags = [],
			Limit = "^(a+)+$"
		};
		var value = new string('a', 40) + "b";

		using var budget = ExecutionBudget.FromMilliseconds(50);
		using var scope = budget.Enter();
		var result = AttributeValueRestriction.Check(entry, value);

		await Assert.That(result is Error<string>).IsTrue();
	}
}
