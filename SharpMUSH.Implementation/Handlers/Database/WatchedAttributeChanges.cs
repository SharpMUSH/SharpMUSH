using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// The one place an attribute write's watched changes become <see cref="AttributeChangedNotification"/>s.
/// Every set, clear and wipe handler answers through here, so no caller of those commands can write a watched
/// attribute without it being announced.
/// </summary>
internal static class WatchedAttributeChanges
{
	/// <summary>Publishes each watched change in <paramref name="outcome"/>, then answers whether the write happened.</summary>
	public static async ValueTask<bool> AnnounceAsync(this IPublisher publisher, AttributeWriteOutcome outcome,
		WatchedAttributes watched, DBRef target, DBRef? enactor, CancellationToken cancellationToken)
	{
		if (outcome is WatchedAttributesChanged changed)
		{
			foreach (var change in changed.Changes)
			{
				// A watch taken off between the read of the set and the write has no pattern left; it runs nothing.
				if (watched.Patterns.GetValueOrDefault(change.Name) is not { } pattern) continue;
				await publisher.Publish(new AttributeChangedNotification(target, change.Name, change.OldValue, change.NewValue, enactor,
					pattern), cancellationToken);
			}
		}

		return outcome.Written();
	}
}
