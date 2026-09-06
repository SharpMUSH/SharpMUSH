using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectPowerCommand(AnySharpObject Target, SharpPower Flag) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys =>
	[
		Definitions.CacheKeys.Object(Target.Object().DBRef),
		Definitions.CacheKeys.ObjectPowers(Target.Object().Id!)
	];
	public string[] CacheTags => [];
}
