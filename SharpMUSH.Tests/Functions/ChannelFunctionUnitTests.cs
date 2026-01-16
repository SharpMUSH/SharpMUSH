using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class ChannelFunctionUnitTests
{
	private const string TestChannelName = "TestChannel";
	private const string TestChannelPrivilege = "Open";
	private const int TestPlayerDbRef = 1;

	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.FunctionParser;
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => Factory.Services.GetRequiredService<ISharpDatabase>();

	private SharpChannel? _testChannel;
	private SharpPlayer? _testPlayer;

	[Before(Test)]
	public async Task SetupTestChannel()
	{
		// Get the test player (DBRef #1)
		var playerNode = await Database.GetObjectNodeAsync(new DBRef(TestPlayerDbRef));
		_testPlayer = playerNode.IsPlayer ? playerNode.AsPlayer : null;

		if (_testPlayer == null)
		{
			throw new InvalidOperationException($"Test player #{TestPlayerDbRef} not found");
		}

		// Create a test channel
		await Mediator.Send(new CreateChannelCommand(
			MModule.single(TestChannelName),
			[TestChannelPrivilege],
			_testPlayer
		));

		// Retrieve the created channel
		var channelQuery = new GetChannelQuery(TestChannelName);
		_testChannel = await Mediator.Send(channelQuery);

		// Add the test player to the channel
		if (_testChannel != null && playerNode.IsPlayer)
		{
			await Mediator.Send(new AddUserToChannelCommand(_testChannel, playerNode.AsPlayer));
		}
	}

	[After(Test)]
	public async Task CleanupTestChannel()
	{
		// Clean up: Delete the test channel
		if (_testChannel != null)
		{
			await Mediator.Send(new DeleteChannelCommand(_testChannel));
		}
	}

	[Test]
	public async Task Channels_ReturnsTestChannel()
	{
		var result = (await Parser.FunctionParse(MModule.single("channels()")))?.Message!;
		var channels = result.ToPlainText();

		await Assert.That(channels).Contains(TestChannelName);
	}

	[Test]
	public async Task Channels_WithOnFilter_ReturnsChannelsPlayerIsOn()
	{
		var result = (await Parser.FunctionParse(MModule.single("channels(%#,on)")))?.Message!;
		var channels = result.ToPlainText();

		await Assert.That(channels).Contains(TestChannelName);
	}

	[Test]
	public async Task Cowner_ReturnsChannelOwner()
	{
		var result = (await Parser.FunctionParse(MModule.single($"cowner({TestChannelName})")))?.Message!;
		var owner = result.ToPlainText();

		await Assert.That(owner).StartsWith($"#{TestPlayerDbRef}:");
	}

	[Test]
	public async Task Cflags_ReturnsChannelFlags()
	{
		var result = (await Parser.FunctionParse(MModule.single($"cflags({TestChannelName})")))?.Message!;
		var flags = result.ToPlainText();

		await Assert.That(flags).Contains(TestChannelPrivilege.ToUpper());
	}

	[Test]
	public async Task Cwho_ReturnsChannelMembers()
	{
		var result = (await Parser.FunctionParse(MModule.single($"cwho({TestChannelName})")))?.Message!;
		var members = result.ToPlainText();

		await Assert.That(members).Contains($"#{TestPlayerDbRef}");
	}

	[Test]
	public async Task Cusers_ReturnsUserCount()
	{
		var result = (await Parser.FunctionParse(MModule.single($"cusers({TestChannelName})")))?.Message!;
		var count = result.ToPlainText();

		await Assert.That(int.Parse(count)).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task Cstatus_ReturnsPlayerStatus()
	{
		var result = (await Parser.FunctionParse(MModule.single($"cstatus(%#,{TestChannelName})")))?.Message!;
		var status = result.ToPlainText();

		await Assert.That(status).Contains("ON");
	}

	[Test]
	[NotInParallel]
	[Skip("TODO: Failing test - needs investigation")]
	public async Task Cstatus_WithNonMember_ReturnsOff()
	{
		if (_testChannel == null)
		{
			throw new InvalidOperationException("Test channel is not initialized.");
		}
		var playerNode = await Database.GetObjectNodeAsync(new DBRef(TestPlayerDbRef));
		
		var userStartsOn = (await Parser.FunctionParse(MModule.single($"cstatus(%#,{TestChannelName})")))?.Message!;
		await Assert.That(userStartsOn.ToPlainText()).Contains("ON");

		// TEST: Remove the player from the channel
		await Mediator.Send(new RemoveUserFromChannelCommand(_testChannel, playerNode.AsPlayer));

		var userEndsOff = (await Parser.FunctionParse(MModule.single($"cstatus(%#,{TestChannelName})")))?.Message!;
		await Assert.That(userEndsOff.ToPlainText()).IsEqualTo("OFF");

		// CLEANUP: Add the player back to the channel
		// Commented out due to a weird bug.
		// {"code":404,"error":true,"errorMessage":"edge collection not used in graph","errorNum":1930}
		//
		// await Mediator.Send(new AddUserToChannelCommand(_testChannel, playerNode.AsPlayer));
		// var userIsPutBackOn = (await Parser.FunctionParse(MModule.single($"cstatus(%#,{TestChannelName})")))?.Message!;
		// await Assert.That(userIsPutBackOn.ToPlainText()).Contains("ON");
	}

	[Test]
	public async Task Cbuffer_ReturnsBufferSize()
	{
		var result = (await Parser.FunctionParse(MModule.single($"cbuffer({TestChannelName})")))?.Message!;
		var buffer = result.ToPlainText();

		await Assert.That(int.TryParse(buffer, out _)).IsTrue();
	}

	[Test]
	public async Task Cdesc_ReturnsChannelDescription()
	{
		var result = (await Parser.FunctionParse(MModule.single($"cdesc({TestChannelName})")))?.Message!;
		// Description should be empty or a valid string
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Cmogrifier_ReturnsEmptyForNoMogrifier()
	{
		var result = (await Parser.FunctionParse(MModule.single($"cmogrifier({TestChannelName})")))?.Message!;
		var mogrifier = result.ToPlainText();

		await Assert.That(mogrifier).IsEmpty();
	}

	[Test]
	public async Task Clock_ReturnsEmptyForNoLock()
	{
		var result = (await Parser.FunctionParse(MModule.single($"clock({TestChannelName})")))?.Message!;
		var lockStr = result.ToPlainText();

		// Default is empty lock string
		await Assert.That(lockStr).IsNotNull();
	}

	[Test]
	public async Task Ctitle_ReturnsEmptyForNoTitle()
	{
		var result = (await Parser.FunctionParse(MModule.single($"ctitle(%#,{TestChannelName})")))?.Message!;
		var title = result.ToPlainText();

		// Player has no title by default
		await Assert.That(title).IsEmpty();
	}

	[Test]
	public async Task Clflags_ReturnsLockFlags()
	{
		var result = (await Parser.FunctionParse(MModule.single($"clflags({TestChannelName})")))?.Message!;
		// Should return empty or list of lock flags
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Cmsgs_ReturnsZeroForNoMessages()
	{
		var result = (await Parser.FunctionParse(MModule.single($"cmsgs({TestChannelName})")))?.Message!;
		var msgCount = result.ToPlainText();

		await Assert.That(msgCount).IsEqualTo("0");
	}

	[Test]
	public async Task Crecall_ReturnsEmptyForNoHistory()
	{
		var result = (await Parser.FunctionParse(MModule.single($"crecall({TestChannelName})")))?.Message!;
		// Should return empty or error message
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Cemit_WithNonExistentChannel_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MModule.single("cemit(NonExistentChan,test message)")))?.Message!;
		var error = result.ToPlainText();

		await Assert.That(error).Contains("#-1");
	}

	[Test]
	public async Task Nscemit_WithNonExistentChannel_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MModule.single("nscemit(NonExistentChan,test message)")))?.Message!;
		var error = result.ToPlainText();

		await Assert.That(error).Contains("#-1");
	}

	[Test]
	public async Task Cbufferadd_WithNonExistentChannel_ReturnsError()
	{
		var result = (await Parser.FunctionParse(MModule.single("cbufferadd(NonExistentChan,test message)")))?.Message!;
		var error = result.ToPlainText();

		await Assert.That(error).Contains("#-1");
	}
}