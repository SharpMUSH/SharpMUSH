using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Revokes transient authority without requiring a session store dependency.</summary>
public interface IAccountCredentialRevocation
{
	void RevokeAccount(string accountId);
	void RevokeSession(string sessionToken);
	void RevokeCharacter(DBRef character);
}
