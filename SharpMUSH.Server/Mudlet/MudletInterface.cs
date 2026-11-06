using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SharpMUSH.Server.Mudlet;

/// <summary>
/// The interface package every game can offer Mudlet without making one: a panel with the room, gauges,
/// chat and a map, drawn from the GMCP the gmcp package sends. gmcp-mudlet offers it with <c>Client.GUI</c>
/// when the game's <c>gui-url</c> setting is empty, at <c>&lt;mud_url&gt;</c><see cref="Path"/>.
/// </summary>
/// <remarks>
/// The package is <c>config.lua</c> and one Mudlet XML file holding <c>sharpmush-ui.lua</c>, zipped once.
/// Mudlet names an installed package after the last part of the URL, so <see cref="Path"/> never changes;
/// a new interface is a new <see cref="Version"/>, which Mudlet reinstalls when <c>Client.GUI</c> names it.
/// </remarks>
public static partial class MudletInterface
{
	/// <summary>Where the package is served. Its file name is the package's name in Mudlet.</summary>
	public const string Path = "/mudlet/sharpmush-ui.mpackage";

	/// <summary>The package's name inside Mudlet: <see cref="Path"/>'s file name without the extension.</summary>
	public const string PackageName = "sharpmush-ui";

	private static readonly Lazy<string> ConfigLua = new(() => Read("config.lua"));
	private static readonly Lazy<string> Script = new(() => Read("sharpmush-ui.lua"));
	private static readonly Lazy<byte[]> Archive = new(Build);

	/// <summary>The version <c>config.lua</c> gives, which gmcp-mudlet sends as Client.GUI's version.</summary>
	public static string Version => VersionPattern().Match(ConfigLua.Value) is { Success: true } match
		? match.Groups["version"].Value
		: string.Empty;

	/// <summary>The Lua the package runs.</summary>
	public static string Lua => Script.Value;

	/// <summary>The .mpackage, as Mudlet downloads it.</summary>
	public static ReadOnlyMemory<byte> Package => Archive.Value;

	public static IEndpointRouteBuilder MapMudletInterface(this IEndpointRouteBuilder endpoints)
	{
		// Anonymous: Mudlet downloads it before anyone has logged in, with no portal session.
		endpoints.MapGet(Path, () => Results.Bytes(Package, "application/zip", $"{PackageName}.mpackage"));
		return endpoints;
	}

	/// <summary>The Mudlet XML holding the script: one folder named for the package, one script inside it.</summary>
	public static string Xml()
	{
		var document = new XDocument(
			new XDeclaration("1.0", "UTF-8", null),
			new XDocumentType("MudletPackage", null, null, null),
			new XElement("MudletPackage", new XAttribute("version", "1.001"),
				new XElement("ScriptPackage",
					ScriptElement("ScriptGroup", folder: true, PackageName, string.Empty,
						ScriptElement("Script", folder: false, "SharpMUSH interface", Script.Value)))));

		using var writer = new Utf8StringWriter();
		document.Save(writer);
		return writer.ToString();
	}

	private static XElement ScriptElement(string kind, bool folder, string name, string script, params object[] children)
		=> new(kind,
			new XAttribute("isActive", "yes"),
			new XAttribute("isFolder", folder ? "yes" : "no"),
			new XElement("name", name),
			new XElement("packageName", string.Empty),
			new XElement("script", script),
			new XElement("eventHandlerList"),
			children);

	private static byte[] Build()
	{
		using var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			Add(zip, "config.lua", ConfigLua.Value);
			Add(zip, $"{PackageName}.xml", Xml());
		}

		return stream.ToArray();
	}

	private static void Add(ZipArchive zip, string name, string text)
	{
		var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
		// A fixed time, so the same interface is the same bytes on every server and every start.
		entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		writer.Write(text);
	}

	private static string Read(string file)
	{
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"SharpMUSH.Server.Mudlet.{file}")
			?? throw new InvalidOperationException($"The Mudlet interface's {file} is not embedded in the server.");
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	[GeneratedRegex(@"^version\s*=\s*\[\[(?<version>[^\]]+)\]\]", RegexOptions.Multiline)]
	private static partial Regex VersionPattern();

	private sealed class Utf8StringWriter : StringWriter
	{
		public override Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
	}
}
