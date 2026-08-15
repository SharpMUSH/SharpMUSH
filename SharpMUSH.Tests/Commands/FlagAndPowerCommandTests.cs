using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class FlagAndPowerCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async ValueTask Flag_List_DisplaysAllFlags()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagListCmd");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@flag/list"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "Object Flags:")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Flag_Add_CreatesNewFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		var createdFlag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(createdFlag).IsNotNull();
		await Assert.That(createdFlag!.Name).IsEqualTo(flagName);
		await Assert.That(createdFlag.Symbol).IsEqualTo(symbol);
		await Assert.That(createdFlag.System).IsFalse();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagCreatedWithSymbolFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Add_PreventsSystemFlagCreation()
	{
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		var createdFlag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(createdFlag).IsNotNull();
		await Assert.That(createdFlag!.System).IsFalse();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Add_PreventsDuplicateFlags()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagAlreadyExistsFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Delete_RemovesNonSystemFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		var createdFlag = await Mediator.Send(new CreateObjectFlagCommand(
			flagName, null, symbol, false,
			["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER", "THING", "ROOM", "EXIT"]
		));
		await Assert.That(createdFlag).IsNotNull();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/delete {flagName}"));

		var deletedFlag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(deletedFlag).IsNull();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagDeletedFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Flag_Delete_PreventsSystemFlagDeletion()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/delete WIZARD"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CannotDeleteSystemFlagFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Flag_Delete_HandlesNonExistentFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = "NONEXISTENT_FLAG_XYZ123";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/delete {flagName}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Power_List_DisplaysAllPowers()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/list"));

		// The notify substitute is a session-wide singleton, so assert the call happened, not how many times.
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "Object Powers:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Power_Add_CreatesNewPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));

		var createdPower = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(createdPower).IsNotNull();
		await Assert.That(createdPower!.Name).IsEqualTo(powerName);
		await Assert.That(createdPower.Alias).IsEqualTo(alias);
		await Assert.That(createdPower.System).IsFalse();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PowerCreatedWithAliasFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Add_PreventsSystemPowerCreation()
	{
		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));

		var createdPower = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(createdPower).IsNotNull();
		await Assert.That(createdPower!.System).IsFalse();

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Delete_RemovesNonSystemPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		var createdPower = await Mediator.Send(new CreatePowerCommand(
			powerName, alias, false,
			["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER"]
		));
		await Assert.That(createdPower).IsNotNull();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/delete {powerName}"));

		var deletedPower = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(deletedPower).IsNull();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PowerDeletedFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Power_Delete_PreventsSystemPowerDeletion()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var builderPower = await Mediator.Send(new GetPowerQuery("BUILDER"));
		if (builderPower != null && builderPower.System)
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single("@power/delete BUILDER"));

			await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CannotDeleteSystemPowerFormat), executor, executor)).IsTrue();
		}
	}

	[Test]
	public async ValueTask Power_Delete_HandlesNonExistentPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = "NONEXISTENT_POWER_XYZ123";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/delete {powerName}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PowerNotFoundFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Flag_Add_RequiresBothArguments()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/add TESTFLAG"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagAddRequiresNameAndSymbol), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Power_Add_RequiresBothArguments()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/add TESTPOWER"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PowerAddRequiresNameAndAlias), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Flag_Disable_DisablesNonSystemFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_DISABLE_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/disable {flagName}"));

		var flag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Disabled).IsTrue();

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Flag '{flagName}' disabled.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Enable_EnablesDisabledFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_ENABLE_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/disable {flagName}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/enable {flagName}"));

		var flag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Disabled).IsFalse();

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Flag '{flagName}' enabled.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Disable_PreventsSystemFlagDisable()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Use WIZARD (a system flag stored in the ObjectFlags table).
		// PLAYER is a type flag added implicitly per-object and is NOT in the ObjectFlags table,
		// so it cannot be looked up or disabled via @flag/disable.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/disable WIZARD"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CannotDeleteSystemFlagFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Power_Disable_DisablesNonSystemPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = $"TEST_POWER_DISABLE_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/disable {powerName}"));

		var power = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(power).IsNotNull();
		await Assert.That(power!.Disabled).IsTrue();

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Power '{powerName}' disabled.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Enable_EnablesDisabledPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = $"TEST_POWER_ENABLE_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/disable {powerName}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/enable {powerName}"));

		var power = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(power).IsNotNull();
		await Assert.That(power!.Disabled).IsFalse();

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Power '{powerName}' enabled.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Disable_PreventsSystemPowerDisable()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/disable Builder"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CannotDisableSystemPowerFormat), executor, executor)).IsTrue();
	}

	[Test]
	[NotInParallel]
	public async ValueTask God_CanSetTrustFlag()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create GodTrustFlagTestObj"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {newDb}=TRUST"));

		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));
		await Assert.That(newObject.Object()).IsNotNull();
		var flags = await newObject.Object()!.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Any(f => f.Name.Equals("TRUST", StringComparison.OrdinalIgnoreCase))).IsTrue();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	private async ValueTask<string[]> PowerNamesOf(DBRef dbref)
	{
		var node = await Mediator.Send(new GetObjectNodeQuery(dbref));
		var powers = await node.Object()!.Powers.Value.ToArrayAsync();
		return powers.Select(p => p.Name).ToArray();
	}

	// PennMUSH src/wiz.c do_power: with no switch, @power <object>=<power> grants the power.
	[Test]
	public async ValueTask Power_Grant_SetsPowerOnObject()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerGrant")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder"));

		await Assert.That(await PowerNamesOf(newDb)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/flags.c set_power: granting emits "<name> - <power> granted."
	[Test]
	public async ValueTask Power_Grant_NotifiesGranted()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("PowerGrantMsg");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {name}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder"));

		// ManipulateSharpObjectService reports flag and power changes with no explicit sender.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{name} - Builder granted.")),
				null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/wiz.c do_power: a leading ! on the power name revokes it.
	[Test]
	public async ValueTask Power_Revoke_ClearsPowerOnObject()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerRevoke")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder"));
		await Assert.That(await PowerNamesOf(newDb)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=!Builder"));
		await Assert.That(await PowerNamesOf(newDb)).DoesNotContain("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/wiz.c do_power splits the right-hand side on spaces and applies each token.
	[Test]
	public async ValueTask Power_Grant_AppliesEverySpaceSeparatedToken()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerMulti")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder Boot"));
		var granted = await PowerNamesOf(newDb);
		await Assert.That(granted).Contains("Builder");
		await Assert.That(granted).Contains("Boot");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=!Builder Boot"));
		var after = await PowerNamesOf(newDb);
		await Assert.That(after).DoesNotContain("Builder");
		await Assert.That(after).Contains("Boot");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/flags.c set_power reports the unrecognised power name, not the object's name.
	[Test]
	public async ValueTask Power_Grant_UnknownPowerNamesThePower()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerUnknown")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=NOSUCHPOWERXYZ"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextEquals(s, "NOSUCHPOWERXYZ - I don't recognize that power.")),
				null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/wiz.c do_power: "Only wizards may grant powers."
	[Test]
	public async ValueTask Power_Grant_RequiresWizard()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PowerNonWiz");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@power {testPlayer.DbRef}=Builder"));

		await Assert.That(await PowerNamesOf(testPlayer.DbRef)).DoesNotContain("Builder");
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s,
					ErrorMessages.Notifications.OnlyWizardsMayGrantPowers)),
				null, INotifyService.NotificationType.Announce);
	}

	// PennMUSH src/wiz.c do_power: with no "=", @power describes the named power; it does not list an object's powers.
	[Test]
	public async ValueTask Power_NoEquals_ShowsPowerInformation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single("@power Builder"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "     Name: Builder")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// PennMUSH src/flags.c do_flag_info: an unknown name reports "No such power."
	[Test]
	public async ValueTask Power_NoEquals_UnknownPowerReportsNoSuchPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single("@power NOSUCHPOWERABC"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.NoSuchPowerInfo), executor, executor)).IsTrue();
	}

	// PennMUSH src/flags.c: every power definition switch is God-only; a wizard is not enough.
	[Test]
	public async ValueTask Power_Add_RequiresGod()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PowerAddNonGod");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@power/add {powerName}=TPOW"));

		await Assert.That(await Mediator.Send(new GetPowerQuery(powerName))).IsNull();
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.NotEnoughMagic), testPlayer.DbRef, testPlayer.DbRef)).IsTrue();
	}

	// PennMUSH src/fundb.c fun_powers routes powers() through do_power, so it honours the same "!" revoke prefix.
	[Test]
	public async ValueTask PowersFunction_SideEffect_HonoursRevokePrefix()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerFnRevoke")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder"));
		await Assert.That(await PowerNamesOf(newDb)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"think [powers({newDb},!Builder)]"));
		await Assert.That(await PowerNamesOf(newDb)).DoesNotContain("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/flags.c list_all_flags filters the listing by a glob pattern.
	[Test]
	public async ValueTask Power_List_FiltersByPattern()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/list Buil*"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextContains(s, "Builder")
					&& !TestHelpers.MessagePlainTextContains(s, "Announce")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
