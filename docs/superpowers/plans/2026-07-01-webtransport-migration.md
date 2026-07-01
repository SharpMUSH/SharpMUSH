# WebTransport (migration) Alongside WebSocket — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add WebTransport-over-HTTP/3 to the SharpMUSH terminal-play path alongside WebSocket so a browser session survives network changes via QUIC connection migration.

**Architecture:** Both transports become adapters behind one `IDuplexTransport` byte-pipe interface consumed by a shared `ConnectionPump`; the existing `ConnectionServerService` handle lifecycle is unchanged, so a migrated QUIC connection keeps the same handle. Client uses JS interop to the browser `WebTransport` API with automatic WebSocket fallback. A JetStream-backed replay buffer covers only the fallback (non-migration) reconnect path.

**Tech Stack:** .NET 10, ASP.NET Core Kestrel (experimental WebTransport/HTTP/3), MsQuic, Blazor WebAssembly, JS interop, NATS JetStream (MassTransit-style `IMessageBus`), TUnit/existing test frameworks.

## Global Constraints

- Target framework: `net10.0` (every project).
- Tabs for indentation in C# (match existing files).
- WebTransport code MUST be gated behind a `WebTransportEnabled` feature flag (server and client); when off, behavior is identical to today (WS only).
- Experimental Kestrel flag is required: `<RuntimeHostConfigurationOption Include="Microsoft.AspNetCore.Server.Kestrel.Experimental.WebTransportAndH3Datagrams" Value="true" />`.
- WT stream framing is **length-prefixed**: 4-byte big-endian unsigned length, then that many UTF-8 payload bytes. Applies both directions on the single bidirectional stream. (WebSocket keeps its native message framing.)
- Single bidirectional stream per session (no multiplexing).
- Do not change `ConnectionServerService` session-state semantics except where Task 11–12 explicitly add resume support.
- Commit after every task. Existing WS integration tests MUST stay green after Task 1.

---

## File Structure

**Server (`SharpMUSH.ConnectionServer`):**
- Create `ProtocolHandlers/IDuplexTransport.cs` — transport-agnostic duplex byte pipe.
- Create `ProtocolHandlers/ConnectionPump.cs` — shared register/receive/route/disconnect loop.
- Create `ProtocolHandlers/WebSocketTransport.cs` — `IDuplexTransport` over `System.Net.WebSockets.WebSocket`.
- Create `ProtocolHandlers/FrameCodec.cs` — length-prefixed frame reader/writer (WT only).
- Create `ProtocolHandlers/WebTransportTransport.cs` — `IDuplexTransport` over a WT bidi stream.
- Create `ProtocolHandlers/WebTransportServer.cs` — accepts WT session + stream, hands to pump.
- Modify `ProtocolHandlers/WebSocketServer.cs` — delegate to `ConnectionPump` via `WebSocketTransport`.
- Modify `Program.cs` — Kestrel H3 config, map `/wt`, feature flag, dev cert helper.
- Modify `SharpMUSH.ConnectionServer.csproj` — experimental runtime option.

**Client (`SharpMUSH.Client`):**
- Create `Services/ITransportClient.cs` — unified transport interface.
- Modify `Services/WebSocketClientService.cs` — implement `ITransportClient` (add `Kind`).
- Create `wwwroot/js/webtransport.js` — browser WebTransport wrapper (length-prefix framing).
- Create `Services/WebTransportClientService.cs` — C# over JS interop, implements `ITransportClient`.
- Create `Services/TransportNegotiator.cs` — try WT, fall back to WS.
- Modify `Program.cs` — register negotiator + config.

**Tests:**
- Create `SharpMUSH.Tests/ConnectionServer/ConnectionPumpTests.cs`.
- Create `SharpMUSH.Tests/ConnectionServer/FrameCodecTests.cs`.
- Create `SharpMUSH.Tests/ClientState/TransportNegotiatorTests.cs`.
- Create `test-clients/webtransport-migration-proof.mjs` — external migration proof (manual).

---

## Task 1: Transport seam + refactor WebSocketServer

**Files:**
- Create: `SharpMUSH.ConnectionServer/ProtocolHandlers/IDuplexTransport.cs`
- Create: `SharpMUSH.ConnectionServer/ProtocolHandlers/ConnectionPump.cs`
- Create: `SharpMUSH.ConnectionServer/ProtocolHandlers/WebSocketTransport.cs`
- Modify: `SharpMUSH.ConnectionServer/ProtocolHandlers/WebSocketServer.cs`
- Test: `SharpMUSH.Tests/ConnectionServer/ConnectionPumpTests.cs`

**Interfaces:**
- Consumes: `IConnectionServerService.RegisterAsync(long, string, string, string, Func<byte[],Task>, Func<byte[],Task>, Func<Encoding>, Action)`, `IMessageBus.Publish`, `WebSocketInputMessage`, `NAWSUpdateMessage`, `WebSocketControlFrame.TryParseNaws`.
- Produces:
  - `interface IDuplexTransport { string Kind {get;} string RemoteIp {get;} string Hostname {get;} Task SendAsync(ReadOnlyMemory<byte>, CancellationToken); Task<string?> ReceiveTextAsync(CancellationToken); Task CloseAsync(); }` (`ReceiveTextAsync` returns one decoded UTF-8 frame, or null at close.)
  - `sealed class ConnectionPump(ILogger, IConnectionServerService, IMessageBus, IDescriptorGeneratorService)` with `Task RunAsync(IDuplexTransport transport, long handle, CancellationToken ct)`.

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests/ConnectionServer/ConnectionPumpTests.cs
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

public class ConnectionPumpTests
{
    private sealed class FakeTransport : IDuplexTransport
    {
        private readonly Queue<string?> _frames;
        public List<byte[]> Sent { get; } = new();
        public bool Closed { get; private set; }
        public string Kind => "fake";
        public string RemoteIp => "1.2.3.4";
        public string Hostname => "host";
        public FakeTransport(params string?[] frames) => _frames = new(frames);
        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        { Sent.Add(data.ToArray()); return Task.CompletedTask; }
        public Task<string?> ReceiveTextAsync(CancellationToken ct)
            => Task.FromResult(_frames.Count > 0 ? _frames.Dequeue() : null);
        public Task CloseAsync() { Closed = true; return Task.CompletedTask; }
    }

