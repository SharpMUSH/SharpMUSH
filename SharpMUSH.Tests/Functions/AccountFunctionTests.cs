using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// account() and accountchars(): which account holds a character, open to players.view and to the
/// characters on that account.
/// </summary>
public class AccountFunctionTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _first = null!;
	private TestIsolationHelpers.TestPlayer _second = null!;
	private TestIsolationHelpers.TestPlayer _stranger = null!;
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private SharpAccount _account = null!;

	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();

	[Before(Test)]
	public async Task LinkTwoCharacters()
	{
		_first = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AcctFirst");
		_second = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AcctSecond");
		_stranger = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AcctStranger");
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AcctWiz");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!));

		var username = TestIsolationHelpers.GenerateUniqueName("acct").ToLowerInvariant();
		_account = (await Accounts.CreateAccountAsync(username, null, "correct horse battery")).Expect<SharpAccount>();
		foreach (var player in new[] { _first, _second })
			await Accounts.AttachCharacterAsync(_account.Id!, (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>());
	}

	[After(Test)]
	public async Task Disconnect()
	{
		foreach (var player in new[] { _first, _second, _stranger, _wizard })
			await ConnectionService.Disconnect(player.Handle);
	}

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	[Test]
	public async Task StaffReadTheAccountIdAndName()
	{
		await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_first)})")).IsEqualTo(_account.Id);
		await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_first)},name)")).IsEqualTo(_account.Username);
		await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_stranger)})")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task ACharacterReadsItsOwnAccountIdButNotItsName()
	{
		await Assert.That(await EvalAs(_second.DbRef, $"account({Ref(_first)})")).IsEqualTo(_account.Id);
		await Assert.That(await EvalAs(_second.DbRef, $"account({Ref(_first)},name)")).IsEqualTo("#-1 PERMISSION DENIED");
	}

	[Test]
	public async Task SomeoneElseIsRefused()
	{
		await Assert.That(await EvalAs(_stranger.DbRef, $"account({Ref(_first)})")).IsEqualTo("#-1 PERMISSION DENIED");
		await Assert.That(await EvalAs(_stranger.DbRef, $"accountchars({Ref(_first)})")).IsEqualTo("#-1 PERMISSION DENIED");
	}

	[Test]
	public async Task AccountCharsListsEveryCharacterOnTheAccount()
	{
		var expected = $"{await EvalAs(_wizard.DbRef, $"objid({Ref(_first)})")} {await EvalAs(_wizard.DbRef, $"objid({Ref(_second)})")}";

		await Assert.That(await EvalAs(_first.DbRef, $"accountchars({Ref(_second)})")).IsEqualTo(expected);
		await Assert.That(await EvalAs(_wizard.DbRef, $"accountchars({Ref(_first)})")).IsEqualTo(expected);
	}

	[Test]
	public async Task ACharacterOnNoAccountListsOnlyItself()
		=> await Assert.That(await EvalAs(_stranger.DbRef, $"accountchars({Ref(_stranger)})"))
			.IsEqualTo(await EvalAs(_stranger.DbRef, $"objid({Ref(_stranger)})"));

	[Test]
	public async Task AnUnknownFieldIsAnError()
		=> await Assert.That(await EvalAs(_wizard.DbRef, $"account({Ref(_first)},email)")).IsEqualTo("#-1 FIELD MUST BE ID OR NAME");
}
