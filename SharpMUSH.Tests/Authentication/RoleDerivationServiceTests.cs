using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Authentication;

public class RoleDerivationServiceTests
{
	private static SharpObjectFlag Flag(string name) => new()
	{
		Name = name,
		Symbol = name[..1].ToUpperInvariant(),
		SetPermissions = [],
		UnsetPermissions = [],
		System = false,
		TypeRestrictions = [],
	};

	private readonly RoleDerivationService _svc = new();

	[Test]
	public async ValueTask DeriveRole_DbrefOne_ReturnsGod()
	{
		var role = _svc.DeriveRole(1, []);
		await Assert.That(role).IsEqualTo(PortalRole.God);
	}

	[Test]
	public async ValueTask DeriveRole_WizardFlag_ReturnsWizard()
	{
		var role = _svc.DeriveRole(5, [Flag("WIZARD")]);
		await Assert.That(role).IsEqualTo(PortalRole.Wizard);
	}

	[Test]
	public async ValueTask DeriveRole_WizardFlagLowercase_ReturnsWizard()
	{
		var role = _svc.DeriveRole(5, [Flag("wizard")]);
		await Assert.That(role).IsEqualTo(PortalRole.Wizard);
	}

	[Test]
	public async ValueTask DeriveRole_RoyaltyFlag_ReturnsRoyalty()
	{
		var role = _svc.DeriveRole(5, [Flag("ROYALTY")]);
		await Assert.That(role).IsEqualTo(PortalRole.Royalty);
	}

	[Test]
	public async ValueTask DeriveRole_WizardBeatsRoyalty_ReturnsWizard()
	{
		var role = _svc.DeriveRole(5, [Flag("ROYALTY"), Flag("WIZARD")]);
		await Assert.That(role).IsEqualTo(PortalRole.Wizard);
	}

	[Test]
	public async ValueTask DeriveRole_NoFlags_ReturnsPlayer()
	{
		var role = _svc.DeriveRole(5, []);
		await Assert.That(role).IsEqualTo(PortalRole.Player);
	}

	[Test]
	public async ValueTask DeriveRole_OtherFlags_ReturnsPlayer()
	{
		var role = _svc.DeriveRole(5, [Flag("DARK"), Flag("HALTED")]);
		await Assert.That(role).IsEqualTo(PortalRole.Player);
	}

	[Test]
	public async ValueTask DeriveRole_DbrefOneIgnoresWizardFlag_ReturnsGod()
	{
		var role = _svc.DeriveRole(1, [Flag("WIZARD")]);
		await Assert.That(role).IsEqualTo(PortalRole.God);
	}

	[Test]
	public async ValueTask DeriveAccountRole_EmptyCharacters_ReturnsGuest()
	{
		var role = _svc.DeriveAccountRole([]);
		await Assert.That(role).IsEqualTo(PortalRole.Guest);
	}

	[Test]
	public async ValueTask DeriveAccountRole_MixedCharacters_ReturnsBest()
	{
		var characters = new (int DbrefNumber, IEnumerable<SharpObjectFlag> Flags)[]
		{
			(5, [Flag("ROYALTY")]),
			(6, []),
			(7, [Flag("WIZARD")]),
		};
		var role = _svc.DeriveAccountRole(characters);
		await Assert.That(role).IsEqualTo(PortalRole.Wizard);
	}

	[Test]
	public async ValueTask DeriveAccountRole_GodCharacterInList_ReturnsGod()
	{
		var characters = new (int DbrefNumber, IEnumerable<SharpObjectFlag> Flags)[]
		{
			(1, []),
			(5, [Flag("WIZARD")]),
		};
		var role = _svc.DeriveAccountRole(characters);
		await Assert.That(role).IsEqualTo(PortalRole.God);
	}

	[Test]
	public async ValueTask DeriveAccountRole_AllPlayers_ReturnsPlayer()
	{
		var characters = new (int DbrefNumber, IEnumerable<SharpObjectFlag> Flags)[]
		{
			(5, []),
			(6, [Flag("DARK")]),
		};
		var role = _svc.DeriveAccountRole(characters);
		await Assert.That(role).IsEqualTo(PortalRole.Player);
	}
}
