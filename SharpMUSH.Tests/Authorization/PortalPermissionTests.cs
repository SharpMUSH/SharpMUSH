using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Tests.Authorization;

/// <summary>
/// Unit tests for <see cref="PortalPermission"/> scope membership and expansion.
/// </summary>
public class PortalPermissionTests
{
	[Test]
	public async Task IsKnown_KnownScope_ReturnsTrue()
	{
		await Assert.That(PortalPermission.IsKnown(PortalPermission.WikiRead)).IsTrue();
	}

	[Test]
	public async Task IsKnown_DifferentCase_ReturnsTrue()
	{
		// Expand/Implications treat scopes case-insensitively; IsKnown must agree.
		await Assert.That(PortalPermission.IsKnown("WIKI.READ")).IsTrue();
		await Assert.That(PortalPermission.IsKnown("Wiki.Admin")).IsTrue();
	}

	[Test]
	public async Task IsKnown_UnknownScope_ReturnsFalse()
	{
		await Assert.That(PortalPermission.IsKnown("not.a.scope")).IsFalse();
	}

	[Test]
	public async Task Expand_UmbrellaScope_ImpliesFinerScopes()
	{
		var expanded = PortalPermission.Expand([PortalPermission.WikiAdmin]);

		await Assert.That(expanded.Contains(PortalPermission.WikiRead)).IsTrue();
		await Assert.That(expanded.Contains(PortalPermission.WikiDelete)).IsTrue();
	}
}
