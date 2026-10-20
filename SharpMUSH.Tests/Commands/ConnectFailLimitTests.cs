using System.Text;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>connect_fail_limit</c>: once an address has failed that many logins in ten minutes, its next
/// attempts are refused before any password is checked, while other addresses log in as usual.
/// </summary>
public class ConnectFailLimitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private const string Refusal = "This IP address has failed too many times. Please try again in 10 minutes.";

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();

	private async Task<long> ConnectFromAsync(string address)
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		await Connections.Register(handle, address, address, "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}

	private Task Send(long handle, string line)
		=> Factory.CommandParser.CommandParse(handle, Connections, MarkupText.Plain(line)).AsTask();

	[Test]
	public async Task AnAddressPastTheLimitIsRefusedEvenWithTheRightPassword()
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var limit = (int)Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.ConnectFailLimit;
		var name = TestIsolationHelpers.GenerateUniqueName("FailLimit");
		await TestIsolationHelpers.CreateNamedTestPlayerAsync(Factory.Services, mediator, name);
		var guesser = TestIsolationHelpers.GenerateUniqueName("guesser");
		var bystander = TestIsolationHelpers.GenerateUniqueName("bystander");

		var guessing = await ConnectFromAsync(guesser);
		var refused = await ConnectFromAsync(guesser);
		var elsewhere = await ConnectFromAsync(bystander);
		try
		{
			for (var attempt = 0; attempt < limit; attempt++)
			{
				await Send(guessing, $"connect {name} wrong-guess-{attempt}");
			}

			await Send(refused, $"connect {name} {TestIsolationHelpers.TestPassword}");
			await Send(elsewhere, $"connect {name} {TestIsolationHelpers.TestPassword}");

			await Assert.That(Factory.Notifications.ForHandle(refused).Any(message => message.Contains(Refusal))).IsTrue();
			await Assert.That(Connections.Get(refused)?.Ref).IsNull();
			await Assert.That(Connections.Get(elsewhere)?.Ref).IsNotNull();
		}
		finally
		{
			await Connections.Disconnect(guessing);
			await Connections.Disconnect(refused);
			await Connections.Disconnect(elsewhere);
		}
	}
}