    [Test]
    public async Task Publishes_input_frame_as_WebSocketInputMessage_then_disconnects_on_close()
    {
        var bus = Substitute.For<IMessageBus>();
        var conn = Substitute.For<IConnectionServerService>();
        var desc = Substitute.For<IDescriptorGeneratorService>();
        var pump = new ConnectionPump(NullLogger<ConnectionPump>.Instance, conn, bus, desc);
        var transport = new FakeTransport("look", null); // one command, then close

        await pump.RunAsync(transport, handle: 42, CancellationToken.None);

        await bus.Received(1).Publish(
            Arg.Is<WebSocketInputMessage>(m => m.Handle == 42 && m.Input == "look"),
            Arg.Any<CancellationToken>());
        await conn.Received(1).DisconnectAsync(42);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SharpMUSH.Tests --filter ConnectionPumpTests`
Expected: FAIL — `IDuplexTransport` / `ConnectionPump` do not exist (compile error).

- [ ] **Step 3: Create the interface**

```csharp
// SharpMUSH.ConnectionServer/ProtocolHandlers/IDuplexTransport.cs
using System.Text;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Transport-agnostic duplex byte pipe for a single terminal-play connection.
/// Implemented by WebSocket and WebTransport adapters so <see cref="ConnectionPump"/>
/// is unaware of the underlying protocol. A migrated QUIC connection is transparent
/// here: <see cref="ReceiveTextAsync"/> simply keeps returning frames.
/// </summary>
public interface IDuplexTransport
{
    string Kind { get; }        // "websocket" | "webtransport"
    string RemoteIp { get; }
    string Hostname { get; }

    /// <summary>Sends one UTF-8 frame to the client.</summary>
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

    /// <summary>Returns one complete decoded UTF-8 frame, or null when the peer closed.</summary>
    Task<string?> ReceiveTextAsync(CancellationToken ct);

    Task CloseAsync();
}
```

- [ ] **Step 4: Create the pump (moves logic out of WebSocketServer verbatim)**

```csharp
// SharpMUSH.ConnectionServer/ProtocolHandlers/ConnectionPump.cs
using System.Text;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Owns the shared connection lifecycle: register the handle with its output delegate,
/// pump inbound frames (routing browser control frames vs commands), and disconnect on close.
/// Transport-agnostic — used by both WebSocket and WebTransport handlers.
/// </summary>
public sealed class ConnectionPump(
    ILogger<ConnectionPump> logger,
    IConnectionServerService connectionService,
    IMessageBus publishEndpoint,
    IDescriptorGeneratorService descriptorGenerator)
{
    public async Task RunAsync(IDuplexTransport transport, long handle, CancellationToken ct)
    {
        await connectionService.RegisterAsync(
            handle,
            transport.RemoteIp,
            transport.Hostname,
            transport.Kind,
            data => transport.SendAsync(data, ct),
            data => transport.SendAsync(data, ct),
            () => Encoding.UTF8,
            () => _ = transport.CloseAsync());

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await transport.ReceiveTextAsync(ct);
                if (message is null) break; // peer closed

                if (WebSocketControlFrame.TryParseNaws(message, out var cols, out var rows))
                    await publishEndpoint.Publish(new NAWSUpdateMessage(handle, rows, cols), ct);
                else
                    await publishEndpoint.Publish(new WebSocketInputMessage(handle, message), ct);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error pumping {Kind} connection {Handle}", transport.Kind, handle);
        }
        finally
        {
            await connectionService.DisconnectAsync(handle);
            descriptorGenerator.ReleaseWebSocketDescriptor(handle);
        }
    }
}
```

- [ ] **Step 5: Create the WebSocket adapter**

```csharp
// SharpMUSH.ConnectionServer/ProtocolHandlers/WebSocketTransport.cs
using System.Net.WebSockets;
using System.Text;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>Adapts a <see cref="WebSocket"/> to <see cref="IDuplexTransport"/> using native text framing.</summary>
public sealed class WebSocketTransport(WebSocket socket, string remoteIp, string hostname) : IDuplexTransport
{
    public string Kind => "websocket";
    public string RemoteIp => remoteIp;
    public string Hostname => hostname;

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (socket.State == WebSocketState.Open)
            await socket.SendAsync(data, WebSocketMessageType.Text, true, ct);
    }

