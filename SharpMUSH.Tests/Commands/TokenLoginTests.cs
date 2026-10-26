using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary><c>connect token &lt;token&gt;</c>: the portal's one-time login token binds once, then never again.</summary>
public class TokenLoginTests : ServerTestBase
{
	[Test]
	public async Task OneTimeToken_LogsInOnceAndIsThenRefused()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "TokenLogin");
		var token = await WebAppFactoryArg.Services.GetRequiredService<IOttStore>().CreateTokenAsync(player, TimeSpan.FromMinutes(5));
		var first = await RegisterAsync();
		var replay = await RegisterAsync();
		try
		{
			await CommandParser.CommandParse(first, ConnectionService, MString.Plain($"connect token {token}"));
			await CommandParser.CommandParse(replay, ConnectionService, MString.Plain($"connect token {token}"));

			await Assert.That(ConnectionService.Get(first)?.Ref).IsEqualTo(player);
			await Assert.That(ConnectionService.Get(replay)?.Ref).IsNull();
			await Assert.That(Notifications.ForHandle(replay)).Contains("Invalid or expired login token.");
		}
		finally
		{
			await ConnectionService.Disconnect(first);
			await ConnectionService.Disconnect(replay);
		}
	}

	[Test]
	public async Task UnknownToken_IsRefused()
	{
		var handle = await RegisterAsync();
		try
		{
			await CommandParser.CommandParse(handle, ConnectionService,
				MString.Plain($"connect token {TestIsolationHelpers.GenerateUniqueName("NoSuchToken")}"));

			await Assert.That(ConnectionService.Get(handle)?.Ref).IsNull();
			await Assert.That(Notifications.ForHandle(handle)).Contains("Invalid or expired login token.");
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	/// <summary>
	/// A connection at the connect screen, from an address of its own: a refused token counts toward
	/// <c>connect_fail_limit</c>, which would otherwise lock out every other test logging in from localhost.
	/// </summary>
	private async Task<long> RegisterAsync()
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		var address = $"198.51.100.{handle % 250 + 1}";
		await ConnectionService.Register(handle, address, address, "test", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8);
		return handle;
	}
}
