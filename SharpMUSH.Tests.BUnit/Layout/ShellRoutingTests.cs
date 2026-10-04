using SharpMUSH.Client.Layout;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The shell's routing policy without rendering the layout: active routes, the setup and
/// password-change funnels, and the new-tab character hint.
/// </summary>
public class ShellRoutingTests
{
	private const string Base = "https://game.example/";

	[Test]
	[Arguments("https://game.example/", "/", true, true)]
	[Arguments("https://game.example/scenes", "/", true, false)]
	[Arguments("https://game.example/scenes", "/scenes", false, true)]
	[Arguments("https://game.example/Scenes/", "/scenes", false, true)]
	[Arguments("https://game.example/scenes/42", "/scenes", false, true)]
	[Arguments("https://game.example/scenesactive", "/scenes", false, false)]
	[Arguments("https://game.example/mail?x=1", "/mail", false, true)]
	public async Task IsNavActive(string uri, string href, bool exact, bool expected)
		=> await Assert.That(ShellRouting.IsNavActive(uri, href, exact)).IsEqualTo(expected);

	[Test]
	public async Task PlayAndConfigRoutes()
	{
		await Assert.That(ShellRouting.IsPlayRoute(Base + "play/")).IsTrue();
		await Assert.That(ShellRouting.IsPlayRoute(Base + "PLAY")).IsTrue();
		await Assert.That(ShellRouting.IsPlayRoute(Base + "play/x")).IsFalse();
		await Assert.That(ShellRouting.IsConfigRoute(Base + "admin/config/net")).IsTrue();
		await Assert.That(ShellRouting.IsConfigRoute(Base + "admin/roles")).IsFalse();
	}

	[Test]
	[Arguments(null, false, true)]
	[Arguments(true, false, true)]
	[Arguments(false, false, false)]
	[Arguments(null, true, false)]
	public async Task ShouldCheckSetup_UntilAnsweredNo_AndNeverForTheDebugAdmin(bool? needsSetup, bool isDebugAuth, bool expected)
		=> await Assert.That(ShellRouting.ShouldCheckSetup(needsSetup, isDebugAuth)).IsEqualTo(expected);

	[Test]
	public async Task Redirect_SetupPending_FunnelsEverythingToSetup_EvenOverAPasswordChange()
	{
		await Assert.That(ShellRouting.Redirect("/scenes", setupPending: true, mustChangePassword: true) is "/setup").IsTrue();
		await Assert.That(ShellRouting.Redirect("/SETUP", setupPending: true, mustChangePassword: true) is NotFound).IsTrue();
	}

	[Test]
	public async Task Redirect_MustChangePassword_GoesToAccount()
	{
		await Assert.That(ShellRouting.Redirect("/scenes", setupPending: false, mustChangePassword: true) is "/account").IsTrue();
		await Assert.That(ShellRouting.Redirect("/account", setupPending: false, mustChangePassword: true) is NotFound).IsTrue();
		await Assert.That(ShellRouting.Redirect("/scenes", setupPending: false, mustChangePassword: false) is NotFound).IsTrue();
	}

	[Test]
	public async Task ParseCharacterHint_ReadsDbrefAndCreationTime()
	{
		var hint = ShellRouting.ParseCharacterHint("12-1700000000000");

		await Assert.That(hint is CharacterHint { Dbref: 12, CreationTime: 1700000000000 }).IsTrue();
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("12")]
	[Arguments("12-")]
	[Arguments("x-1")]
	[Arguments("1-2-3")]
	public async Task ParseCharacterHint_IgnoresMalformedHints(string? hint)
		=> await Assert.That(ShellRouting.ParseCharacterHint(hint) is NotFound).IsTrue();
}