    public async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        var buffer = new byte[1024 * 4];
        using var messageBuffer = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", ct);
                return null;
            }
            messageBuffer.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return messageBuffer.Length > 0 ? Encoding.UTF8.GetString(messageBuffer.ToArray()) : string.Empty;
    }

    public Task CloseAsync()
    {
        if (socket.State == WebSocketState.Open)
            _ = socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Refactor `WebSocketServer.HandleWebSocketAsync` to use the pump**

Replace the body after `AcceptWebSocketAsync` with:

```csharp
public async Task HandleWebSocketAsync(HttpContext context)
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var handle = _descriptorGenerator.GetNextWebSocketDescriptor();
    var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var hostname = context.Request.Headers.Host.ToString();

    var transport = new WebSocketTransport(webSocket, remoteIp, hostname);
    await _pump.RunAsync(transport, handle, context.RequestAborted);
}
```

Add `ConnectionPump _pump` to the constructor (inject it). Register `ConnectionPump` in DI in `Program.cs`: `builder.Services.AddSingleton<ConnectionPump>();`.

- [ ] **Step 7: Run tests**

Run: `dotnet test SharpMUSH.Tests --filter ConnectionPumpTests` → PASS.
Run: `dotnet test SharpMUSH.Tests.Integration --filter WebSocket` → existing WS tests PASS (behavior preserved).

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.ConnectionServer/ProtocolHandlers/ SharpMUSH.ConnectionServer/Program.cs SharpMUSH.Tests/ConnectionServer/ConnectionPumpTests.cs
git commit -m "refactor: extract IDuplexTransport + ConnectionPump seam from WebSocketServer"
```

---

## Task 2: Length-prefixed frame codec (for WebTransport)

**Files:**
- Create: `SharpMUSH.ConnectionServer/ProtocolHandlers/FrameCodec.cs`
- Test: `SharpMUSH.Tests/ConnectionServer/FrameCodecTests.cs`

**Interfaces:**
- Produces:
  - `static class FrameCodec { static Task WriteFrameAsync(Stream, ReadOnlyMemory<byte>, CancellationToken); static Task<byte[]?> ReadFrameAsync(Stream, CancellationToken); }`
  - Wire format: 4-byte big-endian uint length prefix + payload. `ReadFrameAsync` returns null on clean EOF.

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests/ConnectionServer/FrameCodecTests.cs
using System.Text;
using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.Tests.ConnectionServer;

public class FrameCodecTests
{
    [Test]
    public async Task Roundtrips_two_frames_including_payload_with_newlines()
    {
        using var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("line1\nline2"), CancellationToken.None);
        await FrameCodec.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("second"), CancellationToken.None);
        ms.Position = 0;

        var f1 = await FrameCodec.ReadFrameAsync(ms, CancellationToken.None);
        var f2 = await FrameCodec.ReadFrameAsync(ms, CancellationToken.None);
        var f3 = await FrameCodec.ReadFrameAsync(ms, CancellationToken.None);

        Assert.That(Encoding.UTF8.GetString(f1!), Is.EqualTo("line1\nline2"));
        Assert.That(Encoding.UTF8.GetString(f2!), Is.EqualTo("second"));
        Assert.That(f3, Is.Null); // clean EOF
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SharpMUSH.Tests --filter FrameCodecTests`
Expected: FAIL — `FrameCodec` does not exist.

- [ ] **Step 3: Implement the codec**

```csharp
// SharpMUSH.ConnectionServer/ProtocolHandlers/FrameCodec.cs
using System.Buffers.Binary;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Length-prefixed framing for the WebTransport bidirectional stream: a 4-byte big-endian
/// unsigned length followed by that many payload bytes. Needed because a WebTransport stream
/// is a raw byte stream with no message boundaries (unlike WebSocket), and payloads may
/// themselves contain newlines.
/// </summary>
public static class FrameCodec
{
    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEofAsync(stream, header, ct)) return null;
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        var payload = new byte[length];
        if (length > 0 && !await ReadExactlyOrEofAsync(stream, payload, ct))
            throw new EndOfStreamException("Truncated WebTransport frame payload.");
        return payload;
    }

    private static async Task<bool> ReadExactlyOrEofAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], ct);
            if (n == 0) return read != 0 ? throw new EndOfStreamException("Truncated frame header.") : false;
            read += n;
        }
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SharpMUSH.Tests --filter FrameCodecTests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.ConnectionServer/ProtocolHandlers/FrameCodec.cs SharpMUSH.Tests/ConnectionServer/FrameCodecTests.cs
git commit -m "feat: length-prefixed frame codec for WebTransport stream"
```

---

## Task 3: Kestrel HTTP/3 + experimental WebTransport config

**Files:**
- Modify: `SharpMUSH.ConnectionServer/SharpMUSH.ConnectionServer.csproj`
- Modify: `SharpMUSH.ConnectionServer/Program.cs`

**Interfaces:**
- Produces: a boot-time HTTP/3 listener with a WT-capable dev certificate; `WebTransportEnabled` config read from `builder.Configuration.GetValue<bool>("WebTransport:Enabled")`.

- [ ] **Step 1: Add the experimental runtime option to the csproj**

```xml
<!-- SharpMUSH.ConnectionServer.csproj, inside a PropertyGroup/ItemGroup section -->
<ItemGroup>
    <RuntimeHostConfigurationOption
        Include="Microsoft.AspNetCore.Server.Kestrel.Experimental.WebTransportAndH3Datagrams"
        Value="true" Trim="false" />
</ItemGroup>
```

- [ ] **Step 2: Configure Kestrel for HTTP/3 in Program.cs**

Add near the top of `Program.cs` where the builder is configured:

```csharp
// WebTransport requires HTTP/3 + a cert meeting WebTransport requirements.
// The default dev cert does NOT work; generate a short-lived ECDSA cert whose
// SHA-256 hash the browser client pins via serverCertificateHashes (dev only).
var webTransportEnabled = builder.Configuration.GetValue<bool>("WebTransport:Enabled");
if (webTransportEnabled)
{
    builder.WebHost.ConfigureKestrel((ctx, options) =>
    {
        options.ConfigureEndpointDefaults(lo =>
        {
            lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3;
        });
    });
}
```

(Certificate generation helper is added and referenced in Task 4 where the endpoint is mapped, to keep this task limited to protocol negotiation.)

- [ ] **Step 3: Verify the server boots with HTTP/3 advertised**

Run: `WebTransport__Enabled=true dotnet run --project SharpMUSH.ConnectionServer` (Ctrl-C after boot).
Expected: no startup exception; logs show Kestrel listening; `alt-svc: h3` header present on an HTTPS response (`curl -sI --http2 https://localhost:<port>/ | grep -i alt-svc`).

- [ ] **Step 4: Commit**

```bash
git add SharpMUSH.ConnectionServer/SharpMUSH.ConnectionServer.csproj SharpMUSH.ConnectionServer/Program.cs
git commit -m "feat: enable experimental Kestrel HTTP/3 for WebTransport (flagged)"
```

---

## Task 4: WebTransport endpoint + adapter (`/wt`)

**Files:**
- Create: `SharpMUSH.ConnectionServer/ProtocolHandlers/WebTransportTransport.cs`
- Create: `SharpMUSH.ConnectionServer/ProtocolHandlers/WebTransportServer.cs`
- Modify: `SharpMUSH.ConnectionServer/Program.cs`

**Interfaces:**
- Consumes: `FrameCodec`, `ConnectionPump`, `IDescriptorGeneratorService`, `IHttpWebTransportFeature`, `IWebTransportSession`, `IStreamDirectionFeature`.
- Produces: `WebTransportServer.HandleAsync(HttpContext)`; `/wt` endpoint mapped when `WebTransport:Enabled`.

- [ ] **Step 1: Create the WebTransport adapter**

```csharp
// SharpMUSH.ConnectionServer/ProtocolHandlers/WebTransportTransport.cs
using System.Text;
using Microsoft.AspNetCore.Connections;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Adapts a single WebTransport bidirectional stream to <see cref="IDuplexTransport"/> using
/// <see cref="FrameCodec"/> length-prefixed framing. Migration is transparent: the stream
/// (and thus this adapter) survives a QUIC connection migration underneath.
/// </summary>
public sealed class WebTransportTransport(ConnectionContext stream, string remoteIp, string hostname) : IDuplexTransport
{
    private readonly Stream _input = stream.Transport.Input.AsStream();
    private readonly Stream _output = stream.Transport.Output.AsStream();

    public string Kind => "webtransport";
    public string RemoteIp => remoteIp;
    public string Hostname => hostname;

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        => FrameCodec.WriteFrameAsync(_output, data, ct);

    public async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        var frame = await FrameCodec.ReadFrameAsync(_input, ct);
        return frame is null ? null : Encoding.UTF8.GetString(frame);
    }

    public Task CloseAsync()
    {
        stream.Abort();
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Create the WebTransport server handler**

```csharp
// SharpMUSH.ConnectionServer/ProtocolHandlers/WebTransportServer.cs
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Accepts a WebTransport session over HTTP/3, takes its first client-initiated bidirectional
/// stream as the terminal byte pipe, and runs it through the shared <see cref="ConnectionPump"/>.
/// </summary>
public class WebTransportServer(
    ILogger<WebTransportServer> logger,
    ConnectionPump pump,
    IDescriptorGeneratorService descriptorGenerator)
{
    public async Task HandleAsync(HttpContext context)
    {
        var feature = context.Features.GetRequiredFeature<IHttpWebTransportFeature>();
        if (!feature.IsWebTransportRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var session = await feature.AcceptAsync(context.RequestAborted);
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var hostname = context.Request.Headers.Host.ToString();

        while (!context.RequestAborted.IsCancellationRequested)
        {
            var stream = await session.AcceptStreamAsync(context.RequestAborted);
            if (stream is null) return; // session ended

            var direction = stream.Features.GetRequiredFeature<IStreamDirectionFeature>();
            if (!(direction.CanRead && direction.CanWrite))
            {
                logger.LogDebug("Ignoring non-bidirectional WebTransport stream");
                continue;
            }

            var handle = descriptorGenerator.GetNextWebSocketDescriptor();
            var transport = new WebTransportTransport(stream, remoteIp, hostname);
            // One bidi stream == one terminal connection for this spike.
            await pump.RunAsync(transport, handle, context.RequestAborted);
            return;
        }
    }
}
```

- [ ] **Step 3: Map `/wt` and register the handler + dev cert in Program.cs**

```csharp
// In Program.cs service registration:
builder.Services.AddSingleton<WebTransportServer>();

// After app is built, where /ws is mapped, add:
if (webTransportEnabled)
{
    app.Map("/wt", wtApp =>
        wtApp.Run(ctx => ctx.RequestServices.GetRequiredService<WebTransportServer>().HandleAsync(ctx)));
}
```

Add the dev cert helper and wire it into the Kestrel config from Task 3 (`lo.UseHttps(GenerateWebTransportDevCert())`):

```csharp
static System.Security.Cryptography.X509Certificates.X509Certificate2 GenerateWebTransportDevCert()
{
    // WebTransport dev cert: ECDSA P-256, <14 day validity, so the browser accepts it via
    // serverCertificateHashes. Emit the SHA-256 to the log so the client can pin it.
    using var ec = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
    var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
        "CN=localhost", ec, System.Security.Cryptography.HashAlgorithmName.SHA256);
    req.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509SubjectAlternativeNameBuilder()
        .Also(b => b.AddDnsName("localhost")).Build());
    var now = DateTimeOffset.UtcNow;
    var cert = req.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(13));
    Console.WriteLine($"[WebTransport] cert SHA-256: {Convert.ToHexString(cert.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256))}");
    return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(cert.Export(
        System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12), null);
}
```

(If `X509SubjectAlternativeNameBuilder.Also` extension is unavailable, build the SAN with a local variable — the `Also` call is illustrative of adding a single `localhost` DNS SAN.)

- [ ] **Step 4: Verify the endpoint accepts a session (deferred to Task 5's external client)**

Run: build only — `dotnet build SharpMUSH.ConnectionServer`.
Expected: compiles. Live acceptance is proven in Task 5.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.ConnectionServer/ProtocolHandlers/WebTransport*.cs SharpMUSH.ConnectionServer/Program.cs
git commit -m "feat: WebTransport /wt endpoint + bidi-stream adapter (flagged)"
```

