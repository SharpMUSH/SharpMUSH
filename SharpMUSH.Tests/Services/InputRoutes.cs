using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;

namespace SharpMUSH.Tests.Services;

/// <summary>Routes for a session whose one attribute takes every line, and <paramref name="exit"/> as its exit.</summary>
public static class InputRoutes
{
	public static InputRouteSpec[] To(DBRef target, string attribute, string exit = "done")
		=> [new(exit, target, attribute), new("*", target, attribute)];
}
