# Portly

A lightweight server-client architecture built with .NET for secure, self-hosted community servers using Trust-On-First-Use (TOFU) identity verification and encrypted communication over TCP.

---

## Overview

**Purpose:**  
Portly provides a minimal yet robust framework for building networked applications that require secure client-server communication without pre-shared keys or certificates. It is designed specifically for small self-hosted community servers where users may connect from diverse environments (different OSes, browsers, etc.) and cannot be expected to install certificates beforehand.

**Main Features:**
- **TOFU Identity Verification** – Server identity is established on first connection; the fingerprint is stored locally so subsequent connections are validated against it.
- **Length-Prefixed Binary Protocol** – Efficient framing with 4-byte big-endian length prefix, replay protection via nonces and timestamps, and optional per-packet encryption.
- **Pluggable Serialization & Encryption** – Built-in MessagePack serialization provider; encryption providers can be swapped (default is AES-GCM).
- **Rate Limiting & IP Controls** – Per-client rate limiting with configurable thresholds; whitelist/blacklist support for IP addresses.
- **Keep-Alive Mechanism** – Automatic keep-alive packets and idle-timeout detection to prevent zombie connections.

**Primary Use Cases:**
- Self-hosted game servers, chat applications, or any small community service where users connect via TCP from heterogeneous clients.
- Situations requiring zero-trust setup (no prior certificates) but still needing strong cryptographic guarantees after the initial connection.

---

## Architecture

### High-Level Design

