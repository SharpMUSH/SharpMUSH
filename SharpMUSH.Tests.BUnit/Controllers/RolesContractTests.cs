using System.Text.Json;
using SharpMUSH.Client.Models.Roles;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.BUnit.Controllers;

/// <summary>
/// Guards the wire contract between the server <see cref="RolesController.RoleDto"/> and the client
/// <see cref="PortalRoleModel"/>. A divergence here (e.g. the timestamps being <c>long</c> on one side
/// and <c>DateTimeOffset</c> on the other) silently breaks the Roles admin page: the list fails to
/// deserialize (page shows empty) and upserts fail to bind (creating a role fails).
/// </summary>
public class RolesContractTests
{
	// Mirrors System.Net.Http.Json defaults used by HttpClient.GetFromJsonAsync / PostAsJsonAsync.
	private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

	[Test]
	public async Task ServerRoleDto_DeserializesInto_ClientModel()
	{
		var dto = new RolesController.RoleDto(
			Slug: "wizard",
			Name: "Wizard",
			Color: "#6cde9a",
			Priority: 30,
			IsSystem: true,
			Permissions: new Dictionary<string, string> { ["wiki.admin"] = "Allow" },
			CreatedAt: 1_749_859_200_000,
			UpdatedAt: 1_749_859_200_000);

		var json = JsonSerializer.Serialize(dto, Web);
		var model = JsonSerializer.Deserialize<PortalRoleModel>(json, Web);

		await Assert.That(model).IsNotNull();
		await Assert.That(model!.Slug).IsEqualTo("wizard");
		await Assert.That(model.IsSystem).IsTrue();
		await Assert.That(model.CreatedAt).IsEqualTo(1_749_859_200_000L);
		await Assert.That(model.Permissions["wiki.admin"]).IsEqualTo("Allow");
	}

	[Test]
	public async Task ClientModel_DeserializesInto_ServerRoleDto()
	{
		var model = new PortalRoleModel(
			Slug: "moderator",
			Name: "Moderator",
			Color: "#6cde9a",
			Priority: 12,
			IsSystem: false,
			Permissions: new Dictionary<string, string> { ["wiki.admin"] = "Allow" },
			CreatedAt: 1_749_859_200_000,
			UpdatedAt: 1_749_859_200_000);

		var json = JsonSerializer.Serialize(model, Web);
		var dto = JsonSerializer.Deserialize<RolesController.RoleDto>(json, Web);

		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Slug).IsEqualTo("moderator");
		await Assert.That(dto.CreatedAt).IsEqualTo(1_749_859_200_000L);
	}
}
