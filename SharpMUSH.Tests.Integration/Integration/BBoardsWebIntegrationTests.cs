using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The routes the portal's Boards page reads and posts to, on the HTTP handler, called the way the
/// portal calls them: the schema, the data and the sidebar for an address, and the act action.
/// </summary>
public partial class BBoardsIntegrationTests
{
	private async Task<string> Objid(TestIsolationHelpers.TestPlayer player) => await God($"think [objid(#{player.DbRef.Number})]");

	private async Task<JsonElement> Http(string method, string path, string body, TestIsolationHelpers.TestPlayer? viewer)
	{
		var dispatcher = WebAppFactoryArg.Services.GetRequiredService<IHttpHandlerCommandDispatcher>();
		DBRef? who = viewer is null ? null : DBRef.Parse(await Objid(viewer));
		var response = (await dispatcher.DispatchAsync(method, path, body, [], IHttpHandlerCommandDispatcher.UnknownAddress, who))
			.Expect<HttpHandlerResult>();
		await Assert.That(response.Status).IsEqualTo(200).Because(response.Body);
		await Assert.That(response.ContentType).StartsWith("application/json");
		await WebAppFactoryArg.QueueBarrierAsync();
		return JsonDocument.Parse(response.Body).RootElement.Clone();
	}

	private async Task<JsonElement> Act(TestIsolationHelpers.TestPlayer viewer, object body) =>
		await Http("POST", "/boards/act", JsonSerializer.Serialize(body), viewer);

	/// <summary>The rows of data field <paramref name="field"/>.</summary>
	private static JsonElement[] Rows(JsonElement data, string field) =>
		data.GetProperty("fields").GetProperty(field).GetProperty("value").EnumerateArray().ToArray();

	private static string Field(JsonElement row, string name) =>
		row.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString() : string.Empty;

	/// <summary>Every element of every section of a schema, as one list.</summary>
	private static JsonElement[] Elements(JsonElement schema) =>
		schema.GetProperty("pages")[0].GetProperty("sections").EnumerateArray()
			.SelectMany(s => s.GetProperty("elements").EnumerateArray()).ToArray();

	/// <summary>The number path of top-level board <paramref name="name"/>, from the index page as the viewer sees it.</summary>
	private async Task<(string Path, string Key)> BoardRow(TestIsolationHelpers.TestPlayer viewer, string name)
	{
		var index = await Http("GET", "/boards/data?at=", "", viewer);
		var row = Rows(index, "boards").Single(r => Field(r, "name") == name);
		return (Field(row, "path"), Field(row, "key"));
	}

