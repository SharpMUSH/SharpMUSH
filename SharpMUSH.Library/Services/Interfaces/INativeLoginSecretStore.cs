using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface INativeLoginSecretStore : IAccountCredentialRevocation
{
	Result<NativeLoginSecretMint> Issue(NativeLoginAuthority authority, string originIp);
	Found<NativeLoginSecretLease> Consume(string secret, string sourceIp, string connectionId);
	void RecordFailure(string sourceIp, string connectionId);
	IReadOnlyList<NativeLoginSecretStatus> List(string accountId);
	bool Revoke(string accountId, string id);
}
