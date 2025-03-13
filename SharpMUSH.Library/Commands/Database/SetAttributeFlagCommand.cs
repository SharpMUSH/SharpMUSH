﻿using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetAttributeFlagCommand(DBRef DBRef, SharpAttribute Target, SharpAttributeFlag Flag) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [DBRef.ToString()];
	public string[] CacheTags => [Definitions.CacheTags.ObjectAttributes];
}
