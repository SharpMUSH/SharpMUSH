using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The retention policy's semantics (#1464), apart from any store: the default keeps everything, both
/// bounds must agree before a version goes, a pinned version never goes, and an unreadable setting keeps
/// more rather than less.
/// </summary>
public class HistoryRetentionPolicyTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

	[Test]
	public async Task TheDefaultRuleKeepsEverything()
	{
		var rule = new HistoryRetentionRule();

		await Assert.That(rule.KeepsEverything).IsTrue();
		await Assert.That(rule.IsPurgeable(500, Now.AddYears(-20), pinned: false, Now)).IsFalse();
		await Assert.That(new HistoryRetentionOptions().RuleFor("wiki")).IsEqualTo(HistoryRetentionRule.KeepEverything);
	}

	[Test]
	[Arguments(0, false)]
	[Arguments(2, false)]
	[Arguments(3, true)]
	[Arguments(10, true)]
	public async Task KeepNewestProtectsTheNewestRanks(int rank, bool purgeable)
		=> await Assert.That(new HistoryRetentionRule { KeepNewest = 3 }.IsPurgeable(rank, Now, pinned: false, Now))
			.IsEqualTo(purgeable);

	[Test]
	public async Task BothBoundsMustAgree()
	{
		var rule = new HistoryRetentionRule { KeepNewest = 2, MaxAge = TimeSpan.FromDays(30) };

		await Assert.That(rule.IsPurgeable(5, Now.AddDays(-31), false, Now)).IsTrue();
		await Assert.That(rule.IsPurgeable(5, Now.AddDays(-29), false, Now)).IsFalse().Because("too young");
		await Assert.That(rule.IsPurgeable(1, Now.AddDays(-400), false, Now)).IsFalse().Because("among the newest two");
	}

	[Test]
	public async Task APinnedVersionIsNeverPurgeable()
		=> await Assert.That(new HistoryRetentionRule { KeepNewest = 1, MaxAge = TimeSpan.FromSeconds(1) }
			.IsPurgeable(99, Now.AddYears(-5), pinned: true, Now)).IsFalse();

	[Test]
	public async Task SettingsAreReadPerKind()
	{
		var environment = new Dictionary<string, string>
		{
			["SHARPMUSH_HISTORY_WIKI_KEEP"] = "20",
			["SHARPMUSH_HISTORY_SCENE_EDITS_MAX_AGE"] = "90d",
			["SHARPMUSH_HISTORY_SCENE_DELETED_MAX_AGE"] = "730d",
			["SHARPMUSH_HISTORY_INTERVAL"] = "1d",
			["SHARPMUSH_HISTORY_BATCH"] = "50",
			["SHARPMUSH_HISTORY_ARCHIVE_PATH"] = "/var/archive"
		};

		var options = HistoryRetentionOptions.Resolve(["wiki", "scene.edits", "scene.deleted"],
			name => environment.GetValueOrDefault(name), NullLogger.Instance);

		await Assert.That(options.RuleFor("wiki")).IsEqualTo(new HistoryRetentionRule { KeepNewest = 20 });
		await Assert.That(options.RuleFor("scene.edits")).IsEqualTo(new HistoryRetentionRule { MaxAge = TimeSpan.FromDays(90) });
		await Assert.That(options.RuleFor("scene.deleted").MaxAge).IsEqualTo(TimeSpan.FromDays(730));
		await Assert.That(options.Interval).IsEqualTo(TimeSpan.FromDays(1));
		await Assert.That(options.BatchSize).IsEqualTo(50);
		await Assert.That(options.ArchivePath).IsEqualTo("/var/archive");
	}

	/// <summary>A typo can only fail safe: the bound it was meant to set stays off, so more is kept.</summary>
	[Test]
	[Arguments("SHARPMUSH_HISTORY_WIKI_KEEP", "twenty")]
	[Arguments("SHARPMUSH_HISTORY_WIKI_KEEP", "-3")]
	[Arguments("SHARPMUSH_HISTORY_WIKI_MAX_AGE", "90 days")]
	[Arguments("SHARPMUSH_HISTORY_WIKI_MAX_AGE", "0")]
	public async Task AnUnreadableSettingKeepsEverything(string name, string value)
	{
		var options = HistoryRetentionOptions.Resolve(["wiki"], n => n == name ? value : null, NullLogger.Instance);

		await Assert.That(options.RuleFor("wiki").KeepsEverything).IsTrue();
	}

	[Test]
	[Arguments("wiki", "SHARPMUSH_HISTORY_WIKI")]
	[Arguments("scene.edits", "SHARPMUSH_HISTORY_SCENE_EDITS")]
	[Arguments("scene.deleted", "SHARPMUSH_HISTORY_SCENE_DELETED")]
	public async Task AKindNamesItsSettings(string kind, string name)
		=> await Assert.That(HistoryRetentionOptions.SettingNameFor(kind)).IsEqualTo(name);

	/// <summary>Retention ages run to years, past the backup interval's one-year ceiling.</summary>
	[Test]
	public async Task AnAgeMayBeLongerThanAYear()
	{
		await Assert.That(DurationSetting.TryParse("1095d", 100L * 365 * 86400, out var age)).IsTrue();
		await Assert.That(age).IsEqualTo(TimeSpan.FromDays(1095));
		await Assert.That(WorldBackupOptions.TryParseInterval("1095d", out _)).IsFalse();
	}
	/// <summary>A provider's own failure type, as Lightning's commit failure is: it derives from nothing more
	/// specific than <see cref="Exception"/>.</summary>
	private sealed class ProviderFailure(string message) : Exception(message);

	/// <summary>
	/// Whatever a store throws, the kind reports a failed outcome and the next kind still runs; only
	/// cancellation escapes the pass.
	/// </summary>
	[Test]
	public async Task AnyStoreFailureIsReportedAndTheNextKindStillRuns()
	{
		var failing = Substitute.For<IHistoryStore>();
		failing.Kind.Returns("first");
		failing.FindPurgeableAsync(default!, default, default!, default, default)
			.ReturnsForAnyArgs<ValueTask<HistoryPurgeBatch>>(_ => throw new ProviderFailure("commit failed"));
		var healthy = Substitute.For<IHistoryStore>();
		healthy.Kind.Returns("second");
		healthy.FindPurgeableAsync(default!, default, default!, default, default)
			.ReturnsForAnyArgs(ValueTask.FromResult(new HistoryPurgeBatch([], [])));
		var keepOne = new HistoryRetentionRule { KeepNewest = 1 };
		var retention = new HistoryRetentionService([failing, healthy],
			new HistoryRetentionOptions
			{
				Rules = new Dictionary<string, HistoryRetentionRule> { ["first"] = keepOne, ["second"] = keepOne }
			},
			NullLogger<HistoryRetentionService>.Instance, TimeProvider.System);

		var outcomes = await retention.PurgeAsync();

		await Assert.That(outcomes[0].Expect<HistoryPurgeFailed>().Reason).IsEqualTo("commit failed");
		await Assert.That(outcomes[1].Expect<HistoryPurged>().Records).IsEqualTo(0);
	}
}