---

## Task 5: External migration proof (go/no-go gate)

**Files:**
- Create: `test-clients/webtransport-migration-proof.mjs`
- Create: `test-clients/README.md`

**Interfaces:**
- Consumes: running ConnectionServer with `WebTransport:Enabled=true` and the logged cert hash.
- Produces: a manual pass/fail on whether experimental Kestrel WT interoperates with a real WT client AND survives a source-port rebind (NAT-rebind-style migration).

**This is the gate.** If it fails to interoperate, STOP and report: the client-side tasks (7–9) are not worth building until Kestrel WT talks to a real client. WS fallback already covers users.

- [ ] **Step 1: Write the proof client**

```javascript
// test-clients/webtransport-migration-proof.mjs
// Requires: npm i @fails-components/webtransport
// Usage: node webtransport-migration-proof.mjs https://localhost:PORT/wt <CERT_SHA256_HEX>
import { WebTransport } from '@fails-components/webtransport';

const [url, certHex] = process.argv.slice(2);
const certHash = Uint8Array.from(certHex.match(/../g).map(h => parseInt(h, 16)));

function frame(str) {
  const payload = new TextEncoder().encode(str);
  const buf = new Uint8Array(4 + payload.length);
  new DataView(buf.buffer).setUint32(0, payload.length, false); // big-endian
  buf.set(payload, 4);
  return buf;
}

const wt = new WebTransport(url, { serverCertificateHashes: [{ algorithm: 'sha-256', value: certHash }] });
await wt.ready;
console.log('session ready');
const stream = await wt.createBidirectionalStream();
const writer = stream.writable.getWriter();
const reader = stream.readable.getReader();

await writer.write(frame('look'));
console.log('sent "look"; awaiting output...');
const { value } = await reader.read();
console.log('received', value?.length, 'bytes — server responded, interop OK');

// Migration proof: @fails-components exposes no direct rebind API; document manual step.
console.log('MIGRATION: change network path (or run behind a NAT that rebinds) and send again:');
await new Promise(r => setTimeout(r, 15000));
await writer.write(frame('look'));
const again = await reader.read();
console.log(again.value ? 'output after path change — MIGRATION SURVIVED' : 'no output — migration failed');
await wt.close();
```

- [ ] **Step 2: Run the proof**

Run: start server with `WebTransport__Enabled=true`, copy the logged cert SHA-256, then
`node test-clients/webtransport-migration-proof.mjs https://localhost:<port>/wt <hash>`
Expected: "server responded, interop OK". For migration, exercise a NAT rebind (e.g. run the client через a userspace NAT or wait past the NAT UDP timeout) and confirm "MIGRATION SURVIVED".

- [ ] **Step 3: Record the result in test-clients/README.md and commit**

