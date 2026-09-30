using System.Security.Claims;
using SharpMUSH.Client.Pages;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components.Characters;

/// <summary>
/// Who sees a profile's edit controls: the account that owns the character, or staff (Royalty and up,
/// who control every character server-side). Any other signed-in visitor would only earn a refusal.
/// </summary>
public class ProfileEditRightsTests
{
	private static ClaimsPrincipal User(string? role) => new(new ClaimsIdentity(
		role is null ? [] : [new Claim(ClaimTypes.Role, role)], "TestScheme"));

	private static readonly AccountCharacter[] Mine = [new(313, 1, "Ilsa Varn", "PLAYER")];

	[Test]
	public async Task TheOwner_CanEdit_CaseInsensitively()
		=> await Assert.That(ProfileEditRights.CanEdit(User("Player"), Mine, "ilsa varn")).IsTrue();

	[Test]
	public async Task AnotherPlayer_CannotEdit()
		=> await Assert.That(ProfileEditRights.CanEdit(User("Player"), Mine, "Tomas Reyes")).IsFalse();

	[Test]
	public async Task Staff_CanEditAnyone()
		=> await Assert.That(ProfileEditRights.CanEdit(User("Royalty"), [], "Tomas Reyes")).IsTrue();

	[Test]
	public async Task AnAnonymousVisitor_CannotEdit()
		=> await Assert.That(ProfileEditRights.CanEdit(new ClaimsPrincipal(new ClaimsIdentity()), [], "Tomas Reyes")).IsFalse();
}
