using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using System.Threading.RateLimiting;

namespace SharpMUSH.Server.RateLimiting;

/// <summary>
/// The <c>"public-api"</c> policy: the admission control on the anonymous surfaces that check a
/// credential or claim something (login, token minting, first-run setup) and on a few read endpoints
/// that opt in with <c>[EnableRateLimiting]</c>.
/// <para>
/// One fixed window <b>per client IP</b>. It used to be a single <c>AddFixedWindowLimiter</c>, which
/// is one window shared by every caller: the whole site got 30 requests a minute between it, so a
/// handful of visitors queued each other's logins and one abusive client could lock everybody out of
/// signing in. The address is whatever <c>UseForwardedHeaders</c> resolved, which runs first in the
/// pipeline, so behind a configured proxy this is the real client and not the proxy hop.
/// </para>
/// </summary>
public static class PublicApiRateLimit
{
	public const string PolicyName = "public-api";

	/// <summary>The partition a request with no known remote address falls into.</summary>
	public const string UnknownClient = "unknown";

	/// <summary>
	/// Registers the policy. The limits are read from <paramref name="configuration"/> when a
	/// client's partition is first created, never at registration, so configuration a test host
	/// appends after <c>ConfigureServices</c> still applies:
	/// <c>RateLimiting:PublicApi:PermitLimit</c> (30), <c>:WindowSeconds</c> (60) and
	/// <c>:QueueLimit</c> (5).
	/// </summary>
	public static RateLimiterOptions AddPublicApiPolicy(this RateLimiterOptions options, IConfiguration configuration)
		=> options.AddPolicy(PolicyName, httpContext =>
			RateLimitPartition.GetFixedWindowLimiter(
				partitionKey: PartitionKey(httpContext),
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = configuration.GetValue("RateLimiting:PublicApi:PermitLimit", 30),
					Window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:PublicApi:WindowSeconds", 60)),
					QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
					QueueLimit = configuration.GetValue("RateLimiting:PublicApi:QueueLimit", 5),
				}));

	/// <summary>The client a request is charged to: its remote address.</summary>
	public static string PartitionKey(HttpContext httpContext)
		=> httpContext.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
}
