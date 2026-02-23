using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetAttributeFlagCommand(DBRef DBRef, SharpAttribute Target, SharpAttributeFlag Flag) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys =>
	[
		$"attribute:{DBRef}:{string.Join("`", Target.LongName)})",
		$"commands:{DBRef}"
	];
	public string[] CacheTags => [];
}
