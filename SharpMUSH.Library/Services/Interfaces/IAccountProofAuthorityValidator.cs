using SharpMUSH.Library.Models;
namespace SharpMUSH.Library.Services.Interfaces;
/// <summary>Checks whether the proved account method is still operational at a local security decision.</summary>
public interface IAccountProofAuthorityValidator
{
	Task<bool> IsCurrentAsync(string accountId, AccountProofAuthority authority, CancellationToken ct = default);
}
