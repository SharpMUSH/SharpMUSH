using System.Text;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	public ValueTask<OnboardingProgress> GetOnboardingProgressAsync(string accountId, GuideJourney journey,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var key = OnboardingKey(accountId, journey);
		return ValueTask.FromResult(Store.Read(tx => tx.TryGet(Tables.OnboardingProgress, key, out var bytes)
			? Codec.Deserialize<OnboardingProgress>(bytes) : new OnboardingProgress()));
	}

	public async ValueTask<OnboardingSaveResult> SaveOnboardingProgressAsync(string accountId, GuideJourney journey,
		OnboardingProgress progress, long expectedRevision, CancellationToken cancellationToken = default)
	{
		if (!GuideStepIds.IsValid(journey, progress, expectedRevision))
			return new Error<string>("Invalid onboarding progress or unsupported version.");
		var key = OnboardingKey(accountId, journey);
		// Freeze caller-owned arrays before enqueueing: a queued write must validate the same data it persists.
		var snapshot = progress with { CompletedStepIds = [.. progress.CompletedStepIds], SkippedStepIds = [.. progress.SkippedStepIds] };
		if (!GuideStepIds.IsValid(journey, snapshot, expectedRevision))
			return new Error<string>("Invalid onboarding progress or unsupported version.");
		return await Store.WriteAsync<OnboardingSaveResult>(tx =>
		{
			var current = tx.TryGet(Tables.OnboardingProgress, key, out var bytes)
				? Codec.Deserialize<OnboardingProgress>(bytes) : new OnboardingProgress();
			if (current.SchemaVersion != 1 || current.GuideVersion != 1)
				return new Error<string>("Saved onboarding progress uses an unsupported version.");
			if (current.Revision != expectedRevision)
				return new OnboardingConflict(current.Revision);
			var saved = snapshot with { Revision = expectedRevision + 1 };
			tx.Put(Tables.OnboardingProgress, key, Codec.Serialize(saved));
			return saved;
		}, cancellationToken);
	}

	public ValueTask<string> GetOnboardingGameIdAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("onboarding:game-id"), out var value)
			? Encoding.UTF8.GetString(value) : throw new InvalidOperationException("Onboarding migration has not run.")));
	}

	private static byte[] OnboardingKey(string accountId, GuideJourney journey)
	{
		if (string.IsNullOrWhiteSpace(accountId) || Encoding.UTF8.GetByteCount(accountId) > 256 || !Enum.IsDefined(journey))
			throw new ArgumentException("An account and a valid journey are required.");
		return Keys.Str(Convert.ToBase64String(Encoding.UTF8.GetBytes(accountId)) + ":" + (int)journey);
	}
}
