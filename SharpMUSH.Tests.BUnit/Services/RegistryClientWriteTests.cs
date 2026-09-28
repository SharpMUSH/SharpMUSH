using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Tests.Shared;
using System.Net;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The role and application registry writes answer with <see cref="ApiResult{T}"/>. Assign, remove
/// and application delete used to answer <see langword="bool"/>, so "Unknown role: x" reached the
/// admin as "Failed to assign role." and nothing else.
/// </summary>
public class RegistryClientWriteTests
{
	private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) =>
			new(handler, disposeHandler: false) { BaseAddress = new Uri("https://localhost/") };
	}

	private static (RoleRegistryClient Roles, ApplicationRegistryClient Apps, CapturingHttpHandler Wire) Answering(
		HttpStatusCode status, string? json = null)
	{
		var wire = CapturingHttpHandler.WithJson(status, json);
		var factory = new SingleClientFactory(wire);
		return (new RoleRegistryClient(factory, NullLogger<RoleRegistryClient>.Instance),
			new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance),
			wire);
	}

	[Test]
	public async Task ARefusedAssignmentCarriesTheServersReason()
	{
		var (roles, _, wire) = Answering(HttpStatusCode.BadRequest, """{"error":"Unknown role: bulders"}""");

		var result = await roles.AssignAsync("accounts/42", "bulders");

		await Assert.That(result is ApiFailure { Message: "Unknown role: bulders" }).IsTrue();
		await Assert.That(wire.LastRequest!.Method).IsEqualTo(HttpMethod.Post);
		await Assert.That(wire.LastRequest!.RequestUri!.AbsolutePath)
			.IsEqualTo("/api/roles/account/accounts%2F42/bulders");
	}

	[Test]
	public async Task RemovalSucceedsOnASuccessStatus()
	{
		var (roles, _, wire) = Answering(HttpStatusCode.NoContent);

		var result = await roles.RemoveAsync("accounts/42", "builder");

		await Assert.That(result is Success).IsTrue();
		await Assert.That(wire.LastRequest!.Method).IsEqualTo(HttpMethod.Delete);
	}

	[Test]
	public async Task ARefusedApplicationDeleteCarriesTheStatusSentence()
	{
		var (_, apps, _) = Answering(HttpStatusCode.Forbidden);

		var result = await apps.DeleteAsync("chargen");

		await Assert.That(result is ApiFailure { Kind: ApiFailureKind.Forbidden, Message: "Permission denied." })
			.IsTrue();
	}
}
