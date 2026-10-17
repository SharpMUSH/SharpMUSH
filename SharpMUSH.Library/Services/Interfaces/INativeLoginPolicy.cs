using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface INativeLoginPolicy
{
	ValueTask<bool> CanConnectAsync(DBRef character, string ip, string hostName, CancellationToken cancellationToken = default);
}
