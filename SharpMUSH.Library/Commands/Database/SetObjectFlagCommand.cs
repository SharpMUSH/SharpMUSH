using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectFlagCommand(AnySharpObject Target, SharpObjectFlag Flag) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys =>
	[
		Definitions.CacheKeys.Object(Target.Object().DBRef),
		Definitions.CacheKeys.ObjectFlags(Target.Object().Id!)
	];
	public string[] CacheTags => [];
}
