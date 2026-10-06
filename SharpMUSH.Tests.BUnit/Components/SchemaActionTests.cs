using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Schema;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Answers every action POST with the next queued body, recording what each one sent.</summary>
internal sealed class SchemaActionHandler : HttpMessageHandler
{
	public ConcurrentQueue<(string Path, JsonElement Body)> Posts { get; } = new();
	public ConcurrentQueue<string> Answers { get; } = new();

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(ct);
		Posts.Enqueue((request.RequestUri!.AbsolutePath, JsonDocument.Parse(body).RootElement.Clone()));
		var answer = Answers.TryDequeue(out var next) ? next : """{"ok":true}""";
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
	}
}

/// <summary>
/// What a form or view does with an action: the values a button carries, the data an answer replaces,
/// <c>reset_fields</c>, <c>triggers_action</c>, the display elements a form shows, and a view's timeline
/// actions — the contract the jobs application is written against.
/// </summary>
public class SchemaActionTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly SchemaActionHandler _handler = new();

	public SchemaActionTests()
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(Track(new HttpClient(_handler) { BaseAddress = new Uri("https://localhost:8081/") }));

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

	private static SchemaData Data(params (string Key, string Json)[] fields) =>
		new(fields.ToDictionary(f => f.Key, f => new SchemaFieldValue(Json(f.Json))));

	private static readonly SchemaElement Timeline = new(Kind: "timeline", RowsField: "entries", Empty: "No comments yet.");

	private static PortalSchemaDocument JobForm(SchemaActionSuccess? onComment = null) => new(
		"form", 1, "Job 12", null,
		[
			new SchemaPage("main", null, 1,
			[
				new SchemaSection("Job", 1, null,
				[
					new SchemaElement(Kind: "keyvalue", Fields: ["status"]),
					new SchemaElement(Kind: "image", SrcField: "avatar", Alt: "Avatar"),
					new SchemaElement(Kind: "table", RowsField: "watchers", Columns: [new SchemaColumn("name", "Watcher")]),
					Timeline,
					new SchemaElement(Kind: "field", Key: "comment", Label: "Comment", Type: "textarea"),
					new SchemaElement(Kind: "field", Key: "filter", Label: "Filter", Type: "text", TriggersAction: "filter"),
					new SchemaElement(Kind: "button", Label: "Comment", Action: "comment"),
					new SchemaElement(Kind: "button", Label: "Close", Action: "comment",
						Values: new Dictionary<string, JsonElement> { ["intent"] = Json("\"close\"") }),
				])
			], null, null)
		],
		new Dictionary<string, SchemaAction>
		{
			["comment"] = new("http", "POST", "/http/jobs/12/comment", "fields", onComment, null),
			["filter"] = new("http", "POST", "/http/jobs/12/filter", "fields", null, null),
		});

	private static SchemaData JobData(string comments = "[]") => Data(
		("status", "\"Open\""),
		("avatar", "\"https://example.com/a.png\""),
		("watchers", """[{"name":"Ada"}]"""),
		("entries", comments),
		("comment", "\"draft text\""));

	private IRenderedComponent<MudHarness> RenderForm(PortalSchemaDocument doc, SchemaData data) =>
		Render<MudHarness>(p => p.AddChildContent<SchemaFormRenderer>(c => c.Add(x => x.Document, doc).Add(x => x.Data, data)));

	private void WaitForPosts(IRenderedComponent<MudHarness> cut, int count) =>
		cut.WaitForAssertion(() =>
		{
			if (_handler.Posts.Count < count) throw new InvalidOperationException($"{_handler.Posts.Count} of {count} posts");
		}, TimeSpan.FromSeconds(5));

	private static AngleSharp.Dom.IElement Button(IRenderedComponent<MudHarness> cut, string label, int index = 0) =>
		cut.FindAll("button").Where(b => b.TextContent.Trim() == label).ElementAt(index);

	[Test]
	public async Task A_form_renders_the_display_elements_a_view_has_from_its_data()
	{
		var cut = RenderForm(JobForm(), JobData("""[{"author":"Ada","time":0,"body":"first"}]"""));

		await Assert.That(cut.Find(".schema-kv").TextContent).Contains("Open");
		await Assert.That(cut.Find("img.schema-image").GetAttribute("src")).IsEqualTo("https://example.com/a.png");
		await Assert.That(cut.Find("table").TextContent).Contains("Ada");
		await Assert.That(cut.Find(".schema-timeline-body").TextContent).Contains("first");
		await Assert.That(cut.FindAll("textarea").Count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task A_buttons_values_are_merged_over_the_field_values_it_posts()
	{
		var cut = RenderForm(JobForm(), JobData());

		Button(cut, "Close", 0).Click();
		WaitForPosts(cut, 1);

		var (path, body) = _handler.Posts.Single();
		await Assert.That(path).IsEqualTo("/http/jobs/12/comment");
		await Assert.That(body.GetProperty("comment").GetString()).IsEqualTo("draft text");
		await Assert.That(body.GetProperty("intent").GetString()).IsEqualTo("close");
	}

	[Test]
	public async Task Returned_data_replaces_the_tables_and_timeline_without_a_reload()
	{
		_handler.Answers.Enqueue("""
			{"ok":true,"data":{"fields":{
			  "entries":{"value":[{"author":"Bea","time":0,"body":"second"}],"visible":true},
			  "watchers":{"value":[{"name":"Cy"}],"visible":true}}}}
			""");
		var cut = RenderForm(JobForm(), JobData());
		await Assert.That(cut.Find(".schema-empty").TextContent).IsEqualTo("No comments yet.");

		Button(cut, "Comment").Click();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("second")) throw new InvalidOperationException("data not replaced yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".schema-timeline-author").TextContent).IsEqualTo("Bea");
		await Assert.That(cut.Find("table").TextContent).Contains("Cy");
		await Assert.That(cut.Find("table").TextContent).DoesNotContain("Ada");
	}

	[Test]
	public async Task Reset_fields_clears_every_input_before_merging_the_answer()
	{
		_handler.Answers.Enqueue("""{"ok":true,"fields":{"filter":"open"}}""");
		var cut = RenderForm(JobForm(new SchemaActionSuccess(null, null, MergeFields: false, ResetFields: true)), JobData());

		Button(cut, "Comment").Click();
		WaitForPosts(cut, 1);
		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("draft text")) throw new InvalidOperationException("comment not cleared yet");
		}, TimeSpan.FromSeconds(5));

		// What the next action sends is what the form now holds.
		Button(cut, "Comment").Click();
		WaitForPosts(cut, 2);

		var second = _handler.Posts.Last().Body;
		await Assert.That(second.TryGetProperty("comment", out _)).IsFalse();
		await Assert.That(second.GetProperty("filter").GetString()).IsEqualTo("open");
	}

	[Test]
	public async Task Without_reset_fields_a_merge_keeps_what_was_typed()
	{
		_handler.Answers.Enqueue("""{"ok":true,"fields":{"filter":"open"}}""");
		var cut = RenderForm(JobForm(new SchemaActionSuccess(null, null, MergeFields: true)), JobData());

		Button(cut, "Comment").Click();
		WaitForPosts(cut, 1);
		Button(cut, "Comment").Click();
		WaitForPosts(cut, 2);

		var second = _handler.Posts.Last().Body;
		await Assert.That(second.GetProperty("comment").GetString()).IsEqualTo("draft text");
		await Assert.That(second.GetProperty("filter").GetString()).IsEqualTo("open");
	}

	[Test]
	public async Task A_field_that_triggers_an_action_dispatches_it_when_it_changes()
	{
		var cut = RenderForm(JobForm(), JobData());

		var filter = cut.FindAll("input").First(i => i.GetAttribute("type") is null or "text");
		filter.Change("mine");
		WaitForPosts(cut, 1);

		var (path, body) = _handler.Posts.Single();
		await Assert.That(path).IsEqualTo("/http/jobs/12/filter");
		await Assert.That(body.GetProperty("filter").GetString()).IsEqualTo("mine");
	}

	[Test]
	public async Task A_views_timeline_action_posts_its_values_and_takes_the_returned_data()
	{
		_handler.Answers.Enqueue("""
			{"ok":true,"message":"Deleted.","data":{"fields":{"entries":{"value":[],"visible":true}}}}
			""");
		var view = new PortalSchemaDocument("view", 1, "Job 12", null,
			[new SchemaPage("main", null, 1, [new SchemaSection("Log", 1, null, [Timeline])], null, null)],
			new Dictionary<string, SchemaAction> { ["delete"] = new("http", "POST", "/http/jobs/12/delete", "fields", null, null) });
		var data = Data(("entries", """[{"author":"Ada","time":0,"body":"oops","actions":[{"label":"Delete","action":"delete","values":{"entry":3}}]}]"""));

		var cut = Render<MudHarness>(p => p.AddChildContent<SchemaViewRenderer>(c => c.Add(x => x.Document, view).Add(x => x.Data, data)));
		cut.Find(".schema-timeline-action").Click();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("No comments yet.")) throw new InvalidOperationException("data not replaced yet");
		}, TimeSpan.FromSeconds(5));

		var (path, body) = _handler.Posts.Single();
		await Assert.That(path).IsEqualTo("/http/jobs/12/delete");
		await Assert.That(body.EnumerateObject().Select(p => p.Name).ToList()).IsEquivalentTo(new[] { "entry" });
		await Assert.That(body.GetProperty("entry").GetInt32()).IsEqualTo(3);
	}
}
