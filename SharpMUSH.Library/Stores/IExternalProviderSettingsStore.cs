using SharpMUSH.Library.Models;
namespace SharpMUSH.Library;

public interface IExternalProviderSettingsStore
{
	ValueTask<ExternalProviderSettings> ReadAsync(string provider, CancellationToken cancellationToken = default);
	ValueTask WriteAsync(string provider, ExternalProviderSettings settings, CancellationToken cancellationToken = default);
}
