using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectLocationCommand(AnySharpContent Target, AnySharpContainer Container) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.Object().DBRef), Definitions.CacheKeys.Object(Container.Object().DBRef)];
	// The raw setter has no old-container argument, so its rare writes invalidate all contents.
	public string[] CacheTags =>
	[
		Definitions.CacheTags.ObjectContents,
		Definitions.CacheKeys.LocationTag(Target.Object().DBRef.Number),
		Definitions.CacheKeys.LocationTag(Target.Object().Id!)
	];
}
