# Portly

Portly is a lightweight, secure .NET server-client architecture designed for small community servers. It features a decoupled design that separates core abstractions, security protocols, and infrastructure concerns.

## Key Features

- **TOFU Security**: Trust-On-First-Use model for seamless and secure connection establishment.
- **Packet Routing**: A built-in `PacketRouter` for handling multiple message types and destinations.
- **Tick System**: Synchronized `TickClock` for consistent simulation logic between server and client.
- **Traffic Protection**: Built-in rate limiting and replay protection.
- **Pluggable Architecture**: Supports multiple transport layers (e.g., TCP), serialization providers (MessagePack, JSON, XML), and logging providers.

## Architecture

- **Abstractions**: Core interfaces for clients, servers, transport, and serialization.
- **Protocol**: Packet definitions, routing, and length-prefixed processing.
- **Security**: Handshaking, AES encryption, and trust management.
- **Infrastructure**: Rate limiting, logging, configuration, and tick synchronization.
- **Runtime**: High-level `PortlyServer` and `PortlyClient` implementations.

## Quick Start

### Server
```csharp
var server = new PortlyServer();
await server.StartAsync();
```

### Client
```csharp
var client = new PortlyClient();
await client.ConnectAsync("127.0.0.1", 8080);
```

### Server
```csharp
var server = new PortlyServer();
await server.StartAsync();
```

### Client
```csharp
var client = new PortlyClient();
await client.ConnectAsync("127.0.0.1", 8080);
```

### Usage

#### Server
```csharp
var server = new PortlyServer();
await server.StartAsync();
```

### Client
```csharp
var client = new PortlyClient();
await client.ConnectAsync("127.0.0.1", 8080);
```

### Usage

#### Sending/Handling Packets
Register handlers on the `Router` to process specific `PacketType` identifiers:

```csharp
// Server
server.Router.Register(PacketType.MyAction, PacketExecutionMode.Immediate, async (client, packet) => 
{
    // Handle logic
});

// Client
client.Router.Register(PacketType.MyAction, PacketExecutionMode.Immediate, async (client, packet) => 
{
    // Handle logic
});
```

#### Example: Sending a Custom Packet
To send a packet, use the `SendPacketAsync` method. The `encrypt` parameter should be set based on your requirements (usually `true` for data packets).

```csharp
// Example payload (depends on your PacketType definition)
var payload = new MyCustomData { Value = 42 }; 
var packet = Packet.Create(PacketType.MyAction, payload);

// Server-side (send to specific client)
await server.SendToClientAsync(client, packet, encrypt: true);

// Client-side
await client.SendPacketAsync(packet, encrypt: true);
```

#### Example: Sending a Custom Packet
To send a packet, use the `SendPacketAsync` method. The `encrypt` parameter should be set based on your requirements (usually `true` for data packets).

```csharp
// Example payload (depends on your PacketType definition)
var payload = new MyCustomData { Value = 42 }; 
var packet = Packet.Create(PacketType.MyAction, payload);

// Server-side (send to specific client)
await server.SendToClientAsync(client, packet, encrypt: true);

// Client-side
await client.SendPacketAsync(packet, encrypt: true);
```

## Development

- **Build**: `dotnet build -c Release`
- **Test**: `dotnet test`

## License
See the LICENSE file for details.
