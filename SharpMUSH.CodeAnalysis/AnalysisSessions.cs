using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.CodeAnalysis;

public interface IAnalysisSessionFactory
{
	IAnalysisSession CreateSession(ClaimsPrincipal principal);
}

public interface IAnalysisSession
{
	string PrincipalKey { get; }
	ValueTask<IMushCodeAnalyzer> CreateAnalyzerAsync(CancellationToken cancellationToken = default);
}

public static class AnalysisIdentity
{
	public const string CredentialVersionClaim = "analysis_credential_version";
	public static string CredentialVersion(string passwordHash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));
}
