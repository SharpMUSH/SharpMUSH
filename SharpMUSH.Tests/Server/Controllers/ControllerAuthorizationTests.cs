using Microsoft.AspNetCore.Authorization;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Pins that the two admin controllers which were shipped with NO authorization attribute — so
/// <c>POST api/databaseconversion/upload</c> (overwrites the whole game DB) and all of
/// <c>api/suggestion</c> were anonymously reachable — are gated at the class level. Reflection over
/// the attribute is enough to catch a regression that drops the gate; the policy machinery itself is
/// covered elsewhere.
/// </summary>
public class ControllerAuthorizationTests
{
	private static AuthorizeAttribute? ClassAuthorize<T>() =>
		(AuthorizeAttribute?)Attribute.GetCustomAttribute(typeof(T), typeof(AuthorizeAttribute));

	[Test]
	public async Task DatabaseConversionController_IsGatedOnServerAdmin()
	{
		var attr = ClassAuthorize<DatabaseConversionController>();

		await Assert.That(attr).IsNotNull();
		await Assert.That(attr!.Policy).IsEqualTo(PortalPermission.ServerAdmin);
	}

	[Test]
	public async Task SuggestionController_IsGatedOnConfigAdmin()
	{
		var attr = ClassAuthorize<SuggestionController>();

		await Assert.That(attr).IsNotNull();
		await Assert.That(attr!.Policy).IsEqualTo(PortalPermission.ConfigAdmin);
	}
}