	[Test]
	public async Task ThePortalPostsCommentsAndRepliesAndReadsThemBackAsConversations()
	{
		try
		{
			await InstallAsync();
			await InstallAsync("bboards-app");
			var app = (await WebAppFactoryArg.Services.GetRequiredService<IApplicationRegistryService>().GetApplicationAsync("boards"))
				.Expect<SharpMUSH.Library.Models.Portal.Applications.RegisteredApplication>();
			await Assert.That(app.SchemaUrl).IsEqualTo("http/boards/schema?at={path}");
			await Assert.That(app.NavUrl).IsEqualTo("http/boards/nav");
			var admin = await Player("BbwAdm", "bboard-admin");
			var mira = await Player("BbwMira");
			var board = await Board(admin, "BbwTalk");
			var (path, key) = await BoardRow(mira, board);

			var paths = Rows(await Http("GET", "/boards/data?at=", "", admin), "boards").Select(r => Field(r, "path")).ToArray();
			await Assert.That(paths.Distinct().Count()).IsEqualTo(paths.Length).Because(string.Join(" ", paths));
			await Assert.That(paths).DoesNotContain("0");

			var indexSchema = await Http("GET", "/boards/schema?at=", "", mira);
			await Assert.That(indexSchema.GetProperty("title").GetString()).IsEqualTo("Boards");
			var nav = await Http("GET", "/boards/nav", "", mira);
			var navLabels = nav.GetProperty("groups").EnumerateArray()
				.SelectMany(g => g.GetProperty("items").EnumerateArray()).Select(i => Field(i, "label")).ToArray();
			await Assert.That(navLabels).Contains("What's new").And.Contains($"{path} {board}");

			var posted = await Act(mira, new { op = "post", board = key, title = "From the web", text = "Hello [add(1,2)] 50%", format = "md" });
			await Assert.That(posted.GetProperty("ok").GetBoolean()).IsTrue().Because(posted.ToString());
			var postUrl = posted.GetProperty("redirect").GetString()!;
			await Assert.That(postUrl).StartsWith($"/apps/boards/{path}/");
			var postPath = postUrl["/apps/boards/".Length..];
			var adminRow = Rows(await Http("GET", "/boards/data?at=", "", admin), "boards").Single(r => Field(r, "name") == board);
			await Assert.That(Field(adminRow, "new")).IsEqualTo("1 new");
			var unread = Rows(await Http("GET", "/boards/data?at=unread", "", admin), "boards");
			await Assert.That(unread.Select(r => Field(r, "name"))).Contains(board)
				.Because("the unread page lists the boards with something new and nothing empty between them");
			await Assert.That(await As(admin, $"+bbread {postPath}")).Contains("Hello [add(1,2)] 50%")
				.Because("a post from the portal is the same post +bbread shows, never evaluated");

			var thread = await Http("GET", $"/boards/schema?at={postPath}", "", mira);
			var buttons = Elements(thread).Where(e => Field(e, "kind") == "button").ToArray();
			var follow = buttons.First(b => Field(b, "label") == "Follow").GetProperty("values");
			var postKey = Field(follow, "post");

			var commented = await Act(admin, new { op = "comment", post = postKey, text = "First comment", format = "md", at = postPath });
			await Assert.That(commented.GetProperty("ok").GetBoolean()).IsTrue().Because(commented.ToString());
			await Assert.That(commented.GetProperty("data").ToString()).Contains("First comment")
				.Because("a comment answers with the page drawn again");
			await Act(admin, new { op = "comment", post = postKey, text = "Second comment", format = "md", at = postPath });

			var data = await Http("GET", $"/boards/data?at={postPath}", "", mira);
			var rows = Rows(data, "comments");
			await Assert.That(Field(rows[0], "author")).IsEqualTo($"[1] {admin.Name}");
			await Assert.That(Field(rows[0], "unread")).IsEqualTo("True").Because("mira has not read it");
			await Assert.That(rows[0].GetProperty("links")[0].GetProperty("href").GetString())
				.IsEqualTo($"/apps/boards/{postPath}/1");

			var conv = await Http("GET", $"/boards/schema?at={postPath}/1", "", mira);
			var reply = Elements(conv).Single(e => Field(e, "label") == "Post reply").GetProperty("values");
			var replied = await Act(mira, new { op = "reply", post = Field(reply, "post"), comment = Field(reply, "comment"), text = "An answer", format = "md", at = $"{postPath}/1" });
			await Assert.That(replied.GetProperty("ok").GetBoolean()).IsTrue().Because(replied.ToString());

			rows = Rows(await Http("GET", $"/boards/data?at={postPath}", "", mira), "comments");
			await Assert.That(rows.Select(r => Field(r, "body")).ToArray())
				.IsEquivalentTo(new[] { "First comment", "An answer", "Second comment" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
				.Because("a reply sits under the comment it answers");
			await Assert.That(Field(rows[1], "group")).IsEqualTo(Field(rows[0], "group"));
			await Assert.That(Field(rows[1], "reply_to")).IsEmpty().Because("it answers the top of its own conversation");
			await Assert.That(Field(rows[2], "group")).IsNotEqualTo(Field(rows[0], "group"));
			await Assert.That(Field(rows[0], "unread")).IsEqualTo("False").Because("reading the last page marked the post read");

			var empty = await Act(mira, new { op = "comment", post = postKey, text = "  ", format = "md", at = postPath });
			await Assert.That(empty.GetProperty("ok").GetBoolean()).IsFalse();
			await Assert.That(empty.GetProperty("errors").GetProperty("text").GetString()).IsEqualTo("Write something first.");
			var bogus = await Act(mira, new { op = "explode" });
			await Assert.That(bogus.GetProperty("errors").GetProperty("_global").GetString()).IsEqualTo("There is no explode action.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task AMushCommentKeepsItsColourAndTheReadLockHidesTheBoard()
	{
		try
		{
			await InstallAsync();
			var admin = await Player("BbwAdm", "bboard-admin");
			var mira = await Player("BbwMira");
			var board = await Board(admin, "BbwInk");
			var post = await Post(admin, board, "Colour", "Plain text.");
			await As(admin, $"+bbcomment/mush {post}=[ansi(r,Red)] words");

			var rows = Rows(await Http("GET", $"/boards/data?at={post}", "", mira), "comments");
			await Assert.That(Field(rows[0], "format")).IsEqualTo("mstring");
			var markup = MarkupString.MarkupTextSerializer.Deserialize(Field(rows[0], "body"));
			await Assert.That(markup.ToPlainText()).IsEqualTo("Red words");
			await Assert.That(markup.Render(MarkupString.MarkupFormat.Ansi)).Contains("\e[").Because("the colour survives the JSON");

			var mine = await Post(mira, board, "Mine", "plain");
			await As(mira, $"+bbedit/all/mush {mine}=[ansi(g,Green)] by [name(me)]");
			var text = Elements(await Http("GET", $"/boards/schema?at={mine}&edit=1", "", admin)).Single(e => Field(e, "key") == "text");
			await Assert.That(Field(text, "default")).IsEmpty()
				.Because("someone else's SharpMUSH text is not copied into a moderator's form, where saving would run it as them");
			await Assert.That(Field(text, "help")).Contains("Someone else wrote this");
			text = Elements(await Http("GET", $"/boards/schema?at={mine}&edit=1", "", mira)).Single(e => Field(e, "key") == "text");
			await Assert.That(Field(text, "default")).IsEqualTo("[ansi(g,Green)] by [name(me)]");

			await As(admin, $"+bblock {board}/read=#false");
			var index = await Http("GET", "/boards/data?at=", "", mira);
			await Assert.That(Rows(index, "boards").Any(r => Field(r, "name") == board)).IsFalse();
			var refused = await Act(mira, new { op = "comment", post = Field(Rows(await Http("GET", $"/boards/data?at={post}", "", admin), "comments")[0].GetProperty("actions")[0].GetProperty("values"), "post"), text = "sneaky", format = "md" });
			await Assert.That(refused.GetProperty("ok").GetBoolean()).IsFalse().Because(refused.ToString());
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task ALargeThreadPagesByConversationAndADeepOneLinksToTheRest()
	{
		try
		{
			await InstallAsync();
			var admin = await Player("BbwAdm", "bboard-admin");
			var board = await Board(admin, "BbwBig");
			var post = await Post(admin, board, "Busy", new string('x', 6000));
			for (var i = 1; i <= 20; i++)
			{
				await As(admin, $"+bbcomment {post}=Comment {i} {new string('y', 500)}");
			}

			for (var i = 1; i <= 8; i++)
			{
				await As(admin, $"+bbreply {post}/1=Reply {i}");
			}

			var page1 = Rows(await Http("GET", $"/boards/data?at={post}", "", admin), "comments");
			var groups = page1.Select(r => Field(r, "group")).Distinct().Count();
			await Assert.That(groups).IsEqualTo(15).Because("a page holds 15 conversations");
			var first = page1.Where(r => Field(r, "group") == Field(page1[0], "group")).ToArray();
			await Assert.That(first.Length).IsEqualTo(6).Because("the top comment and its first five replies");
			await Assert.That(Field(first[^1], "children_hidden")).IsEqualTo("3");
			await Assert.That(Field(first[^1], "more")).IsEqualTo($"/apps/boards/{post}/1");

			var page2 = Rows(await Http("GET", $"/boards/data?at={post}&page=2", "", admin), "comments");
			await Assert.That(page2.Select(r => Field(r, "group")).Distinct().Count()).IsEqualTo(5);
			var schema = await Http("GET", $"/boards/schema?at={post}&page=2", "", admin);
			await Assert.That(schema.ToString()).Contains("Page 2 of 2");

			var key = Field(Rows(await Http("GET", $"/boards/data?at={post}", "", admin), "comments")[0].GetProperty("actions")[0].GetProperty("values"), "post");
			var marker = TestIsolationHelpers.GenerateUniqueName("Last");
			var commented = await Act(admin, new { op = "comment", post = key, text = marker, format = "md", at = post, page = "1" });
			await Assert.That(commented.GetProperty("data").ToString()).Contains(marker)
				.Because("a new comment answers with the last page, where it lands");

			var conv = Rows(await Http("GET", $"/boards/data?at={post}/1", "", admin), "comments");
			await Assert.That(conv.Length).IsEqualTo(9).Because("the conversation page shows every reply");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
