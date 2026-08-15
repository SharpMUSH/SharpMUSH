using SharpMUSH.Database.SurrealDB;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Pins the message matching behind SurrealDB conflict handling. The predicate decides two things —
/// whether a channel create retries up to eight times, and whether a failed response is reported as a
/// warning or an error — so both a miss and a false positive are costly.
/// </summary>
public class SurrealRetryableConflictTests
{
	/// <summary>The real message, verbatim from a CI run.</summary>
	private const string ConflictMessage =
		"Failed to commit transaction due to a read or write conflict. This transaction can be retried";

	[Test]
	public async Task TheRealConflictMessage_IsRetryable()
	{
		await Assert.That(SurrealDatabase.IsRetryableConflict(ConflictMessage)).IsTrue();
	}

	[Test]
	public async Task TheConflictMessage_IsRetryable_WhateverTheCasing()
	{
		await Assert.That(SurrealDatabase.IsRetryableConflict(ConflictMessage.ToUpperInvariant())).IsTrue();
	}

	[Test]
	[Arguments("The query was not executed due to a failed transaction")]
	[Arguments("Database record `channel:Foo` already exists")]
	[Arguments("There was a problem with the database: Serialization error")]
	public async Task OrdinaryFailures_AreNotRetryable(string message)
	{
		await Assert.That(SurrealDatabase.IsRetryableConflict(message)).IsFalse();
	}

	[Test]
	public async Task AnUnrelatedErrorMentioningRetry_IsNotRetryable()
	{
		// Matching a bare "can be retried" would hand eight retries, and a demotion from error to
		// warning, to something that is not a conflict at all.
		await Assert.That(SurrealDatabase.IsRetryableConflict(
			"Upload failed and can be retried by the client")).IsFalse();
	}
}
