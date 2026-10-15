using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@secret</c> sets, lists and clears the server's secrets, and never shows a value. Each test names a secret of
/// its own, so tests running at once never touch each other's.
/// </summary>
public class SecretCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISecretStore Secrets => WebAppFactoryArg.Services.GetRequiredService<ISecretStore>();
	private IExpandedObjectDataService ServerData => WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
	private DBRef God => WebAppFactoryArg.ExecutorDBRef;

	private static string SecretName()
		=> "test.secret." + string.Concat(TestIsolationHelpers.GenerateUniqueName("s").Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who).Skip(before)];
	}

	private Task<List<string>> AsGod(string command)
		=> MessagesWhile(God, async () => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command)));

	private Task<List<string>> As(TestIsolationHelpers.TestPlayer player, string command)
		=> MessagesWhile(player.DbRef, async () => await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command)));

	[Test]
	public async Task ASetSecretIsListedWithoutItsValueAndReadByTheServer()
	{
		var name = SecretName();
		var value = $"hunter2-{Guid.NewGuid():N}";
		try
		{
			var set = await AsGod($"@secret {name}={value}");
			// The listing wraps a long host across lines of its column; read the column's text back joined.
			var listed = new string(string.Join("\n", await AsGod("@secret"))
				.Where(c => !char.IsWhiteSpace(c) && c != '│' && c != '|').ToArray());

			await Assert.That(set).Contains($"{name} is set. Its value is never shown again.");
			await Assert.That(listed).Contains(name);
			await Assert.That(listed).DoesNotContain(value);
			await Assert.That((await Secrets.GetAsync(name)).Expect<string>()).IsEqualTo(value);
			await Assert.That(System.Text.Json.JsonSerializer.Serialize(await ServerData.GetExpandedServerDataAsync<SecretsData>()))
				.DoesNotContain(value);
		}
		finally
		{
			await Secrets.ClearAsync(name);
		}
	}

	[Test]
	public async Task HostsAreSetListedAndCleared()
	{
		var name = "http." + SecretName();
		try
		{
			var set = await AsGod($"@secret/host {name}=api.example.com *.example.org");
			// The listing wraps a long host across lines of its column; read the column's text back joined.
			var listed = new string(string.Join("\n", await AsGod("@secret"))
				.Where(c => !char.IsWhiteSpace(c) && c != '│' && c != '|').ToArray());
			var cleared = await AsGod($"@secret/host {name}=");

			await Assert.That(set).Contains($"@http may send {name} to: *.example.org api.example.com");
			await Assert.That(listed).Contains("api.example.com");
			await Assert.That(cleared).Contains($"@http may send {name} to no host.");
			await Assert.That(await Secrets.HostsAsync(name)).IsEmpty();
		}
		finally
		{
			await Secrets.SetHostsAsync(name, []);
		}
	}

	/// <summary>The command is not evaluated: brackets, substitutions and a second <c>=</c> are part of the value.</summary>
	[Test]
	public async Task AValueIsStoredExactlyAsTyped()
	{
		var name = SecretName();
		try
		{
			await AsGod($"@secret {name}=a=b[add(1,2)]%r%q0==");

			await Assert.That((await Secrets.GetAsync(name)).Expect<string>()).IsEqualTo("a=b[add(1,2)]%r%q0==");
		}
		finally
		{
			await Secrets.ClearAsync(name);
		}
	}

	[Test]
	public async Task ClearingRemovesTheStoredValue()
	{
		var name = SecretName();
		await AsGod($"@secret {name}=value");

		var cleared = await AsGod($"@secret/clear {name}");
		var again = await AsGod($"@secret/clear {name}");

		await Assert.That(cleared).Contains($"{name} is cleared.");
		await Assert.That(again).Contains($"{name} has no stored value.");
		await Assert.That((await Secrets.GetAsync(name)).Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task WithoutConfigAdminNothingIsSetOrListed()
	{
		var name = SecretName();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService,
			"SecretMortal");

		var set = await As(mortal, $"@secret {name}=value");
		var list = await As(mortal, "@secret");

		await Assert.That(set).Contains("Permission denied.");
		await Assert.That(list).Contains("Permission denied.");
		await Assert.That((await Secrets.GetAsync(name)).Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task ABadNameIsRefused()
	{
		var refused = await AsGod("@secret ../escape=value");

		await Assert.That(refused.Any(line => line.Contains("is not a secret name"))).IsTrue();
		await Assert.That((await Secrets.ListAsync()).Any(secret => secret.Name.Contains("escape"))).IsFalse();
	}

	[Test]
	public async Task TheSqlPasswordIsDeclared()
		=> await Assert.That(Secrets.Definitions.Select(definition => definition.Name)).Contains(SecretDefinition.SqlPassword.Name);
}
