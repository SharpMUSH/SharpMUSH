using System.Runtime.CompilerServices;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Resolves the refs a store's ref projection returns through the object node cache. A handler for a cached
/// object-shaped query reads refs (a header decode each) and hands out the node cache's instances, so the
/// caching behaviour's own re-resolve of the stored refs is a hit and each object is built once on a miss,
/// by the node cache (#1554). <see cref="GetObjectNodeQuery"/> checks the full id, so an object recycled
/// between the ref read and the resolve is gone rather than the object that took its number.
/// </summary>
internal static class ObjectRefs
{
	/// <summary>Each ref's node, as <typeparamref name="T"/>; a ref whose object is gone, or is not a <typeparamref name="T"/>, is skipped.</summary>
	public static async IAsyncEnumerable<T> ResolveAsync<T>(IMediator mediator, IAsyncEnumerable<DBRef> refs,
		[EnumeratorCancellation] CancellationToken cancellationToken) where T : IObjectShaped<T>
	{
		await foreach (var reference in refs.WithCancellation(cancellationToken))
		{
			if (T.TryFromNode(await mediator.Send(new GetObjectNodeQuery(reference), cancellationToken), out var value))
			{
				yield return value;
			}
		}
	}

	/// <summary>The node <paramref name="found"/> names, or <see cref="None"/> when it names none or its object is gone.</summary>
	public static async ValueTask<AnyOptionalSharpObject> NodeAsync(IMediator mediator, Found<DBRef> found,
		CancellationToken cancellationToken) => found switch
		{
			DBRef reference => await mediator.Send(new GetObjectNodeQuery(reference), cancellationToken),
			NotFound => new None()
		};

	/// <summary>
	/// The container a location or home edge names. No edge may name an exit as a container, so one that
	/// does is a corrupt edge and throws.
	/// </summary>
	public static AnySharpContainer EdgeContainer(AnySharpObject found) => found.AsOptionalContainer switch
	{
		AnySharpContainer container => container,
		None => throw new InvalidOperationException($"#{found.Object().DBRef.Number} is an exit, which cannot be a container")
	};
}
