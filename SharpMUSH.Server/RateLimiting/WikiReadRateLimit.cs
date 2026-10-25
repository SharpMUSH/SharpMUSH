using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using System.Threading.RateLimiting;

namespace SharpMUSH.Server.RateLimiting;

/// <summary>
/// The <c>"wiki-read"</c> policy: a fixed window per client IP on the wiki API and <c>/sitemap.xml</c>, for
/// callers who are not signed in. It keeps a crawler or scraper from taking the server's threads from the game.
/// A signed-in account is not limited, so players and staff never meet it. The address is whatever
/// <c>UseForwardedHeaders</c> resolved, as for <see cref="PublicApiRateLimit"/>.
/// </summary>
public static class WikiReadRateLimit
{
	public const string PolicyName = "wiki-read";

	/// <summary>The partition every signed-in request shares; it has no limiter.</summary>
	public const string SignedIn = "signed-in";

	/// <summary>
	/// Registers the policy. The limits are read from <paramref name="configuration"/> when a client's
	/// partition is first created: <c>RateLimiting:WikiRead:PermitLimit</c> (300),
	/// <c>:WindowSeconds</c> (60) and <c>:QueueLimit</c> (0).
	/// </summary>
	public static RateLimiterOptions AddWikiReadPolicy(this RateLimiterOptions options, IConfiguration configuration)
		=> options.AddPolicy(PolicyName, httpContext => httpContext.User.Identity?.IsAuthenticated == true
			? RateLimitPartition.GetNoLimiter(SignedIn)
			: RateLimitPartition.GetFixedWindowLimiter(
				partitionKey: PublicApiRateLimit.PartitionKey(httpContext),
				_ => new FixedWindowRateLimiterOptions
				{
					PermitLimit = configuration.GetValue("RateLimiting:WikiRead:PermitLimit", 300),
					Window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:WikiRead:WindowSeconds", 60)),
					QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
					QueueLimit = configuration.GetValue("RateLimiting:WikiRead:QueueLimit", 0),
				}));
}
