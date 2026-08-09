using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The first line of every telnet session. It used to be
/// <c>"Welcome back, #12:1786227218582!"</c> — the <see cref="DBRef"/> was passed straight in as
/// the format argument, so its <c>ToString()</c> rendered.
/// </summary>
public class LoginGreetingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static readonly LocalizationService Localization = new();

	/// <summary>A raw dbref or objid rendered into player-facing text: <c>#12</c>, <c>#12:1786…</c>.</summary>
	private static readonly Regex RawDbRef = new(@"#\d+(:\d+)?");

	private async ValueTask<long> RegisterHandleAsync()
	{
		var handle = Random.Shared.NextInt64(800_000, 899_999);
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	[Test]
	public async Task AReturningPlayerIsGreetedByNameAndNotByDbref()
	{
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "GreetByName");
		var name = await NameOfAsync(playerRef);

		var handle = await RegisterHandleAsync();
		await ConnectionService.Bind(handle, playerRef);

		var greeting = GreetingTo(handle);

		await Assert.That(greeting).IsNotNull();
		await Assert.That(greeting!).Contains(name);
		await Assert.That(RawDbRef.IsMatch(greeting)).IsFalse();
	}

	[Test]
	public async Task AReturningPlayerIsWelcomedBack()
	{
		var playerRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "GreetReturning");

		var handle = await RegisterHandleAsync();
		await ConnectionService.Bind(handle, playerRef);

		await Assert.That(GreetingKeyTo(handle))
			.IsEqualTo(nameof(ErrorMessages.Notifications.WelcomeBackFormat));
	}

	private async ValueTask<string> NameOfAsync(DBRef dbref) =>
		(await Mediator.Send(new GetObjectNodeQuery(dbref))).Known().Object().Name;

	/// <summary>The resource key of the last greeting sent to <paramref name="handle"/>.</summary>
	private string? GreetingKeyTo(long handle) => GreetingCallTo(handle)?.GetArguments()[1] as string;

	/// <summary>The last greeting sent to <paramref name="handle"/>, rendered in the neutral locale.</summary>
	private string? GreetingTo(long handle)
	{
		var call = GreetingCallTo(handle);
		return call is null
			? null
			: Localization.Format((string)call.GetArguments()[1]!, null, (object[])call.GetArguments()[2]!);
	}

	private ICall? GreetingCallTo(long handle) =>
		NotifyService.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.NotifyLocalized))
			.Where(call => call.GetArguments() is [long h, string, object[], ..] && h == handle)
			.LastOrDefault(call => (string)call.GetArguments()[1]! is
				nameof(ErrorMessages.Notifications.WelcomeBackFormat));
}
