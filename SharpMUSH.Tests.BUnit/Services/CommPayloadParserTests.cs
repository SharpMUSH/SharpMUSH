using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The typed parser for the <c>comm-feed</c> package's payloads. The shapes are the ones the package sends
/// (<c>docs/softcode/comm-feed-handler.md</c>), which follow the design handoff's §7.3 proposal.
/// </summary>
public class CommPayloadParserTests
{
	private static readonly DateTimeOffset Received = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

	private const string ChannelLine =
		"""{"v":2,"kind":"channel","to":[],"from":"Wren Halloway","text":"anyone up for a scene?","style":"say","ts":1790780182950,"channel":"Public","fromObjid":"#12:1719500000"}""";

	private const string GroupPage =
		"""{"v":2,"kind":"page","to":["Tomas","Dace"],"from":"Ilsa","text":"Ilsa nods","style":"pose","ts":1790780182950,"fromObjid":"#5:1","toObjids":["#7:2","#8:3"]}""";

	// ── comm.message ────────────────────────────────────────────────────────

	[Test]
	public async Task A_channel_line_reads_every_member()
	{
		var entry = CommPayloadParser.ParseMessage(ChannelLine, Received)!;

		await Assert.That(entry.Message.Kind).IsEqualTo("channel");
		await Assert.That(entry.Message.Channel).IsEqualTo("Public");
		await Assert.That(entry.Message.To).IsEmpty();
		await Assert.That(entry.Message.From).IsEqualTo("Wren Halloway");
		await Assert.That(entry.Message.FromObjId).IsEqualTo("#12:1719500000");
		await Assert.That(entry.Message.Text).IsEqualTo("anyone up for a scene?");
		await Assert.That(entry.Message.Timestamp).IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1790780182950));
		await Assert.That(entry.Recipients).IsEmpty();
	}

	[Test]
	public async Task A_page_pairs_each_recipient_with_its_objid()
	{
		var entry = CommPayloadParser.ParseMessage(GroupPage, Received)!;

		await Assert.That(entry.Message.Kind).IsEqualTo("page");
		await Assert.That(entry.Message.Channel).IsNull();
		await Assert.That(entry.Message.To).IsEquivalentTo(new[] { "Tomas", "Dace" });
		await Assert.That(entry.Recipients).IsEquivalentTo(new[]
		{
			new CommParticipant("Tomas", "#7:2"), new CommParticipant("Dace", "#8:3")
		});
	}

	/// <summary>The id the recall endpoint returns for the same line, so a pulled and a pushed copy are one.</summary>
	[Test]
	public async Task A_channel_lines_id_is_read()
	{
		var entry = CommPayloadParser.ParseMessage(ChannelLine.Replace("\"v\":2,", "\"v\":2,\"id\":1790780182950123,"), Received)!;

		await Assert.That(entry.Message.Id).IsEqualTo(1790780182950123);
	}

	[Test]
	[Arguments("""{"v":2,"kind":"page","from":"X","text":"hi"}""")]
	[Arguments("""{"v":2,"kind":"page","from":"X","text":"hi","id":"soon"}""")]
	[Arguments("""{"v":2,"kind":"page","from":"X","text":"hi","id":1.5}""")]
	public async Task A_line_without_a_whole_number_id_has_none(string json)
	{
		var entry = CommPayloadParser.ParseMessage(json, Received)!;

		await Assert.That(entry.Message.Id).IsNull();
	}

	[Test]
	public async Task A_missing_v_is_read_the_same()
	{
		var entry = CommPayloadParser.ParseMessage(ChannelLine.Replace("\"v\":2,", ""), Received);

		await Assert.That(entry!.Message.Text).IsEqualTo("anyone up for a scene?");
	}

	[Test]
	[Arguments("""{"v":2,"kind":"shout","from":"X","text":"hi"}""")]
	[Arguments("""{"v":2,"from":"X","text":"hi"}""")]
	[Arguments("""{"v":2,"kind":"channel","from":"X","text":"hi"}""")]
	[Arguments("""{"v":2,"kind":"channel","channel":"","from":"X","text":"hi"}""")]
	[Arguments("""["kind","channel"]""")]
	[Arguments("not json")]
	[Arguments("")]
	[Arguments(null)]
	public async Task A_message_that_cannot_be_filed_is_dropped(string? json)
	{
		await Assert.That(CommPayloadParser.ParseMessage(json, Received)).IsNull();
	}

	[Test]
	public async Task Malformed_members_fall_back_without_losing_the_message()
	{
		var entry = CommPayloadParser.ParseMessage(
			"""{"kind":"PAGE","to":["Tomas",7,null,""],"from":42,"fromObjid":"","text":null,"ts":"soon","toObjids":"#7:2"}""",
			Received)!;

		await Assert.That(entry.Message.Kind).IsEqualTo("page");
		await Assert.That(entry.Message.To).IsEquivalentTo(new[] { "Tomas" });
		await Assert.That(entry.Message.From).IsEqualTo(string.Empty);
		await Assert.That(entry.Message.FromObjId).IsNull();
		await Assert.That(entry.Message.Text).IsEqualTo(string.Empty);
		await Assert.That(entry.Message.Timestamp).IsEqualTo(Received)
			.Because("a line whose time is unreadable is filed when it arrived");
		await Assert.That(entry.Recipients).IsEquivalentTo(new[] { new CommParticipant("Tomas", null) })
			.Because("objids that do not line up with the names are not guessed at");
	}

	// ── comm.channels ───────────────────────────────────────────────────────

	[Test]
	public async Task A_channel_list_reads_its_viewer_and_rows()
	{
		var list = CommPayloadParser.ParseChannels(
			"""{"v":2,"viewer":{"name":"Ilsa","objid":"#5:1"},"channels":[{"name":"Public","joined":true},{"name":"Builders","joined":true,"gagged":true},{"name":"Staff","joined":false,"unread":3}]}""")!;

		await Assert.That(list.Viewer).IsEqualTo(new CommParticipant("Ilsa", "#5:1"));
		await Assert.That(list.Channels).IsEquivalentTo(new[]
		{
			new CommChannel("Public", 0), new CommChannel("Builders", 0, Gagged: true), new CommChannel("Staff", 3, Joined: false)
		});
	}

	[Test]
	public async Task A_channel_row_carries_its_member_count_and_description()
	{
		var list = CommPayloadParser.ParseChannels(
			"""{"v":2,"channels":[{"name":"Newbie","joined":false,"members":7,"description":"Ask anything."},{"name":"OOC","joined":true,"members":"many","description":""}]}""")!;

		await Assert.That(list.Channels).IsEquivalentTo(new[]
		{
			new CommChannel("Newbie", 0, Joined: false, Members: 7, Description: "Ask anything."),
			new CommChannel("OOC", 0),
		});
	}

	[Test]
	public async Task Channel_rows_are_tolerated_one_at_a_time()
	{
		var list = CommPayloadParser.ParseChannels(
			"""{"channels":["Public",{"name":""},{"joined":true},7,{"name":"OOC","joined":"no","unread":-2}],"viewer":"Ilsa"}""")!;

		await Assert.That(list.Viewer).IsNull();
		await Assert.That(list.Channels).IsEquivalentTo(new[] { new CommChannel("Public", 0), new CommChannel("OOC", 0) });
	}

	[Test]
	[Arguments("""{"v":2,"channels":{"name":"Public"}}""")]
	[Arguments("""["Public"]""")]
	[Arguments("{")]
	[Arguments(null)]
	public async Task A_list_that_is_not_one_is_unreadable(string? json)
	{
		await Assert.That(CommPayloadParser.ParseChannels(json)).IsNull();
	}
}
