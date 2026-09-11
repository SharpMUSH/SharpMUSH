using Microsoft.AspNetCore.Mvc;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// A value a controller action goes on to use, or the response it should return straight away instead.
/// </summary>
public partial union ValueOrResponse<T>(T, ActionResult);
