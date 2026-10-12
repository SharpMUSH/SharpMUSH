namespace SharpMUSH.Library.Authorization;

/// <summary>Names of host-owned security policies consumed by optional analysis transports.</summary>
public static class AnalysisTransportSecurity
{
	public const string AuthenticationScheme = "MushBasic";
	public const string Policy = "MushAnalysis";
	public const string RateLimitPolicy = "mcp";
}
