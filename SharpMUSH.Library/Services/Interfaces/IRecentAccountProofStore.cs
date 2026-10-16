using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IRecentAccountProofStore
{
	Result<RecentAccountProof> Issue(string accountId, string sessionToken, AccountSecurityOperation operation, AccountProofAuthority authority);
	Found<AccountProofAuthority> Consume(string? grant, string accountId, string sessionToken, AccountSecurityOperation operation);
	void RevokeAccount(string accountId);
	void RevokeSession(string sessionToken);
}
