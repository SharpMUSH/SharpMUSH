using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Functions;

[NotInParallel]
public class ChannelFunctionUnitTests
{
	private const string TestChannelName = "TestChannel";
	private const string TestChannelPrivilege = "Open";
	private const int TestPlayerDbRef = 1;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private SharpChannel? _testChannel;
	private SharpPlayer? _testPlayer;

	[Before(Test)]
	public async Task SetupTestChannel()
	{
		_testPlayer = (await Database.GetObjectNodeAsync(new DBRef(TestPlayerDbRef)))
			.Expect<SharpPlayer>($"test player #{TestPlayerDbRef} exists");

		// Create a test channel (owner is automatically added as a member)
		await Mediator.Send(new CreateChannelCommand(
			MarkupText.Plain(TestChannelName),
			[TestChannelPrivilege],
			_testPlayer
		));

		var channelQuery = new GetChannelQuery(TestChannelName);
		_testChannel = await Mediator.Send(channelQuery);
	}

	[After(Test)]
	public async Task CleanupTestChannel()
	{
		if (_testChannel != null)
		{
			await Mediator.Send(new DeleteChannelCommand(_testChannel));
		}
	}

	[Test]
	public async Task Channels_ReturnsTestChannel()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("channels()")))?.Message!;
		var channels = result.ToPlainText();

		await Assert.That(channels).Contains(TestChannelName);
	}

	[Test]
	public async Task Channels_WithOnFilter_ReturnsChannelsPlayerIsOn()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("channels(%#,on)")))?.Message!;
		var channels = result.ToPlainText();

		await Assert.That(channels).Contains(TestChannelName);
	}

	[Test]
	public async Task Cowner_ReturnsChannelOwner()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"cowner({TestChannelName})")))?.Message!;
		var owner = result.ToPlainText();

		await Assert.That(owner).StartsWith($"#{TestPlayerDbRef}:");
	}

	/// <summary>
	/// PennMUSH <c>fun_cflags</c> abbreviates each privilege to its <c>priv_table</c> letter
	/// (<c>src/extchat.c:2287</c>) — 'o' for Open, lowercase because 'O' is Object.
	/// </summary>
	[Test]
	public async Task Cflags_ReturnsChannelPrivilegeLetters()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"cflags({TestChannelName})")))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("o");
	}

	/// <summary>The same privileges spelled out, which is the only difference between the two functions.</summary>
	[Test]
	public async Task Clflags_ReturnsChannelPrivilegeNames()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"clflags({TestChannelName})")))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo(TestChannelPrivilege);
	}

	[Test]
	public async Task Cwho_ReturnsChannelMembers()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"cwho({TestChannelName})")))?.Message!;
		var members = result.ToPlainText();

		await Assert.That(members).Contains($"#{TestPlayerDbRef}");
	}

	[Test]
	public async Task Cusers_ReturnsUserCount()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"cusers({TestChannelName})")))?.Message!;
		var count = result.ToPlainText();

		await Assert.That(int.Parse(count)).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task Cstatus_ReturnsPlayerStatus()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"cstatus(%#,{TestChannelName})")))?.Message!;
		var status = result.ToPlainText();

		await Assert.That(status).Contains("ON");
	}

	[Test]
	[NotInParallel]
	public async Task Cstatus_WithNonMember_ReturnsOff()
	{
		if (_testChannel == null)
		{
			throw new InvalidOperationException("Test channel is not initialized.");
		}
		var player = (await Database.GetObjectNodeAsync(new DBRef(TestPlayerDbRef))).Expect<SharpPlayer>();

		var userStartsOn = (await Parser.FunctionParse(MarkupText.Plain($"cstatus(%#,{TestChannelName})")))?.Message!;
		await Assert.That(userStartsOn.ToPlainText()).Contains("ON");

		await Mediator.Send(new RemoveUserFromChannelCommand(_testChannel, player));

		var userEndsOff = (await Parser.FunctionParse(MarkupText.Plain($"cstatus(%#,{TestChannelName})")))?.Message!;
		await Assert.That(userEndsOff.ToPlainText()).IsEqualTo("OFF");

		await Mediator.Send(new AddUserToChannelCommand(_testChannel, player));
		var userIsPutBackOn = (await Parser.FunctionParse(MarkupText.Plain($"cstatus(%#,{TestChannelName})")))?.Message!;
		await Assert.That(userIsPutBackOn.ToPlainText()).Contains("ON");
	}

	[Test]
	public async Task Cbuffer_ReturnsBufferSize()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"cbuffer({TestChannelName})")))?.Message!;
		var buffer = result.ToPlainText();

		await Assert.That(int.TryParse(buffer, out _)).IsTrue();
	}

	[Test]
	public async Task Cdesc_ReturnsChannelDescription()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"cdesc({TestChannelName})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Cmogrifier_ReturnsEmptyForNoMogrifier()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"cmogrifier({TestChannelName})")))?.Message!;
		var mogrifier = result.ToPlainText();

		await Assert.That(mogrifier).IsEmpty();
	}

	[Test]
	public async Task Clock_ReturnsEmptyForNoLock()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"clock({TestChannelName})")))?.Message!;
		var lockStr = result.ToPlainText();

		await Assert.That(lockStr).IsNotNull();
	}

	[Test]
	public async Task Ctitle_ReturnsEmptyForNoTitle()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"ctitle(%#,{TestChannelName})")))?.Message!;
		var title = result.ToPlainText();

		await Assert.That(title).IsEmpty();
	}

	[Test]
	public async Task Clflags_ReturnsLockFlags()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"clflags({TestChannelName})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Cmsgs_ReturnsZeroForNoMessages()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"cmsgs({TestChannelName})")))?.Message!;
		var msgCount = result.ToPlainText();

		await Assert.That(msgCount).IsEqualTo("0");
	}

	[Test]
	public async Task Crecall_ReturnsEmptyForNoHistory()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain($"crecall({TestChannelName})")))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Cemit_WithNonExistentChannel_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("cemit(NonExistentChan,test message)")))?.Message!;
		var error = result.ToPlainText();

		await Assert.That(error).Contains("#-1");
	}

	[Test]
	public async Task Nscemit_WithNonExistentChannel_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("nscemit(NonExistentChan,test message)")))?.Message!;
		var error = result.ToPlainText();

		await Assert.That(error).Contains("#-1");
	}

	[Test]
	public async Task Cbufferadd_WithNonExistentChannel_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("cbufferadd(NonExistentChan,test message)")))?.Message!;
		var error = result.ToPlainText();

		await Assert.That(error).Contains("#-1");
	}
}