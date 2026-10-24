using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Markup;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// <c>GET /pictures/png/{path}.png</c>: the game's picture at <c>/{path}</c> as PNG, for an MXP or Pueblo client
/// or the terminal renderer, which cannot read WebP (<see cref="PngPictures"/>).
/// </summary>
[ApiController]
public class PngPicturesController(PngPictureService pictures) : ControllerBase
{
	[HttpGet(PngPictures.Prefix + "/{**path}")]
	[AllowAnonymous]
	public async Task<IActionResult> Get(string path, CancellationToken ct)
	{
		if (PngPictures.Original($"{PngPictures.Prefix}/{path}") is not { } original
			|| await pictures.ConvertAsync(original, ct) is not { } png)
			return NotFound();

		Response.Headers.CacheControl = "public, max-age=86400";
		Response.Headers.XContentTypeOptions = "nosniff";
		return File(png, "image/png");
	}
}
