using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectParentCommand(AnySharpObject Target, AnySharpObject Parent) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [$"object:{Target.Object().DBRef}", $"object:{Parent.Object().DBRef}"];
	public string[] CacheTags => [];
}

public record UnsetObjectParentCommand(AnySharpObject Target) : ICommand, ICacheInvalidating
{
	// TODO: Consider if .Result is at all safe here, or a better way of doing this.
	// Also, what about the execution order? Does the cache invalidate in time?
	public string[] CacheKeys => [$"object:{Target.Object().DBRef}", $"object:{Target.Object().Parent.WithCancellation(CancellationToken.None).Result.Object()!.DBRef}"];
	public string[] CacheTags => [];
}