Portly follows a **layered architecture** with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────────┐
│  Runtime Layer (PortlyServer / PortlyClient)                │
│  - Orchestrates connections, handshakes, packet routing     │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Protocol Layer                                             │
│  LengthPrefixedPacketProtocol (IPacketProtocol)             │
│  - Reads/writes length-prefixed frames                      │
│  - Handles encryption, replay protection                    │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Security Layer                                             │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐   │
│  │ TrustClient  │    │TrustServer   │    │ AESEncryption│   │
│  └──────────────┘    └──────────────┘    └──────────────┘   │
│         ↓                    ↓                  ↓           │
│  TOFU fingerprint      ECDH key exchange     Session keys   │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  Transport Layer (TcpServerTransport / TcpClientTransport)  │
│  - Raw TCP socket handling, async I/O                       │
└─────────────────────────────────────────────────────────────┘
```

**Design Patterns:**
- **Strategy Pattern** – `IPacketProtocol`, `IEncryptionProvider`, and `ILogProvider` interfaces allow swapping implementations without changing core logic.
- **State Machine** – Both client and server maintain explicit states (`Disconnected → Connecting → Connected → Disconnecting`) with guarded transitions to prevent race conditions.
- **Composite/Aggregator Pattern** – Logging uses a composite provider; packet routing aggregates handlers per identifier.

### Layer Responsibilities

| Layer | Responsibility |
|-------|----------------|
| **Runtime** | Lifecycle management, event wiring, high-level APIs (`ConnectAsync`, `SendPacketAsync`). |
| **Protocol** | Framing, serialization/deserialization, encryption/decryption of payloads. |
| **Security** | TOFU identity storage, ECDH key exchange, signature verification. |
| **Transport** | TCP socket operations (connect, accept, read/write streams). |

### Dependencies

- **MessagePack** – Binary serialization library used by `IPacketSerializationProvider`.
- **.NET Standard libraries** (`System.Net.Sockets`, `System.Security.Cryptography`, etc.).

---

## Project Structure

```
Portly/
├── Abstractions/          # Interface definitions (IClient, IServerTransport, etc.)
│   └── *.cs               # Pure contracts; no implementation details.
│
├── Infrastructure/        # Cross-cutting concerns
│   ├── Configuration/     # ServerConfiguration, loading/saving from disk
│   ├── Logging/          # ILogProvider, ConsoleLogger, FileSystemLogger, CompositeLogger
│   └── KeepAliveManager.cs  # Manages keep-alive timers and idle-timeout detection
│
├── Protocol/             # Network protocol implementation
│   ├── Packet.cs         # Core packet type with MessagePack serialization
│   ├── PacketIdentifier.cs  # Enum-like identifier (IDs 0–100 reserved)
│   ├── PacketType.cs     # System packets: KeepAlive, LiteHandshake, SecureHandshake, Disconnect
│   ├── Processing/       # LengthPrefixedPacketProtocol – the framing layer
│   └── Serialization/    # MessagePackSerializationProvider
│
├── Runtime/              # Core server and client implementations
│   ├── PortlyServer.cs   # Server-side: accepts connections, runs handshakes, routes packets
│   ├── PortlyClient.cs   # Client-side: connects to a host, verifies identity, sends/receives
│   └── ServerClient.cs   # Wraps ITransportConnection for server use (state + keep-alive)
│
├── Security/             # Cryptographic operations
│   ├── Handshake/        # LiteHandshake, ClientHandshake, ServerHandshake DTOs
│   ├── Trust/            # TOFU fingerprint storage (TrustClient), key pair management (TrustServer)
│   └── Encryption/       # AESEncryptionProvider (AES-GCM), ECDH key exchange helper
│
├── Transport/           # TCP socket plumbing
│   ├── TcpServerTransport.cs  # Listens, accepts connections
│   ├── TcpClientTransport.cs  # Connects to remote host
│   └── TcpTransportConnection.cs  # Base stream wrapper with async read/write helpers
│
├── Utilities/           # Small helpers (ByteExtensions for combining byte arrays, VersionUtils)
│
└── Portly.slnx          # Solution file (includes the library project and test projects)
```

---

## Main Components

### Runtime Layer

#### `PortlyServer`

**Responsibility:**  
The server is the central hub that manages all incoming client connections. It:
- Listens on a TCP port defined in configuration.
- Performs two-stage handshakes (lite + secure) to establish trust and encryption.
- Routes packets based on their identifier using `PacketRouter`.
- Enforces rate limits, IP whitelisting/blacklisting, and max-connections-per-IP policies.

**Important Fields:**
| Field | Type | Purpose |
|-------|------|---------|
| `_clients` | `ConcurrentDictionary<Guid, ServerClient>` | Stores active client connections after handshake. |
| `_packetRouter` | `PacketRouter<IServerClient>` | Maps packet IDs to handler delegates. |
| `_keepAliveManager` | `KeepAliveManager<ServerClient>` | Tracks last-activity timestamps; triggers disconnect on timeout. |
| `_clientRateLimiter` | `ClientRateLimiter` | Enforces per-IP request rate limits and bans. |

**Public API:**
```csharp
public Task StartAsync(IPAddress? ip = null, int? port = null);
public Task StopAsync();
public bool IsConnected(Guid clientId, out IServerClient client);
public event EventHandler<IServerClient, Packet> OnPacketReceived;
public event EventHandler<IServerClient> OnClientConnected;
public event EventHandler<IServerClient> OnClientDisconnected;
```

**Lifecycle:**  
When `StartAsync` is called, the server binds to the specified endpoint and begins accepting connections. Each accepted connection spawns a `ServerClient` instance that runs its own async loop (handshake → packet reading). The server never blocks; all I/O is asynchronous.

#### `PortlyClient`

**Responsibility:**  
The client connects to a remote server, verifies its identity via TOFU, and then participates in the same two-stage handshake as the server side. After encryption is established, it can send/receive packets.

**Important Fields:**
| Field | Type | Purpose |
|-------|------|---------|
| `_state` | `int` (enum) | Tracks connection state machine. |
| `_stream` | `Stream?` | The underlying TCP stream after connect. |
| `_packetProtocol` | `IPacketProtocol` | Framing/serialization layer for this client. |

**Public API:**
```csharp
public Task ConnectAsync(string host, int port);
public Task DisconnectAsync();
public event EventHandler<Packet> OnPacketReceived;
public bool IsConnected { get; }
```

**State Machine:**  
The client enforces strict state transitions: `Disconnected → Connecting → Connected`. Any error during the handshake causes a transition back to `Connecting` (with cleanup) and then to `Disconnected`. This prevents sending packets on an invalid connection.

---

### Protocol Layer

#### `LengthPrefixedPacketProtocol`

**Responsibility:**  
Implements `IPacketProtocol`. It reads/writes frames consisting of:
1. A 4-byte big-endian length prefix (max packet size configurable).
2. The payload, which is a serialized `TransportPacket` containing:
   - `Payload`: the actual application data (serialized as a generic `Packet<T>`).
   - `Nonce`: random bytes for replay protection.
   - `CreationTimestampUtc`: when the packet was created.
   - `Encrypted`: flag indicating whether AES-GCM encryption was applied.

**Key Methods:**
- `ReadPacketsAsync(Stream, Func<Packet, Task>)` – loops until cancellation; handles idle-timeout between packets.
- `ReceiveSinglePacketAsync(Stream)` – reads one frame and returns a deserialized `Packet`.
- `SendPacketAsync(Stream, Packet, bool encrypt)` – serializes the packet, optionally encrypts it, then writes length + data.

**Replay Protection:**  
Each packet carries a unique nonce and timestamp. The protocol maintains a sliding window (`RequestsValidForMaxMinutes`) of seen nonces; any duplicate or out-of-window packet is rejected with an exception. This prevents attackers from re-sending old packets to replay actions.

---

### Security Layer

#### `TrustClient` (TOFU on the client side)

**Responsibility:**  
When a client first connects, it receives the server's public key. The client computes a SHA-256 fingerprint of that key and stores it in `known_servers.json`. On subsequent connections to the same host:port, it verifies that the received fingerprint matches the stored one. If they differ, the connection is rejected immediately.

**Storage:**  
The JSON file contains an array of `{Host, Port, Fingerprint}` objects keyed by `"host:port"`. The client loads this on construction and saves updates atomically.

#### `TrustServer` (TOFU on the server side)

**Responsibility:**  
Generates a server identity key pair (ECDSA P-256). On first run it creates `server_key.json`; thereafter it loads the existing private key. The public key is sent to clients during the secure handshake, and all outgoing signatures are verified against this stored key. This ensures that only the legitimate server can prove its identity.

---

### Transport Layer

#### `TcpServerTransport` / `TcpClientTransport`

These classes wrap .NET's `TcpListener` (server) or `TcpClient` (client). They implement:
- `OnClientAccepted` event for servers to handle new connections.
- Async read/write methods that delegate to the underlying stream.
- Proper disposal of sockets on shutdown.

---

## Execution Flow

### Server Startup Sequence

1. **Construction** – A `PortlyServer` instance is created (typically with default configuration). This loads `server_key.json` and initializes:
   - `_packetRouter`, `_keepAliveManager`, `_clientRateLimiter`.
   - The encryption provider factory (`AESEncryptionProvider`).
2. **StartAsync** – Binds to the configured IP/port, logs binding info, then calls `TcpServerTransport.StartAsync`. This begins listening for incoming TCP connections.
3. **Accept Loop** – When a client connects:
   - A new `ITransportConnection` is created and wrapped in a `ServerClient`.
   - The server enters the handshake phase (see below).
4. **Handshake** – Two stages:
   - *Lite Handshake*: Client sends a `LiteHandshake` packet with protocol name/version; server validates it matches its own implementation. If mismatch, reject.
   - *Secure Handshake*: Server sends its public key (`PacketType.SecureHandshake`). Client responds with a challenge + ephemeral key. Both sides perform ECDH to derive a shared secret, then sign the combined data (challenge + keys). The server verifies the client's signature; upon success it sets encryption on the protocol layer and marks the connection as authenticated.
5. **Normal Operation** – After handshake:
   - Keep-alive manager starts sending periodic empty packets (`PacketType.KeepAlive`).
   - Incoming frames are deserialized, replay-checked, decrypted (if needed), then routed via `_packetRouter`.
6. **Shutdown** – `StopAsync` cancels the CTS, closes the transport, sends a disconnect packet to each client, waits for their tasks to finish (with a 10-second timeout), and finally clears all state.

### Client Connection Sequence

1. **ConnectAsync** – Creates a new `TcpClientTransport`, connects to the given host:port, then performs the same two-stage handshake as described above.
2. Once encrypted, the client enters its read loop (`ReadPacketsAsync`) which mirrors the server's processing pipeline.
3. The client also runs a keep-alive timer; if idle timeout expires or an error occurs, it transitions to `Disconnecting` and cleans up resources.

---

## Data Flow

```
┌───────────────┐       ┌─────────────────────┐       ┌───────────────┐
│   Application │ ────> │ LengthPrefixedPacket│ ────> │  Transport    │
│    Code       │       │   Protocol Layer    │       │ (TCP Stream)  │
└───────────────┘       └─────────────────────┘       └───────────────┘
                                  ↓
                        ┌─────────────────────┐
                        │  Encryption/        │
                        │  Replay Protection  │
                        └─────────────────────┘
