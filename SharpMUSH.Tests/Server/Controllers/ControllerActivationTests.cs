using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// MVC builds a controller through <see cref="ActivatorUtilities"/>, which refuses a type with two
/// constructors it could both satisfy unless one is marked <see cref="ActivatorUtilitiesConstructorAttribute"/>.
/// The refusal only shows up when a request arrives: <c>AdminBansController</c> kept a convenience
/// constructor beside its primary one, and every call to <c>api/admin/bans</c> answered 500, which
/// left the Moderation page unable to list bans or host rules.
/// </summary>
public class ControllerActivationTests
{
	public static IEnumerable<Type> Controllers() =>
		typeof(AdminBansController).Assembly.GetTypes()
			.Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(t))
			.OrderBy(t => t.FullName, StringComparer.Ordinal);

	[Test]
	[MethodDataSource(nameof(Controllers))]
	public async Task The_framework_can_choose_a_constructor(Type controller)
	{
		var factory = () => ActivatorUtilities.CreateFactory(controller, []);

		await Assert.That(factory).ThrowsNothing();
	}

	[Test]
	public async Task Every_controller_is_covered() =>
		await Assert.That(Controllers().Count()).IsGreaterThan(20);
}
