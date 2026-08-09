using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The commands a telnet player can run before logging in. <c>WHO</c> and <c>QUIT</c> are
/// <c>CommandBehavior.SOCKET</c> precisely so they answer at the connect screen, but both opened
/// with an unconditional <c>KnownExecutorObject()</c>, which throws
/// <see cref="ArgumentNullException"/> because a handle at the connect screen has no executor.
/// </summary>
public class ConnectScreenTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async ValueTask<long> AnonymousHandleAsync()
	{
		var handle = Random.Shared.NextInt64(700_000, 799_999);
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	[Test]
	public async Task WhoAtTheConnectScreenListsPlayersInsteadOfThrowing()
	{
		await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WhoVisible");

		var anonymous = await AnonymousHandleAsync();
		await Parser.CommandParse(anonymous, ConnectionService, MModule.single("WHO"));

		var listing = LastNotificationTo(anonymous);

		await Assert.That(listing).IsNotNull();
		await Assert.That(listing!).DoesNotStartWith("#-1 EXCEPTION: ");
		await Assert.That(listing).Contains("WhoVisible");
		await Assert.That(listing).Contains("player");
	}

	/// <summary>
	/// An anonymous viewer must get the mortal layout. The wizard layout carries host names and
	/// descriptors, which is exactly what a nobody at the connect screen must not be handed.
	/// </summary>
	[Test]
	public async Task WhoAtTheConnectScreenUsesTheMortalHeaderNotTheWizardOne()
	{
		var anonymous = await AnonymousHandleAsync();
		await Parser.CommandParse(anonymous, ConnectionService, MModule.single("WHO"));

		var header = LastNotificationTo(anonymous)!.Split('\n')[0];

		await Assert.That(header).Contains("Player Name");
		await Assert.That(header).Contains("Doing");
		await Assert.That(header).DoesNotContain("Host");
		await Assert.That(header).DoesNotContain("Loc #");
		await Assert.That(header).DoesNotContain("Des");
	}

	/// <summary>
	/// QUIT threw at its second statement, before <c>Disconnect()</c> and the
	/// <c>DisconnectConnectionMessage</c> — so the connection also survived a QUIT it had just
	/// refused to acknowledge.
	/// </summary>
	[Test]
	public async Task QuitAtTheConnectScreenSaysGoodbyeAndDropsTheConnection()
	{
		var anonymous = await AnonymousHandleAsync();
		await Parser.CommandParse(anonymous, ConnectionService, MModule.single("QUIT"));

		var messages = NotificationsTo(WebAppFactoryArg, anonymous);

		await Assert.That(messages).Contains("GOODBYE.");
		await Assert.That(messages.Any(m => m.StartsWith("#-1 EXCEPTION: "))).IsFalse();
		await Assert.That(ConnectionService.Get(anonymous)).IsNull();
	}

	private string? LastNotificationTo(long handle) =>
		NotificationsTo(WebAppFactoryArg, handle).LastOrDefault();

	/// <summary>
	/// Plain text of every <c>Notify</c> aimed at <paramref name="handle"/>. The INotifyService
	/// substitute is shared for the whole test session, hence filtering by an isolated handle.
	/// </summary>
	internal static string[] NotificationsTo(ServerWebAppFactory factory, long handle) =>
		factory.Services.GetRequiredService<INotifyService>().ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Where(call => call.GetArguments() is [long h, ..] && h == handle)
			.Select(TextOf)
			.OfType<string>()
			.ToArray();

	private static string? TextOf(ICall call) =>
		call.GetArguments() is [_, OneOf<MString, string> message, ..]
			? message.Match(ms => ms.ToPlainText(), s => s)
			: null;
}
