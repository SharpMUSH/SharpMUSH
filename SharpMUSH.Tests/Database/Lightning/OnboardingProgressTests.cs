using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class OnboardingProgressTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;

	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private async Task ReopenAsync()
	{
		await _db.DisposeAsync();
		_db = Create(_path);
		await _db.Migrate();
	}

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-onboarding-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		Directory.Delete(_path, recursive: true);
	}

	[Test]
	public async Task NewAccountHasNoProgress()
	{
		var progress = await _db.GetOnboardingProgressAsync("account-a", GuideJourney.Player);
		await Assert.That(progress.Revision).IsEqualTo(0);
		await Assert.That(progress.Active).IsFalse();
		await Assert.That(progress.CompletedStepIds).IsEmpty();
	}

	[Test]
	public async Task ReopenPreservesProgressAndGameIdentity()
	{
		var game = await _db.GetOnboardingGameIdAsync();
		var input = new OnboardingProgress { Path = GuidePath.Mush, Dismissed = true, CompletedStepIds = ["place"] };
		var saved = (await _db.SaveOnboardingProgressAsync("account-a", GuideJourney.Player, input, 0)).Expect<OnboardingProgress>();
		await Assert.That(saved.Revision).IsEqualTo(1);
		await ReopenAsync();
		var loaded = await _db.GetOnboardingProgressAsync("account-a", GuideJourney.Player);
		await Assert.That(loaded.Dismissed).IsTrue();
		await Assert.That(loaded.Path).IsEqualTo(GuidePath.Mush);
		await Assert.That(loaded.CompletedStepIds).IsEquivalentTo(new[] { "place" });
		await Assert.That(await _db.GetOnboardingGameIdAsync()).IsEqualTo(game);
	}

	[Test]
	public async Task AccountsAndJourneysAreIndependent()
	{
		await _db.SaveOnboardingProgressAsync("account-a", GuideJourney.Player, new() { Dismissed = true }, 0);
		await Assert.That((await _db.GetOnboardingProgressAsync("account-b", GuideJourney.Player)).Dismissed).IsFalse();
		await Assert.That((await _db.GetOnboardingProgressAsync("account-a", GuideJourney.Administrator)).Dismissed).IsFalse();
	}

	[Test]
	public async Task StaleRevisionCannotEraseDismissal()
	{
		await _db.SaveOnboardingProgressAsync("account-a", GuideJourney.Player, new() { Dismissed = true }, 0);
		var result = await _db.SaveOnboardingProgressAsync("account-a", GuideJourney.Player, new() { Active = true }, 0);
		await Assert.That(result is OnboardingConflict).IsTrue();
		var current = await _db.GetOnboardingProgressAsync("account-a", GuideJourney.Player);
		await Assert.That(current.Dismissed).IsTrue();
		await Assert.That(current.Revision).IsEqualTo(1);
	}

	[Test]
	public async Task InvalidAndFutureProgressCannotReplaceSavedState()
	{
		var bad = new OnboardingProgress { CompletedStepIds = ["place"], SkippedStepIds = ["place"] };
		await Assert.That(await _db.SaveOnboardingProgressAsync("account-a", GuideJourney.Player, bad, 0) is Error<string>).IsTrue();
		await Assert.That(await _db.SaveOnboardingProgressAsync("account-a", GuideJourney.Player,
			new() { SchemaVersion = 999 }, 0) is Error<string>).IsTrue();
		await Assert.That((await _db.GetOnboardingProgressAsync("account-a", GuideJourney.Player)).Revision).IsEqualTo(0);
	}

	[Test]
	public async Task ConcurrentTabsHaveOnlyOneWinner()
	{
		var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
			_db.SaveOnboardingProgressAsync("account-a", GuideJourney.Player, new() { Active = true }, 0).AsTask()));
		await Assert.That(results.Count(result => result is OnboardingProgress)).IsEqualTo(1);
		await Assert.That(results.Count(result => result is OnboardingConflict)).IsEqualTo(7);
	}
}
