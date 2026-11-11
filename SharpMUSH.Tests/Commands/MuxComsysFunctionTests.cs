using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>TinyMUX's chanfind(), chaninfo(), chanuser() and chanusers().</summary>
public class MuxComsysFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.Services.GetRequiredService<IMUSHCodeParser>();
	private ITaskScheduler Scheduler => Factory.Services.GetRequiredService<ITaskScheduler>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task TheComsysFunctionsDescribeAChannelAndItsMembers()
	{
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "ChanFnOwner");
		var other = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "ChanFnOther");
		var outsider = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "ChanFnOut");
		var channel = $"Cf{TestIsolationHelpers.GenerateUniqueName("F")[^6..]}";
		try
		{
			await Parser.CommandParse(1, Connections, MarkupText.Plain($"@channel/add {channel}=player"));
			await Parser.CommandParse(1, Connections, MarkupText.Plain($"@channel/chown {channel}=*{owner.Name}"));
			await Parser.CommandParse(1, Connections, MarkupText.Plain($"@channel/describe {channel}=Talk about {channel}"));
			await Parser.CommandParse(1, Connections, MarkupText.Plain($"@channel/off {channel}"));
			await Scheduler.SettleForTestsAsync();
			await Run(owner, $"@channel/on {channel}");
			await Run(other, $"@channel/on {channel}");
			await Run(owner, $"addcom cf={channel}");
			await Run(owner, $"@channel/title {channel}=Herald");
			await Run(other, $"@channel/mute {channel}=yes");
			await Run(other, $"@channel/gag {channel}=yes");

			await Assert.That(await Think(owner, $"CF-FIND [chanfind(lcstr({channel}))] [chanfind(<{channel}>)] [chanfind(Talk about {channel})] [chanfind(Nowhere{channel})]"))
				.IsEqualTo($"CF-FIND {channel} {channel} {channel} #-1 CHANNEL NOT FOUND");

			await Assert.That(await Think(owner, $"CF-INFO [chaninfo({channel},name)] [chaninfo({channel},header)] [chaninfo({channel},owner)] [chaninfo({channel},users)]"))
				.IsEqualTo($"CF-INFO {channel} <{channel}> #{owner.DbRef.Number} 2");
			await Assert.That(await Think(owner, $"CF-TYPE [chaninfo({channel},type)] [chaninfo({channel},flags)] [chaninfo({channel},charge)] [chaninfo({channel},canjoin)] [chaninfo({channel},cantransmit)] [chaninfo({channel},canreceive)]"))
				.IsEqualTo("CF-TYPE 775 PL 0 1 1 1");
			await Assert.That(await Think(owner, $"CF-DESC [chaninfo({channel},desc)]|[chaninfo({channel},object)]|[chaninfo({channel},bogus)]"))
				.IsEqualTo($"CF-DESC Talk about {channel}|#-1 PERMISSION DENIED|#-1 INVALID FIELD");

			await Assert.That(await Think(owner, $"CF-USERS [chanusers({channel})]|[chanusers({channel},|,status)]|[chanusers({channel},|,flags)]|[chanusers({channel},|,gagjoin)]"))
				.IsEqualTo($"CF-USERS #{owner.DbRef.Number} #{other.DbRef.Number}|On|Off||OG|0|1");
			await Assert.That(await Think(owner, $"CF-PAGE [chanusers({channel},,name,1,1)] [chanusers({channel},,alias)]"))
				.IsEqualTo($"CF-PAGE {other.Name} #-1 PERMISSION DENIED");

			await Assert.That(await Think(owner, $"CF-USER [chanuser({channel},me,title)] [chanuser({channel},me,status)] [chanuser({channel},me,alias)] [chanuser({channel},*{other.Name},flags)] [chanuser({channel},*{outsider.Name},status)]"))
				.IsEqualTo($"CF-USER Herald On cf OG #-1 NOT ON CHANNEL");
			await Assert.That(await Think(owner, $"CF-USER2 [chanuser({channel},*{other.Name},alias)] [chanuser({channel},me,bogus)] [chanuser({channel},*Nobody{channel},title)]"))
				.IsEqualTo("CF-USER2 #-1 PERMISSION DENIED: NOT CONTROLLED #-1 INVALID FIELD #-1 PLAYER NOT FOUND");

			// Someone not on the channel may not ask about its members.
			await Assert.That(await Think(outsider, $"CF-OUT [chanuser({channel},*{owner.Name},title)]"))
				.IsEqualTo("CF-OUT #-1 PERMISSION DENIED");
		}
		finally
		{
			await Parser.CommandParse(1, Connections, MarkupText.Plain($"@channel/delete {channel}"));
			await Scheduler.SettleForTestsAsync();
			await Connections.Disconnect(owner.Handle);
			await Connections.Disconnect(other.Handle);
			await Connections.Disconnect(outsider.Handle);
		}
	}

	private async Task<List<string>> Run(TestIsolationHelpers.TestPlayer player, string command)
	{
		var heard = Factory.Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain(command));
		await Scheduler.SettleForTestsAsync();
		return Factory.Notifications.For(player.DbRef).Skip(heard).ToList();
	}

	/// <summary>The line <c>think</c> prints for <paramref name="text"/>, found by its first word.</summary>
	private async Task<string> Think(TestIsolationHelpers.TestPlayer player, string text)
	{
		var heard = await Run(player, $"think {text}");
		var tag = text.Split(' ')[0];
		return heard.LastOrDefault(line => line.StartsWith(tag))?.TrimEnd()
			?? throw new InvalidOperationException($"'{tag}' was not printed: {string.Join(" | ", Factory.Notifications.For(player.DbRef))}");
	}
}