```bash
git add test-clients/
git commit -m "test: external WebTransport migration proof client (manual gate)"
```

**Decision point:** interop PASS → continue to Task 6. Interop FAIL → stop, report, keep WS-only (flag off).

---

## Task 6: Client `ITransportClient` abstraction

**Files:**
- Create: `SharpMUSH.Client/Services/ITransportClient.cs`
- Modify: `SharpMUSH.Client/Services/WebSocketClientService.cs`

**Interfaces:**
- Produces: `interface ITransportClient : IAsyncDisposable { string Kind {get;} bool IsConnected {get;} event EventHandler<string>? MessageReceived; event EventHandler<bool>? ConnectionStateChanged; Task ConnectAsync(string uri); Task SendAsync(string message); Task DisconnectAsync(); void ClearSendBuffer(); }`

- [ ] **Step 1: Create the interface**

```csharp
// SharpMUSH.Client/Services/ITransportClient.cs
namespace SharpMUSH.Client.Services;

/// <summary>
/// Transport-agnostic client channel to the ConnectionServer terminal endpoint.
/// Implemented by WebSocket and WebTransport clients so the rest of the app is unaware
/// of which transport is active.
/// </summary>
public interface ITransportClient : IAsyncDisposable
{
    string Kind { get; }                                   // "websocket" | "webtransport"
    bool IsConnected { get; }
    event EventHandler<string>? MessageReceived;
    event EventHandler<bool>? ConnectionStateChanged;      // true = connected
    Task ConnectAsync(string uri);
    Task SendAsync(string message);
    Task DisconnectAsync();
    void ClearSendBuffer();
}
```

- [ ] **Step 2: Make `WebSocketClientService` implement it**

Change the class declaration to `public class WebSocketClientService : IWebSocketClientService, ITransportClient`, add `public string Kind => "websocket";`, and add a `ConnectionStateChanged` raise (`bool`) alongside the existing `WebSocketState` event (map Open→true, else false). Keep the existing `IWebSocketClientService` members for back-compat.

- [ ] **Step 3: Build**

Run: `dotnet build SharpMUSH.Client`
Expected: compiles.

- [ ] **Step 4: Commit**

```bash
git add SharpMUSH.Client/Services/ITransportClient.cs SharpMUSH.Client/Services/WebSocketClientService.cs
git commit -m "refactor: introduce ITransportClient; WebSocketClientService implements it"
```

---

## Task 7: Browser WebTransport JS interop module

**Files:**
- Create: `SharpMUSH.Client/wwwroot/js/webtransport.js`

**Interfaces:**
- Produces globals: `window.sharpWebTransport = { isSupported(), connect(url, certHashHex, dotNetRef), send(id, text), close(id) }`. On each inbound frame calls `dotNetRef.invokeMethodAsync('OnFrame', text)`; on close calls `OnClosed`.

- [ ] **Step 1: Implement the module (length-prefix framing mirror of FrameCodec)**

```javascript
// SharpMUSH.Client/wwwroot/js/webtransport.js
const sessions = new Map();

function frame(text) {
  const payload = new TextEncoder().encode(text);
  const buf = new Uint8Array(4 + payload.length);
  new DataView(buf.buffer).setUint32(0, payload.length, false);
  buf.set(payload, 4);
  return buf;
}

window.sharpWebTransport = {
  isSupported: () => typeof WebTransport !== 'undefined',

  connect: async (url, certHashHex, dotNetRef) => {
    const opts = certHashHex
      ? { serverCertificateHashes: [{ algorithm: 'sha-256',
          value: Uint8Array.from(certHashHex.match(/../g).map(h => parseInt(h, 16))) }] }
      : {};
    const wt = new WebTransport(url, opts);
    await wt.ready;
    const stream = await wt.createBidirectionalStream();
    const writer = stream.writable.getWriter();
    const id = crypto.randomUUID();
    sessions.set(id, { wt, writer });

    (async () => {
      const reader = stream.readable.getReader();
      let buf = new Uint8Array(0);
      try {
        for (;;) {
          const { value, done } = await reader.read();
          if (done) break;
          const merged = new Uint8Array(buf.length + value.length);
          merged.set(buf); merged.set(value, buf.length); buf = merged;
          // drain complete length-prefixed frames
          while (buf.length >= 4) {
            const len = new DataView(buf.buffer, buf.byteOffset, 4).getUint32(0, false);
            if (buf.length < 4 + len) break;
            const text = new TextDecoder().decode(buf.subarray(4, 4 + len));
            buf = buf.subarray(4 + len);
            dotNetRef.invokeMethodAsync('OnFrame', text);
          }
        }
      } finally {
        dotNetRef.invokeMethodAsync('OnClosed');
      }
    })();

    return id;
  },

  send: async (id, text) => { const s = sessions.get(id); if (s) await s.writer.write(frame(text)); },
  close: (id) => { const s = sessions.get(id); if (s) { s.wt.close(); sessions.delete(id); } },
};
```

- [ ] **Step 2: Reference the script in `wwwroot/index.html`**

Add before the Blazor script: `<script src="js/webtransport.js"></script>`.

- [ ] **Step 3: Commit**

```bash
git add SharpMUSH.Client/wwwroot/js/webtransport.js SharpMUSH.Client/wwwroot/index.html
git commit -m "feat: browser WebTransport JS interop module (length-prefix framing)"
```

---

## Task 8: `WebTransportClientService`

**Files:**
- Create: `SharpMUSH.Client/Services/WebTransportClientService.cs`

**Interfaces:**
- Consumes: `IJSRuntime`, `window.sharpWebTransport`.
- Produces: `WebTransportClientService : ITransportClient` with `[JSInvokable] OnFrame(string)` / `OnClosed()`.

- [ ] **Step 1: Implement the service**