```

- **Input:** Application code creates a `Packet<T>` (e.g., sending a chat message). The packet is serialized by the MessagePack provider.
- **Transport Layer:** The protocol layer wraps it in a `TransportPacket` with a nonce and timestamp, optionally encrypts it, then writes 4-byte length + payload to the TCP stream.
- **Reception:** On the other side, the same steps are reversed: read length prefix → deserialize transport packet → decrypt (if needed) → route via `PacketRouter`.

---

## Important Classes

### `ServerClient` (internal)

Wraps an incoming `ITransportConnection`, manages its state (`Connecting → Connected → Disconnecting`), and holds the encrypted stream. It is created once per accepted connection and disposed when the client disconnects or times out.

**Public methods:**
- `SendPacketAsync(Packet, bool encrypt)` – writes a packet to the stream.
- `DisconnectInternalAsync()` – closes the underlying socket.

### `KeepAliveManager<T>`

A generic helper that tracks the last activity time for each connection and can invoke callbacks when idle timeout is exceeded. Used by both server and client to send keep-alive packets or trigger disconnects.

---

## APIs

Portly does not expose HTTP-style REST endpoints; its "API" consists of strongly-typed packet types defined in `PacketType.cs`:

| Packet Type | Purpose |
|-------------|---------|
| `KeepAlive` | No payload; signals the connection is alive (used by keep-alive manager). |
| `LiteHandshake` | Client announces protocol name and version. |
| `SecureHandshake` | Carries public keys, challenges, signatures for identity verification. |
| `Disconnect` | Signals graceful disconnection; may include a reason string. |

Developers can register custom handlers via the router:

```csharp
server.Router.Register(MyPacketType, async (client, packet) => {
    // handle incoming MyPacketType
});
```

---

## Configuration

Portly reads its settings from `ServerConfiguration`, which is loaded once at startup. The configuration supports two file formats via the serializer interface:

- **JSON** – default; files are placed in a configurable folder (or the current directory).
- **XML** – optional, provided by `XmlProvider`.

### Configuration Sections

| Section | Key Settings | Description |
|---------|--------------|-------------|
| **ConnectionSettings** | `Port`, `IpAddress`, `MaxConnections`, `ConnectTimeoutSeconds`, `WriteTimeoutSeconds`, `KeepAliveIntervalSeconds`, `IdleTimeoutSeconds` | Core TCP behavior: listening address, backpressure limits, timeouts. |
| **RateLimits** | `RequestsValidForMaxMinutes` | How long a request count is tracked for rate limiting. |
| **IpWhitelist / IpBlacklist** | Lists of IP addresses | Only whitelist IPs are allowed (if set); otherwise blacklist applies. |

The configuration also includes per-IP limits (`MaxConnectionsPerIp`) and the ability to ban an IP temporarily by storing an expiration timestamp in `IpBlacklist`.

---

## Dependencies

| Dependency | Why It's Used |
|------------|---------------|
| **MessagePack** | Provides fast binary serialization for packets. Portly uses it via `IPacketSerializationProvider` so that the core protocol remains agnostic to the format. |
| **.NET Standard libs** (`System.Net.Sockets`, `System.Security.Cryptography`) | Native TCP I/O and cryptographic primitives (ECDH, AES-GCM). No external crypto libraries are required. |

---

## Error Handling & Logging

- **Exceptions:** The runtime catches exceptions during handshake or packet processing and logs them at the appropriate level before transitioning to a disconnected state. System packets (`KeepAlive`, `Disconnect`) never throw; they are handled gracefully.
- **Logging:** All components use an `ILogProvider`. By default, `CompositeLogger` writes both to console and a file (via `FileSystemLogger`). Developers can inject custom log providers for structured logging or external systems.

---

## Extension Guide

### Adding New Packet Types

You cannot define new values in `PacketType` enum directly because it is internal to the library. Instead, create your own custom enum with values starting higher than existing ones:

```csharp
public enum CustomPacketType
{
    JoinChannel = 101,
    LeaveChannel = 102
}
```

Then create packets using:

```csharp
var packet = Packet.Create<PacketObject, CustomPacketType>(CustomPacketType.JoinChannel, packetObjectPayload);
```

The generic `Packet.Create<TPayload, TPacketType>` allows you to specify both the payload type and your custom packet type enum.

### Registering Handlers with the Router

After creating a custom packet, register a handler via the router:

```csharp
server.Router.Register<CustomPacketType>(async (client, payload) => {
    // handle incoming JoinChannel or LeaveChannel
});
```

The same pattern applies to clients—use `client.Router.Register<T>` to process incoming packets. This decouples packet handling from the core library and lets you define custom logic per packet type.

### Adding Custom Serialization

Implement `IPacketSerializationProvider`:

```csharp
public class MySerializer : IPacketSerializationProvider
{
    public T Deserialize<T>(ReadOnlyMemory<byte> bytes, CancellationToken token = default) where T : Packet
        => /* your logic */;

