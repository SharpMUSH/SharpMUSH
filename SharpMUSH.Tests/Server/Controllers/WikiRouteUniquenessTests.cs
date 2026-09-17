using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The five wiki controllers all sit on <c>api/wiki</c>. That is what keeps the public route map
/// unchanged by the split, and it is also the one way the split can go wrong: two actions declaring
/// the same verb and template no longer collide inside a single class where a reader would see them
/// next to each other, they collide at request time as an <c>AmbiguousMatchException</c> on
/// whichever endpoint a user happens to hit first.
/// </summary>
public class WikiRouteUniquenessTests
{
	private static readonly Type[] WikiControllers =
	[
		typeof(WikiController),
		typeof(WikiBrowseController),
		typeof(WikiRevisionsController),
		typeof(WikiTranslationsController),
		typeof(WikiAdminController),
	];

	private static IEnumerable<(string Verb, string Template, string Action)> Routes() =>
		from controller in WikiControllers
		let prefix = controller.GetCustomAttribute<RouteAttribute>()!.Template
		from method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
		from attribute in method.GetCustomAttributes().OfType<IActionHttpMethodProvider>()
		from verb in attribute.HttpMethods
		select (verb, $"{prefix}/{((IRouteTemplateProvider)attribute).Template}".TrimEnd('/'),
			$"{controller.Name}.{method.Name}");

	[Test]
	public async Task NoTwoWikiActionsClaimTheSameVerbAndTemplate()
	{
		var duplicates = Routes()
			.GroupBy(r => (r.Verb, r.Template))
			.Where(g => g.Count() > 1)
			.Select(g => $"{g.Key.Verb} {g.Key.Template} <- {string.Join(", ", g.Select(r => r.Action))}")
			.ToList();

		await Assert.That(duplicates).IsEmpty();
	}

	/// <summary>
	/// Every route the wiki serves is still under <c>api/wiki</c>. A controller that picked up its own
	/// prefix during a future split would move endpoints the browser calls by literal path.
	/// </summary>
	[Test]
	public async Task EveryWikiControllerSharesTheApiWikiPrefix()
	{
		foreach (var controller in WikiControllers)
		{
			await Assert.That(controller.GetCustomAttribute<RouteAttribute>()?.Template)
				.IsEqualTo("api/wiki")
				.Because($"{controller.Name} serves part of the same API surface");
		}
	}
}
