using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Messaging;

/// <summary>
/// How everything the bus carries is written as JSON: the generated contract of every message in
/// <see cref="Messages"/>, so a process compiled ahead of time (the socket server, the rendering worker)
/// reads and writes them without reflection.
/// </summary>
/// <remarks>
/// Where reflection is available (the server, the tests), a type left out of <see cref="MessagingJsonContext"/>
/// still serializes, with the same reflection-based contract the bus always used. A compiled-ahead process has
/// only the generated one, so a new message type belongs in the context; <c>MessagingJsonContextTests</c>
/// fails one that is not.
/// </remarks>
public static class MessagingJson
{
	public static JsonSerializerOptions Options { get; } = Create();

	/// <summary>The contract for <typeparamref name="T"/>.</summary>
	public static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

	/// <summary>The contract for <paramref name="type"/>, for a reader that knows the type only at run time.</summary>
	public static JsonTypeInfo TypeInfo(Type type) => Options.GetTypeInfo(type);

	private static JsonSerializerOptions Create()
	{
		var options = new JsonSerializerOptions { TypeInfoResolver = Resolver() };
		options.MakeReadOnly();
		return options;
	}

	// IsReflectionEnabledByDefault is a feature switch: published ahead of time it is false, and the trimmer
	// removes the reflection branch. The analyzer does not treat it as a guard, so it is suppressed here.
	[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only where reflection-based serialization is enabled.")]
	[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only where reflection-based serialization is enabled.")]
	private static IJsonTypeInfoResolver Resolver() =>
		JsonSerializer.IsReflectionEnabledByDefault
			? JsonTypeInfoResolver.Combine(MessagingJsonContext.Default, new DefaultJsonTypeInfoResolver())
			: MessagingJsonContext.Default;
}

[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(ConnectionStateData))]
[JsonSerializable(typeof(BroadcastMessage))]
[JsonSerializable(typeof(ClearPlayerOutputPreferencesMessage))]
[JsonSerializable(typeof(ClientIdentityMessage))]
[JsonSerializable(typeof(ClientTimeZoneMessage))]
[JsonSerializable(typeof(ConnectionClosedMessage))]
[JsonSerializable(typeof(ConnectionEstablishedMessage))]
[JsonSerializable(typeof(ConnectionTrafficReportMessage))]
[JsonSerializable(typeof(ConnectionTrafficRequestMessage))]
[JsonSerializable(typeof(DisconnectConnectionMessage))]
[JsonSerializable(typeof(GMCPNegotiatedMessage))]
[JsonSerializable(typeof(GMCPOutputMessage))]
[JsonSerializable(typeof(GMCPSignalMessage))]
[JsonSerializable(typeof(GMCPSupportsMessage))]
[JsonSerializable(typeof(MSDPOutputMessage))]
[JsonSerializable(typeof(MSDPUpdateMessage))]
[JsonSerializable(typeof(MSSPReportMessage))]
[JsonSerializable(typeof(MSSPReportRequestMessage))]
[JsonSerializable(typeof(MainProcessReadyMessage))]
[JsonSerializable(typeof(MainProcessReconnectedMessage))]
[JsonSerializable(typeof(MainProcessShutdownMessage))]
[JsonSerializable(typeof(MarkupOutputMessage))]
[JsonSerializable(typeof(MarkupPromptMessage))]
[JsonSerializable(typeof(MxpNegotiatedMessage))]
[JsonSerializable(typeof(NAWSUpdateMessage))]
[JsonSerializable(typeof(OutputSettingsMessage))]
[JsonSerializable(typeof(ProbeTerminalMessage))]
[JsonSerializable(typeof(PuebloNegotiatedMessage))]
[JsonSerializable(typeof(SessionDetachedMessage))]
[JsonSerializable(typeof(SessionResumeRequestMessage))]
[JsonSerializable(typeof(SessionResumeResponseMessage))]
[JsonSerializable(typeof(SessionResumedMessage))]
[JsonSerializable(typeof(TelnetInputMessage))]
[JsonSerializable(typeof(TelnetNegotiatedMessage))]
[JsonSerializable(typeof(TelnetOutputMessage))]
[JsonSerializable(typeof(TelnetPromptMessage))]
[JsonSerializable(typeof(TerminalReportMessage))]
[JsonSerializable(typeof(TerminalTypeNegotiatedMessage))]
[JsonSerializable(typeof(UpdateColorStyleMessage))]
[JsonSerializable(typeof(UpdatePlayerPreferencesMessage))]
[JsonSerializable(typeof(UpdatePuebloModeMessage))]
[JsonSerializable(typeof(UpdateTelnetModeMessage))]
[JsonSerializable(typeof(UpdateTerminalFeaturesMessage))]
[JsonSerializable(typeof(UpdateThemeMessage))]
[JsonSerializable(typeof(WebSocketInputMessage))]
[JsonSerializable(typeof(WebSocketOutputMessage))]
[JsonSerializable(typeof(WebSocketPromptMessage))]
public sealed partial class MessagingJsonContext : JsonSerializerContext;
