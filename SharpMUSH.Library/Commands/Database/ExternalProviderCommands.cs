using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
namespace SharpMUSH.Library.Commands.Database;

public record SaveExternalProviderSettingsCommand(string Provider, ExternalProviderSettings Settings) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [];
}