```csharp
// SharpMUSH.Client/Services/WebTransportClientService.cs
using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>WebTransport client over browser JS interop. Migration is handled by the browser QUIC stack.</summary>
public sealed class WebTransportClientService(IJSRuntime js, ILogger<WebTransportClientService> logger) : ITransportClient
{
    private DotNetObjectReference<WebTransportClientService>? _ref;
    private string? _sessionId;
    public string Kind => "webtransport";
    public bool IsConnected => _sessionId is not null;

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<bool>? ConnectionStateChanged;

    /// <param name="uri">Absolute https URL to /wt. Optional cert hash appended as "|<hex>" for dev.</param>
    public async Task ConnectAsync(string uri)
    {
        var parts = uri.Split('|', 2);
        var url = parts[0];
        var certHash = parts.Length > 1 ? parts[1] : null;

        if (!await js.InvokeAsync<bool>("sharpWebTransport.isSupported"))
            throw new NotSupportedException("WebTransport is not available in this browser.");

        _ref = DotNetObjectReference.Create(this);
        _sessionId = await js.InvokeAsync<string>("sharpWebTransport.connect", url, certHash, _ref);
        ConnectionStateChanged?.Invoke(this, true);
        logger.LogInformation("WebTransport connected: {Url}", url);
    }

    public async Task SendAsync(string message)
    {
        if (_sessionId is not null)
            await js.InvokeVoidAsync("sharpWebTransport.send", _sessionId, message);
    }

    public async Task DisconnectAsync()
    {
        if (_sessionId is not null)
            await js.InvokeVoidAsync("sharpWebTransport.close", _sessionId);
        _sessionId = null;
        ConnectionStateChanged?.Invoke(this, false);
    }

    public void ClearSendBuffer() { /* WT has no client-side buffer in this spike */ }

    [JSInvokable] public void OnFrame(string text) => MessageReceived?.Invoke(this, text);
    [JSInvokable] public void OnClosed() { _sessionId = null; ConnectionStateChanged?.Invoke(this, false); }

    public async ValueTask DisposeAsync() { await DisconnectAsync(); _ref?.Dispose(); }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build SharpMUSH.Client` → compiles.

- [ ] **Step 3: Commit**

```bash
git add SharpMUSH.Client/Services/WebTransportClientService.cs
git commit -m "feat: WebTransportClientService over JS interop"
```

---

## Task 9: Transport negotiator + wiring

**Files:**
- Create: `SharpMUSH.Client/Services/TransportNegotiator.cs`
- Create: `SharpMUSH.Tests/ClientState/TransportNegotiatorTests.cs`
- Modify: `SharpMUSH.Client/Program.cs`

**Interfaces:**
- Consumes: `ITransportClient` implementations, config `WebTransport:Enabled`, `WebTransport:Url`.
- Produces: `TransportNegotiator.SelectAsync(string wsUri, string? wtUri) : Task<ITransportClient>` — tries WT (if enabled + uri + supported) with a bounded timeout, else returns a connected WS client.

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests/ClientState/TransportNegotiatorTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.ClientState;

public class TransportNegotiatorTests
{
    private sealed class StubClient(string kind, bool failConnect) : ITransportClient
    {
        public string Kind => kind;
        public bool IsConnected { get; private set; }
        public event EventHandler<string>? MessageReceived;
        public event EventHandler<bool>? ConnectionStateChanged;
        public Task ConnectAsync(string uri)
        {
            if (failConnect) throw new NotSupportedException();
            IsConnected = true; ConnectionStateChanged?.Invoke(this, true); return Task.CompletedTask;
        }
        public Task SendAsync(string m) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public void ClearSendBuffer() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        _ = MessageReceived;
    }

