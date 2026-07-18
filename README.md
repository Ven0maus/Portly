# Portly

Portly is a lightweight, secure .NET server-client architecture designed for small community servers. It features a decoupled design that separates core abstractions, security protocols, and infrastructure concerns.

## Key Features

- **TOFU Security**: Trust-On-First-Use model for seamless and secure connection establishment
- **Packet Routing**: Built-in router for handling multiple message types and destinations
- **Tick System**: Synchronized `TickClock` for consistent simulation logic between server and client
- **Traffic Protection**: Rate limiting, replay protection, and IP whitelist/blacklist support
- **Pluggable Architecture**: Supports TCP transport, MessagePack serialization, AES encryption, and custom logging providers

## Architecture

Portly is organized into five layers:

| Layer | Purpose |
|-------|---------|
| **Abstractions** | Core interfaces for clients, servers, transport connections, and packet handling |
| **Protocol** | Packet definitions (`Packet`, `PacketIdentifier`), routing logic, and length-prefixed binary processing |
| **Security** | Handshake protocols (lite and secure), ECDH key exchange, AES encryption, and server identity verification |
| **Infrastructure** | Rate limiters, logging providers, configuration management, and tick synchronization |
| **Runtime** | High-level `PortlyServer` and `PortlyClient` implementations that wire everything together |

## Quick Start

### Starting a Server

```csharp
using Portly.Runtime;

var server = new PortlyServer();
await server.StartAsync(); // Listens on configured IP and port
```

The server will automatically:
- Accept incoming TCP connections
- Perform the TOFU handshake to establish trust
- Begin processing packets from connected clients

### Connecting a Client

```csharp
using Portly.Runtime;

var client = new PortlyClient();
await client.ConnectAsync("127.0.0.1", 8080);
```

The client will:
- Connect to the server's endpoint
- Complete the handshake (including secure key exchange)
- Begin sending and receiving packets

### Sending Packets

#### Server-side

The `Router` is used to register handlers for **incoming** packets. When a client sends a packet, Portly routes it based on its identifier type:

```csharp
// Register a handler for incoming MyAction packets
server.Router.Register(PacketType.MyAction, PacketExecutionMode.Immediate, async (client, packet) => 
{
    // Handle the incoming packet from this specific client
});

// Packets are automatically routed as they arrive if their identifier type is registered.
```

#### Client-side

The client also has a `Router` for handling **incoming** packets from the server:

```csharp
client.Router.Register(PacketType.MyAction, PacketExecutionMode.Immediate, async (client, packet) => 
{
    // Handle incoming MyAction packet from the server
});
```

To send packets to the server or other clients, use `SendPacketAsync` / `SendToClientAsync`.

### Creating and Sending Custom Packets

Portly uses a generic `Packet<T>` type. Any object that MessagePack can serialize is valid as payload:

```csharp
// Define your packet data (must be MessagePack-serializable)
public record MyData(int Value, string Name);

var myPayload = new MyData(42, "example");

// Create a packet with your own enum value
// IMPORTANT: Use an ID higher than the built-in system packets.
// System packets are defined in the portly PacketType enum.
// Define your custom type in your project's namespace
public enum MyPacketTypes { /* ... */ CustomCommand = 100 }

var packet = Packet.Create(MyPacketTypes.CustomCommand, myPayload);

// Send to all clients (e.g., for a broadcast)
await server.SendToAllClientsAsync(packet, encrypt: true);

// Or send to specific clients
await server.SendToClientAsync(client, packet, encrypt: true);
```

## Core Concepts

### `Packet` and `PacketIdentifier`

- **Purpose**: The fundamental unit of communication in Portly. Each packet has a type identifier and payload.
- **When to use**: Every network message you send or receive is wrapped in a `Packet<T>`.
- **Related APIs**: `Packet.Create()`, `Packet.As<T>()`, `Router.Register()`

### `TickClock`

- **Purpose**: Provides synchronized time across server and client for deterministic simulation.
- **When to use**: If your game logic or state machine depends on consistent tick timing, access via `server.Clock`.
- **Related APIs**: `OnTick` event, `Tick()` method (manual ticks when configured)

### `Router`

- **Purpose**: Routes incoming packets to registered handlers based on their type identifier.
- **When to use**: Register handlers here for any packet types you want to automatically route to a handler.
- **Related Parameters**: `PacketExecutionMode.Immediate`, `PacketExecutionMode.Tick`

### Server Events

| Event | Fires When |
|-------|------------|
| `OnServerStarted` | The transport begins accepting connections |
| `OnServerStopped` | The server shuts down |
| `OnClientConnected` | A new client completes the handshake |
| `OnClientDisconnected` | A client disconnects (gracefully or forcibly) |
| `OnPacketReceived` | Any packet arrives from a connected client |

### Client Events

