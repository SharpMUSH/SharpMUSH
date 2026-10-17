using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using SharpMUSH.Library.Models;
namespace SharpMUSH.Server.Authentication.External;

public interface IExternalAuthenticationCompletion
{
	Task FailAsync(HttpContext context, AuthenticationProperties? properties, CancellationToken cancellationToken);

	Task CompleteAsync(HttpContext context, AuthenticationProperties properties, ExternalIdentityKey key,
	 string? label, CancellationToken cancellationToken);
}