    [Test]
    public async Task Falls_back_to_WebSocket_when_WebTransport_connect_throws()
    {
        var wt = new StubClient("webtransport", failConnect: true);
        var ws = new StubClient("websocket", failConnect: false);
        var negotiator = new TransportNegotiator(NullLogger<TransportNegotiator>.Instance, () => wt, () => ws);

        var chosen = await negotiator.SelectAsync("wss://h/ws", "https://h/wt");

        Assert.That(chosen.Kind, Is.EqualTo("websocket"));
        Assert.That(chosen.IsConnected, Is.True);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SharpMUSH.Tests --filter TransportNegotiatorTests`
Expected: FAIL — `TransportNegotiator` does not exist.

- [ ] **Step 3: Implement the negotiator**

```csharp
// SharpMUSH.Client/Services/TransportNegotiator.cs
namespace SharpMUSH.Client.Services;

/// <summary>Selects the terminal transport: WebTransport when available, else WebSocket.</summary>
public sealed class TransportNegotiator(
    ILogger<TransportNegotiator> logger,
    Func<ITransportClient> webTransportFactory,
    Func<ITransportClient> webSocketFactory)
{
    public async Task<ITransportClient> SelectAsync(string wsUri, string? wtUri)
    {
        if (!string.IsNullOrEmpty(wtUri))
        {
            var wt = webTransportFactory();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await wt.ConnectAsync(wtUri).WaitAsync(cts.Token);
                logger.LogInformation("Using WebTransport");
                return wt;
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex, "WebTransport unavailable; falling back to WebSocket");
                await wt.DisposeAsync();
            }
        }

        var ws = webSocketFactory();
        await ws.ConnectAsync(wsUri);
        return ws;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SharpMUSH.Tests --filter TransportNegotiatorTests` → PASS.

- [ ] **Step 5: Register in Program.cs**

```csharp
builder.Services.AddSingleton<WebTransportClientService>();
builder.Services.AddSingleton<TransportNegotiator>(sp => new TransportNegotiator(
    sp.GetRequiredService<ILogger<TransportNegotiator>>(),
    () => sp.GetRequiredService<WebTransportClientService>(),
    () => (ITransportClient)sp.GetRequiredService<IWebSocketClientService>()));
```

Wherever `PlayTerminalService` currently resolves the WS client to connect, resolve `TransportNegotiator` instead and call `SelectAsync(wsUri, config["WebTransport:Url"])`. Leave that call site change minimal.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Client/Services/TransportNegotiator.cs SharpMUSH.Tests/ClientState/TransportNegotiatorTests.cs SharpMUSH.Client/Program.cs
git commit -m "feat: transport negotiator with WebSocket fallback"
```

---

## Task 10: Output sequencing + client lastSeq tracking

**Files:**
- Modify: `SharpMUSH.ConnectionServer/Services/ConnectionServerService.cs` (add per-handle seq counter + tag outbound)
- Modify: `SharpMUSH.Client/Services/PlayTerminalService.cs` (track highest seq)
- Test: `SharpMUSH.Tests/ClientState/ConnectionStateServiceTests.cs` (extend)

**Interfaces:**
- Produces: outbound frames wrapped as `{"seq":<n>,"data":<original>}` for the terminal channel; client exposes `long LastSeq { get; }`.

- [ ] **Step 1: Write the failing test (server assigns increasing seq per handle)**

```csharp
[Test]
public async Task Outbound_frames_get_monotonic_per_handle_sequence()
{
    var svc = /* construct ConnectionServerService with fakes as existing tests do */;
    var seqs = new List<long>();
    await svc.RegisterAsync(1, "ip", "h", "websocket",
        data => { seqs.Add(SeqEnvelope.ReadSeq(data)); return Task.CompletedTask; },
        _ => Task.CompletedTask, () => System.Text.Encoding.UTF8, () => { });

    await svc.SendOutputAsync(1, "a");
    await svc.SendOutputAsync(1, "b");

    Assert.That(seqs, Is.EqualTo(new long[] { 1, 2 }));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test SharpMUSH.Tests --filter Outbound_frames_get_monotonic`
Expected: FAIL — `SendOutputAsync`/`SeqEnvelope` do not exist.

- [ ] **Step 3: Implement seq tagging**

Add to `ConnectionData` a `long OutSeq` counter. Add a helper `SeqEnvelope` (static, in ConnectionServer) that wraps `{"seq":n,"data":"..."}` and can read the seq back. Add `SendOutputAsync(long handle, string data)` that increments the handle's counter, wraps, and calls the registered output function. Route the existing output consumers (`WebSocketOutputConsumer`) through `SendOutputAsync` instead of calling `OutputFunction` directly.

```csharp
// SeqEnvelope.cs
using System.Text;
using System.Text.Json;
namespace SharpMUSH.ConnectionServer.Services;
public static class SeqEnvelope
{
    public static byte[] Wrap(long seq, string data)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { seq, data }));
    public static long ReadSeq(byte[] frame)
        => JsonDocument.Parse(frame).RootElement.GetProperty("seq").GetInt64();
}
```

- [ ] **Step 4: Client tracks highest seq**

In `PlayTerminalService`, when a frame arrives, parse the `seq`, update `LastSeq = Math.Max(LastSeq, seq)`, and render `data`. (Non-seq frames — e.g. legacy — render as-is.)

- [ ] **Step 5: Run tests**

Run: `dotnet test SharpMUSH.Tests --filter Outbound_frames_get_monotonic` → PASS.
Run: existing client/terminal tests → PASS.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.ConnectionServer/Services/ SharpMUSH.Client/Services/PlayTerminalService.cs SharpMUSH.Tests/
git commit -m "feat: per-handle output sequencing + client lastSeq tracking"
```

---

## Task 11: JetStream-backed replay buffer + resume token

**Files:**
- Modify: `SharpMUSH.ConnectionServer/Services/ConnectionServerService.cs`
- Create: `SharpMUSH.ConnectionServer/Services/ResumeTokenService.cs`

**Interfaces:**
- Consumes: existing NATS JetStream output stream, `IOttStore` pattern.
- Produces: `ResumeTokenService.Mint(long handle, long player) : string`, `TryResolve(string token, out long handle) : bool`; a bounded (last 30 s / 200 frames) per-handle output history kept in `ConnectionData` and mirrored to a short-TTL JetStream subject `replay.<handle>`.

- [ ] **Step 1: Write the failing test (replay returns frames after lastSeq)**

```csharp
[Test]
public async Task Replay_returns_only_frames_after_lastSeq()
{
    var svc = /* ConnectionServerService with fakes */;
    await svc.RegisterAsync(7, "ip", "h", "webtransport", _ => Task.CompletedTask, _ => Task.CompletedTask, () => System.Text.Encoding.UTF8, () => { });
    await svc.SendOutputAsync(7, "one");   // seq 1
    await svc.SendOutputAsync(7, "two");   // seq 2
    await svc.SendOutputAsync(7, "three"); // seq 3

    var replay = svc.GetReplayAfter(7, lastSeq: 1).Select(SeqEnvelope.ReadSeq).ToArray();

    Assert.That(replay, Is.EqualTo(new long[] { 2, 3 }));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test SharpMUSH.Tests --filter Replay_returns_only`
Expected: FAIL — `GetReplayAfter` does not exist.

- [ ] **Step 3: Implement bounded history + GetReplayAfter + ResumeTokenService**

Add a bounded `Queue<(long seq, byte[] frame)>` (cap 200, evict older than 30 s) to `ConnectionData`, appended in `SendOutputAsync`. `GetReplayAfter(handle, lastSeq)` returns buffered frames with `seq > lastSeq`. `ResumeTokenService` mints an opaque GUID bound to `(handle, player)` stored via the existing OTT store with a 30 s TTL, and resolves it back to a handle.

- [ ] **Step 4: Run tests**

Run: `dotnet test SharpMUSH.Tests --filter Replay_returns_only` → PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.ConnectionServer/Services/
git commit -m "feat: bounded per-handle replay buffer + resume token service"
```

---

## Task 12: Resume/replay handshake (fresh reconnect)

**Files:**
- Modify: `SharpMUSH.ConnectionServer/ProtocolHandlers/ConnectionPump.cs`
- Modify: `SharpMUSH.Client/Services/PlayTerminalService.cs`

**Interfaces:**
- Consumes: `ResumeTokenService`, `ConnectionServerService.GetReplayAfter`, grace-window survival from Task 11.
- Produces: on first inbound frame `{"resume":"<token>","lastSeq":<n>}`, the server rebinds to the surviving handle (within grace) and replays; else a fresh session. Client sends the resume frame first on any reconnect where it holds a token.

- [ ] **Step 1: Write the failing test (pump honors resume frame)**

```csharp
[Test]
public async Task Resume_frame_rebinds_and_replays_after_lastSeq()
{
    // Arrange a surviving handle 9 with buffered seqs 1..3 and a minted token "tok".
    // FakeTransport yields one frame: {"resume":"tok","lastSeq":1} then null.
    // Assert: transport.Sent contains replayed frames for seq 2 and 3, and no new handle is allocated.
}
```

(Fill the arrange using the same fakes as Task 1 plus a `ResumeTokenService` seeded with `tok → handle 9`.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test SharpMUSH.Tests --filter Resume_frame_rebinds`
Expected: FAIL — pump does not parse resume frames.

- [ ] **Step 3: Implement resume handling in the pump**

Before the receive loop, peek the first frame: if it parses as a resume envelope and the token resolves to a still-alive handle within grace, rebind this transport's output function to that handle, replay `GetReplayAfter(handle, lastSeq)`, and continue the loop against the existing handle (do not allocate/register a new one). Otherwise fall through to normal registration (Task 1 behavior).

- [ ] **Step 4: Client sends resume frame on reconnect**

In `PlayTerminalService`, persist the resume token (received from the server on connect) and, on any reconnect via the negotiator, send `{"resume":token,"lastSeq":LastSeq}` as the first frame before normal input.

- [ ] **Step 5: Run tests + full suite**

Run: `dotnet test SharpMUSH.Tests --filter Resume_frame_rebinds` → PASS.
Run: `dotnet test SharpMUSH.Tests SharpMUSH.Tests.Integration` → green.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.ConnectionServer/ProtocolHandlers/ConnectionPump.cs SharpMUSH.Client/Services/PlayTerminalService.cs SharpMUSH.Tests/
git commit -m "feat: resume/replay handshake for fresh-reconnect fallback path"
```

---

## Self-Review Notes

- **Spec coverage:** §1 seam → T1; §2 endpoint/config → T3,T4; §3 client → T6,T7,T8,T9; §4 replay → T10,T11,T12; §5 error/fallback → T9 (negotiator) + T3/T4 flag; §6 testing → unit tests in T1,T2,T9,T10,T11,T12 + external proof T5.
- **Framing:** length-prefix decision (Global Constraints) is mirrored in server `FrameCodec` (T2), server adapter (T4), and JS module (T7) — all big-endian uint32.
- **Feature flag:** `WebTransport:Enabled` gates csproj-independent runtime mapping (T3/T4) and client negotiation (T9). Off ⇒ WS-only, today's behavior.
- **Gate:** T5 is an explicit go/no-go before the expensive client tasks — consistent with design risk #1.
- **Type consistency:** `IDuplexTransport.ReceiveTextAsync` returns `string?` everywhere; `ITransportClient.ConnectionStateChanged` is `EventHandler<bool>` in T6/T8/T9; `SeqEnvelope.ReadSeq(byte[])` used in T10/T11/T12.

---

## As-Built Notes (2026-07-01 execution) — deltas from the spec above

Implemented on `spike/webtransport`; all new code builds clean under `TreatWarningsAsErrors` and is
covered by 20 new TUnit tests (14 ConnectionServer, 6 ClientState) with zero regressions in those
namespaces. Deviations from the plan as written, and why:

- **Output delegates are `Func<byte[], ValueTask>`** (not `Func<byte[], Task>`); `ConnectionPump`
  adapts via `new ValueTask(transport.SendAsync(...))`.
- **`ITransportClient` was trimmed** to the members `WebSocketClientService` already exposes
  (`Kind`, `IsConnected`, `MessageReceived`, `ConnectAsync/SendAsync/DisconnectAsync/ClearSendBuffer`,
  `IAsyncDisposable`). The planned `ConnectionStateChanged: EventHandler<bool>` was dropped to avoid
  clashing with the existing `EventHandler<WebSocketState>` event — no consumer needed it.
- **Sequencing/replay is isolated, not surgical.** Rather than rewiring every output consumer through
  a new `SendOutputAsync` on `ConnectionServerService`, the new logic lives in standalone units
  (`SeqEnvelope`, `TerminalReplayStore`, `ResumeTokenService`) and is wired at the pump behind
  `TerminalTransportOptions.SequencedOutput` (= `WebTransport:Enabled`). With the flag off (default),
  the pump path is byte-for-byte identical to before — so existing WS behavior and its bUnit/
  integration tests are untouched. `ReadSeq` is strict (throws on a frame with no `seq`).
- **Resume semantics (minimal):** a fresh reconnect with `{"resume":token,"lastSeq":n}` replays
  buffered frames after `n` from the old handle's buffer, then continues as a fresh connection
  (game-session survival is out of scope, per the design). Grace is the buffer's 30 s / 200-frame bound.
- **Kestrel:** WebTransport listens on a **dedicated HTTP/3 port** (`WebTransport:Port`, default 4203)
  so the existing WS/telnet bindings are untouched. Self-signed ECDSA P-256 dev cert (< 14 days) in
  `WebTransportDevCert`; SHA-256 logged for `serverCertificateHashes` pinning.
- **CA2252:** `EnablePreviewFeatures=true` plus a file-scoped `#pragma warning disable CA2252` in
  `WebTransportServer.cs` (the analyzer still fired for the preview WT APIs).
- **WebTransportTransport is stream-based** (`Stream input, Stream output, …, Action onClose`) so it is
  unit-testable with `MemoryStream`; `WebTransportServer` adapts the Kestrel `ConnectionContext`.

### Remaining (needs the running app / interactive gate)

- **Task 5 interop/migration proof** — cannot run in a headless sandbox (needs docker infra + HTTP/3 +
  a real WT client). Script + instructions in `test-clients/`. This is the go/no-go for the WT path.
- **Final terminal wiring** — `TransportNegotiator` is registered and unit-tested but the play terminal
  (`PlayTerminalService` / `IPlayWebSocketClientService`) still connects via its WebSocket client
  directly. Swapping it to `TransportNegotiator.SelectAsync(wsUri, wtUri)` is a small, single-site
  change intentionally deferred until the app can be run against the bUnit/live terminal, since that
  path is integration-tested infrastructure this session could not execute.
- **JS module** (`webtransport.js`) has no unit harness in-repo; it is exercised by the Task 5 gate.

### Interop gate result (empirical, 2026-07-01) — **NO-GO for current browsers**

Ran the gate against a real browser (assembled `libmsquic` 2.5.9 + `libnuma` + a stub `libxdp.so.1`
to get `QuicListener.IsSupported = true` on the dev box; minimal standalone host mounting the real
`WebTransportServer` + `ConnectionPump`; **Playwright-driven Chromium 149** using the browser
WebTransport API with `serverCertificateHashes`):

- **HTTP/3 itself works** end-to-end: `curl --http3 https://localhost:4433/` returns `wthost up`,
  and Kestrel serves H3 fine.
- **The WebTransport session handshake fails**: Chromium 149 reports
  `ERR_QUIC_PROTOCOL_ERROR` / "Opening handshake failed"; the request never reaches `AcceptAsync`
  (falls through as 404). This is at Kestrel's WebTransport-session negotiation layer, **not** the
  app code — the framing/pump/replay units all pass, and plain H3 proves the host wiring is correct.

**Conclusion:** experimental Kestrel WebTransport (draft-02) does **not** interoperate with current
Chromium (the design's risk #1, confirmed). Keep `WebTransport:Enabled=false`; WebSocket remains the
transport via the negotiator's fallback (zero cost, already wired). Re-run this gate when Kestrel's
WebTransport is finalized (tracked upstream for the .NET 11 timeframe) — the seam + client + replay
are all in place to flip it on the day the handshake matches.
