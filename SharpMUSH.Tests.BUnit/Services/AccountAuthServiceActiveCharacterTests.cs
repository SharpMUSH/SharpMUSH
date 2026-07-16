using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

public class AccountAuthServiceActiveCharacterTests
{
	private static AccountAuthService MakeService() =>
		new(Substitute.For<IHttpClientFactory>(),
			Substitute.For<IJSRuntime>(),
			Substitute.For<ILogger<AccountAuthService>>());

	[Test]
	public async Task SetActiveCharacter_raises_ActiveCharacterChanged()
	{
		var sut = MakeService();
		var raised = 0;
		sut.ActiveCharacterChanged += () => raised++;

		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		await Assert.That(raised).IsEqualTo(1);
		await Assert.That(sut.ActiveCharacter!.Name).IsEqualTo("Wizard");
	}

	[Test]
	public async Task SetActiveCharacter_to_same_character_does_not_re_raise()
	{
		var sut = MakeService();
		var ch = new CharacterSummary(7, 1000L, "Wizard", "");
		sut.SetActiveCharacter(ch);
		var raised = 0;
		sut.ActiveCharacterChanged += () => raised++;

		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		await Assert.That(raised).IsEqualTo(0);
	}

	[Test]
	public async Task CanUseTerminal_is_false_without_a_session()
	{
		var sut = MakeService();
		sut.SetActiveCharacter(new CharacterSummary(7, 1000L, "Wizard", ""));

		// IsLoggedIn is false: AccountSessionToken was never set.
		await Assert.That(sut.CanUseTerminal).IsFalse();
	}

	[Test]
	public async Task HasCharacters_is_false_on_an_empty_roster()
	{
		var sut = MakeService();
		await Assert.That(sut.HasCharacters).IsFalse();
	}
}
