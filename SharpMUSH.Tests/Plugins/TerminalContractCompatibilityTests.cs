using System.Text.Json;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Plugins;

public class TerminalContractCompatibilityTests
{
	private static readonly Type[] NetParameters =
	[
		typeof(string), typeof(string), typeof(string), typeof(string),
		typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(string),
		typeof(bool), typeof(string), typeof(bool), typeof(bool), typeof(bool),
		typeof(bool), typeof(bool), typeof(bool), typeof(string), typeof(string),
		typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(string)
	];

	[Test]
	public async Task PublishedNetConstructorAndDeconstructionRemainCallable()
	{
		var constructor = typeof(NetOptions).GetConstructor(NetParameters);
		await Assert.That(constructor).IsNotNull();
		var original = SharpMUSHOptions.Default().Net;
		var arguments = constructor!.GetParameters()
			.Select(parameter => typeof(NetOptions).GetProperty(parameter.Name!)!.GetValue(original)).ToArray();
		var restored = (NetOptions)constructor.Invoke(arguments);
		await Assert.That(restored).IsEqualTo(original);
		await Assert.That(restored.NegotiationWaitMilliseconds).IsEqualTo(400u);
		var deconstruct = typeof(NetOptions).GetMethod("Deconstruct", NetParameters.Select(type => type.MakeByRefType()).ToArray());
		await Assert.That(deconstruct).IsNotNull();
		var values = new object?[arguments.Length];
		deconstruct!.Invoke(restored, values);
		for (var i = 0; i < arguments.Length; i++)
			await Assert.That(values[i]).IsEqualTo(arguments[i]);
	}

	[Test]
	public async Task NetJsonKeepsNondefaultNegotiationWait()
	{
		var original = SharpMUSHOptions.Default().Net with { NegotiationWaitMilliseconds = 1234 };
		var restored = JsonSerializer.Deserialize<NetOptions>(JsonSerializer.Serialize(original));
		await Assert.That(restored).IsEqualTo(original);
	}

	[Test]
	public async Task PublishedOutputConstructorAndDeconstructionRemainCallable()
	{
		var translations = new Dictionary<string, string> { ["·"] = "-" };
		var message = new OutputSettingsMessage(translations, "https://example.com/media", 120, true);
		var (ascii, media, keepAlive, pueblo) = message;
		await Assert.That(ascii).IsEqualTo(translations);
		await Assert.That(media).IsEqualTo("https://example.com/media");
		await Assert.That(keepAlive).IsEqualTo(120u);
		await Assert.That(pueblo).IsTrue();
		await Assert.That(message.NegotiationWaitMilliseconds).IsEqualTo(400u);
		await Assert.That(message.MaxPictureColumns).IsEqualTo(200);
		await Assert.That(message.MaxPictureRows).IsEqualTo(100);
		await Assert.That(typeof(OutputSettingsMessage).GetConstructor(
			[typeof(IReadOnlyDictionary<string, string>), typeof(string), typeof(uint), typeof(bool)])).IsNotNull();
	}

	[Test]
	public async Task OutputJsonKeepsNewSettings()
	{
		var original = new OutputSettingsMessage(new Dictionary<string, string>(), "https://example.com", 120,
			true, 1234, 80, 40);
		var restored = JsonSerializer.Deserialize<OutputSettingsMessage>(JsonSerializer.Serialize(original));
		await Assert.That(restored!.NegotiationWaitMilliseconds).IsEqualTo(1234u);
		await Assert.That(restored.MaxPictureColumns).IsEqualTo(80);
		await Assert.That(restored.MaxPictureRows).IsEqualTo(40);
		await Assert.That(restored.Pueblo).IsTrue();
	}

	[Test]
	public async Task PublishedProbeBooleanMembersRemainCallable()
	{
		var probe = new TerminalProbeResult(true, true, "terminal", 8, 16);
		var (kitty, sixel, version, width, height) = probe;
		await Assert.That(kitty).IsTrue();
		await Assert.That(sixel).IsTrue();
		await Assert.That(version).IsEqualTo("terminal");
		await Assert.That(width).IsEqualTo(8);
		await Assert.That(height).IsEqualTo(16);
		await Assert.That(typeof(TerminalProbeResult).GetProperty("Sixel")!.PropertyType).IsEqualTo(typeof(bool));
		var changed = probe with { Sixel = false };
		await Assert.That(changed.SixelReply).IsFalse();
		await Assert.That(probe.Sixel).IsTrue();
	}

	[Test]
	[Arguments(null)]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ProbeJsonPreservesTriStateOnExistingWireName(bool? reply)
	{
		var probe = new TerminalProbeResult(null, reply, "terminal", 8, 16);
		var json = JsonSerializer.Serialize(probe);
		using var document = JsonDocument.Parse(json);
		await Assert.That(document.RootElement.GetProperty("Sixel").Deserialize<bool?>()).IsEqualTo(reply);
		await Assert.That(document.RootElement.TryGetProperty("SixelReply", out _)).IsFalse();
		var restored = JsonSerializer.Deserialize<TerminalProbeResult>(json);
		await Assert.That(restored).IsEqualTo(probe);
		await Assert.That(restored!.SixelReply).IsEqualTo(reply);
		await Assert.That(restored.Sixel).IsEqualTo(reply == true);
	}

	[Test]
	public async Task LegacyProbeJsonRemainsReadable()
	{
		var probe = JsonSerializer.Deserialize<TerminalProbeResult>(
			"""{"KittyGraphics":true,"Sixel":false,"Version":"legacy","CellWidth":8,"CellHeight":16}""");
		await Assert.That(probe!.SixelReply).IsFalse();
		await Assert.That(probe.Version).IsEqualTo("legacy");
	}
}
