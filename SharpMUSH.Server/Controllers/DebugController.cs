using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Development-only diagnostic endpoints. Every action here must be gated on
/// <see cref="IHostEnvironment.IsDevelopment"/> (mirroring <c>AuthController.GetDebugOtt</c>) —
/// none of this is meant to exist in production.
/// </summary>
[ApiController]
[Route("api/debug")]
public class DebugController(IHostEnvironment environment) : ControllerBase
{
	/// <summary>
	/// Development-only: echoes the request's resolved client IP
	/// (<see cref="HttpContext.Connection"/>.RemoteIpAddress), i.e. whatever the forwarded-headers
	/// middleware (Task 14) settled on. Used to verify that pipeline trusts/ignores
	/// <c>X-Forwarded-For</c> correctly behind a proxy.
	/// </summary>
	[HttpGet("client-ip")]
	public IActionResult GetClientIp()
	{
		if (!environment.IsDevelopment())
			return NotFound();

		// Plain text, not the JSON-wrapped ApiResponse envelope — this is a raw diagnostic echo,
		// and quoting/negotiation noise would only complicate the test's string comparison.
		return Content(HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, "text/plain");
	}
}
