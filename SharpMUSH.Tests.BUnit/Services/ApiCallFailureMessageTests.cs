using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Tests.Shared;
using System.Net;
using System.Text;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// What <see cref="ApiFailure.Message"/> says when the server refused. The pages put it straight into
/// a snackbar, so it has to be the server's sentence — not the JSON that carried it, and not nothing.
/// </summary>
/// <remarks>
/// The server answers a refusal three ways: <c>BadRequest(new { error = "…" })</c> (the roles,
/// applications and mail controllers), <c>BadRequest("…")</c> as plain text (sitelock, banned names),
/// and a bare <c>NotFound()</c>/<c>Forbid()</c> with a ProblemDetails body or none at all.
/// </remarks>
public class ApiCallFailureMessageTests
{
	private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) =>
			new(handler, disposeHandler: false) { BaseAddress = new Uri("https://localhost/") };
	}

	private static HttpClient Answering(HttpStatusCode status, string body, string mediaType) =>
		new SingleClientFactory(new CapturingHttpHandler(() => new HttpResponseMessage(status)
		{
			Content = new StringContent(body, Encoding.UTF8, mediaType)
		})).CreateClient("api");

	private static async Task<ApiFailure> FailureOf(HttpClient http)
	{
		var result = await http.PostApiAsync("api/anything", new { });
		return result switch
		{
			ApiFailure failure => failure,
			_ => throw new InvalidOperationException("Expected the call to fail.")
		};
	}

	[Test]
	public async Task AnErrorObjectIsUnwrappedToItsSentence()
	{
		var failure = await FailureOf(Answering(
			HttpStatusCode.NotFound, """{"error":"No such character: Bob"}""", "application/json"));

		await Assert.That(failure.Message).IsEqualTo("No such character: Bob");
		await Assert.That(failure.Kind).IsEqualTo(ApiFailureKind.NotFound);
	}

	[Test]
	public async Task PlainTextIsTheMessageAsSent()
	{
		var failure = await FailureOf(Answering(
			HttpStatusCode.Conflict, "Name 'Bob' is already banned", "text/plain"));

		await Assert.That(failure.Message).IsEqualTo("Name 'Bob' is already banned");
	}

	/// <summary><c>Forbid()</c> sends no body; a blank snackbar is not a message.</summary>
	[Test]
	public async Task AnEmptyBodyFallsBackToTheStatusSentence()
	{
		var failure = await FailureOf(Answering(HttpStatusCode.Forbidden, "", "text/plain"));

		await Assert.That(failure.Message).IsEqualTo("Permission denied.");
		await Assert.That(failure.Kind).IsEqualTo(ApiFailureKind.Forbidden);
	}

	/// <summary>A bare <c>NotFound()</c> sends ProblemDetails; its title is only the status again.</summary>
	[Test]
	public async Task ProblemDetailsWithoutDetailFallsBackToTheStatusSentence()
	{
		var failure = await FailureOf(Answering(
			HttpStatusCode.NotFound,
			"""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"traceId":"00-abc-def-00"}""",
			"application/problem+json"));

		await Assert.That(failure.Message).IsEqualTo("Not found.");
	}

	[Test]
	public async Task ProblemDetailsDetailIsTheMessage()
	{
		var failure = await FailureOf(Answering(
			HttpStatusCode.BadRequest,
			"""{"title":"Bad Request","status":400,"detail":"Slug is required."}""",
			"application/problem+json"));

		await Assert.That(failure.Message).IsEqualTo("Slug is required.");
	}

	[Test]
	public async Task AJsonStringIsItsText()
	{
		var failure = await FailureOf(Answering(
			HttpStatusCode.BadRequest, "\"Host pattern cannot be empty\"", "application/json"));

		await Assert.That(failure.Message).IsEqualTo("Host pattern cannot be empty");
	}

	/// <summary>A JSON object that names no reason says nothing a person can act on.</summary>
	[Test]
	public async Task AJsonObjectWithNoReasonFallsBackToTheStatusSentence()
	{
		var failure = await FailureOf(Answering(
			HttpStatusCode.InternalServerError, """{"sent":false}""", "application/json"));

		await Assert.That(failure.Message).IsEqualTo("Request failed (500).");
	}

	[Test]
	public async Task SuccessIsNotAFailure()
	{
		var http = Answering(HttpStatusCode.OK, """{"sent":true}""", "application/json");

		var result = await http.PostApiAsync("api/anything", new { });

		await Assert.That(result is Success).IsTrue();
	}
}