| Event | Fires When |
|-------|------------|
| `OnConnected` | Handshake with server succeeded |
| `OnDisconnected` | Connection to server closed |
| `OnPacketReceived` | A packet was received from the server |

## Usage Guide

### Basic Server Lifecycle

```csharp
var server = new PortlyServer();

// Optional: customize before starting
server.Configuration.ConnectionSettings.Port = 8080;

await server.StartAsync();

try 
{
    // Your application logic runs here...
    // Packets will be routed automatically as they arrive
    
    await Task.Delay(TimeSpan.FromMinutes(1));
}
finally 
{
    await server.StopAsync();
}
```

### Basic Client Lifecycle

```csharp
var client = new PortlyClient();

await client.ConnectAsync("127.0.0.1", 8080);

try 
{
    // Register handlers for incoming packets
    client.Router.Register(PacketType.MyAction, PacketExecutionMode.Immediate, async (client, packet) => 
    {
        var data = packet.As<MyData>();
        Console.WriteLine($"Received: {data.Value}");
    });
    
    // Send packets to the server
    var myPayload = new MyData(10);
    await client.SendPacketAsync(Packet.Create(PacketType.MyAction, myPayload), encrypt: true);
}
finally 
{
    await client.DisconnectAsync();
}
```

### Advanced Usage: Tick-Based Processing

When `TickRate` is configured in settings (or manually via `server.Tick()`), packets can be queued for the next tick instead of processed immediately. This is useful for deterministic simulation or batching updates.

```csharp
// Register a tick-based handler
server.Router.Register(
    PacketType.MyAction, 
    PacketExecutionMode.Tick,  // Defers execution until next tick
    async (client, packet) => 
    {
        // This runs at the start of the next server tick
        await HandleMyActionAsync(client, packet);
    }
);
```

### Sending to Specific Clients vs All Clients

- **`SendToAllClientsAsync()`**: Broadcast a packet to every connected client. Use for announcements or global events.
- **`SendToClientAsync(IServerClient)`**: Send directly to one specific client (requires you have the `IServerClient` reference, typically from an event handler).
- **`SendToClientsAsync(...params IServerClient[])`**: Send to a subset of clients by passing multiple references.

## Configuration

Portly reads configuration from a JSON file located in its runtime folder (default: user's AppData or project output directory). The `ServerConfiguration` object exposes these settings:

| Setting | Description |
|---------|-------------|
| `ConnectionSettings.Port` | TCP port the server listens on |
| `ConnectionSettings.IpAddress` | IP address to bind (or leave empty for all interfaces) |
| `ConnectionSettings.MaxConnections` | Maximum concurrent client connections |
| `ConnectionSettings.MaxConnectionsPerIp` | Max connections allowed from a single IP |
| `ConnectionSettings.TickRate` | Ticks per second (0 = disabled, uses real-time instead) |
| `ConnectionSettings.ConnectTimeoutSeconds` | Timeout for handshake phase |
| `KeepAliveIntervalSeconds` / `KeepAliveTimeoutSeconds` | Connection keep-alive settings |

You can also set IP whitelist/blacklist to restrict which addresses may connect.

## Extending the Library

### Adding Custom Packet Types

1. Define a new enum in your project for your packet types
2. Create a record or class for your packet payload
3. Register a handler via `Router.Register()` with your chosen execution mode

Example:

```csharp
// 1. Add to PacketType enum
public enum CustomPacketType { /* ... */ MyCustomCommand }

// 2. Define payload (must be MessagePack-serializable)
public record MyCustomCommand(string Action, int Parameter);

// 3. Register handler on server
server.Router.Register(
    CustomPacketType.MyCustomCommand, 
    PacketExecutionMode.Immediate,
    async (client, packet) => 
    {
        var cmd = packet.As<MyCustomCommand>();
        // Process command...
    }
);
```

### Custom Serialization Provider

Portly uses MessagePack by default. To switch:

1. Implement `IPacketSerializationProvider` in your project
2. Pass it to the `PortlyServer` constructor (or configure via DI if using dependency injection)

### Custom Logging

Implement `ILogProvider` and pass it when constructing `PortlyServer`. All internal logging calls will use your provider instead of the default console logger.

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| "Handshake timed out" | Client didn't send a valid lite handshake within timeout | Increase `ConnectTimeoutSeconds` in config, or ensure client sends the required packets |
| "IP is not whitelisted" | Server has an IP whitelist and the connecting address isn't included | Add the IP to `Configuration.IpWhitelist` before starting |
| "Rate limit exceeded" | Too many bytes received from a single IP in the configured window | The client will be disconnected; reduce incoming traffic or adjust rate limits |
| Packet handlers not firing | Handler wasn't registered, or packet type is reserved (system packets) | Verify `Router.Register()` was called before any packets arrive; use IDs > `100` for custom types |

## License

See the LICENSE file for details.