    public byte[] Serialize<T>(T data, CancellationToken token = default) where T : Packet
        => /* your logic */;
}
```

Then pass it to `PortlyServer` or `PortlyClient` via the constructor.

### Adding a New Transport (e.g., UDP)

Implement `IServerTransport` and `IClientTransport`. These interfaces define:
- Connection lifecycle events (`OnClientAccepted`, etc.).
- Async read/write methods returning `Task<T>` or `ValueTask<T>`.

Wire them into the runtime constructors. Note that TOFU identity verification is currently designed for TCP; extending to UDP would require additional design decisions (e.g., how to associate packets with a session).

---

## Build & Run

### Requirements

- .NET 10 SDK or later.
- No external build tools beyond `dotnet`.

### Commands

```bash
# Restore and build the solution
dotnet restore Portly.slnx
dotnet build Portly.slnx

# Run the example server (listens on configured port)
dotnet run --project Portly.ExampleServer

# In another terminal, connect as a client:
dotnet run --project Portly.ExampleClient
```

### Tests

The test project `Portly.Tests` contains unit and integration tests. To run them:

```bash
dotnet test Portly.Tests/Portly.Tests.csproj
```

---

## Glossary

| Term | Meaning |
|------|---------|
| **TOFU** (Trust-On-First-Use) | Identity is established on the first connection; subsequent connections are validated against a stored fingerprint. |
| **Lite Handshake** | The initial packet exchange where client and server agree on protocol name/version. |
| **Secure Handshake** | Exchange of public keys, challenge-response signing, and ECDH key derivation to establish encryption. |
| **Replay Protection** | Nonce + timestamp window that rejects duplicate or stale packets. |
| **Keep-Alive** | Empty packet sent periodically to detect idle connections. |

---

## Summary

Portly is a deliberately minimal server-client framework whose design philosophy centers on **composability**: every layer (transport, protocol, security) is expressed as an interface that can be swapped without touching the core runtime. This makes it easy to adapt for different environments or add new features while keeping the attack surface small. The TOFU model trades a one-time setup cost (storing a fingerprint and server key) for zero configuration on every client connection—a practical choice for community servers where users may not have administrative privileges.
