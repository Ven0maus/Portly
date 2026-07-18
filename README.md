# Portly
Portly is a lightweight server-client architecture designed for small, self-hosted community servers. It implements a **Trust-On-First-Use (TOFU)** security model, providing a balance between ease of use and secure communication for decentralized environments.

## Features
- **TOFU Security**: Seamless trust establishment on the first connection with persistent security.
- **Packet Routing**: Built-in `PacketRouter` to handle multiple message types and destinations on both client and server.
- **Rate Limiting**: Infrastructure for protecting servers from request flooding.
- **Replay Protection**: Safeguards against repeated packet injection.
- **Multi-Transport Support**: Pluggable transport layer (e.g., TCP).
- **Customizable Serialization**: Supports MessagePack and other serialization providers.

## Architecture
Portly is built on a decoupled architecture separating concerns into distinct layers:
- **Abstractions**: Defines core interfaces (`IClient`, `IServer`, `ITransportConnection`).
- **Protocol**: Handles packet definitions, routing, and serialization.
- **Security**: Manages handshakes, encryption (AES), and trust logic.
- **Infrastructure**: Provides cross-cutting concerns like Logging, Rate Limiting, and Tick Synchronization.
- **Runtime**: High-level `PortlyServer` and `PortlyClient` implementations.

## Installation
Portly is a .NET library. You can integrate it into your project via NuGet (or by referencing the project file):

```bash
dotnet add package Portly
```

## Quick Start
A basic setup involves initializing a server and a client.

**Server Example:**
```csharp
// The folder parameter is used to store trust keys
var server = new PortlyServer("trust_data"); 
await server.StartAsync();
```

**Client Example:**
```csharp
var client = new PortlyClient();
await client.ConnectAsync("127.0.0.1", 8080);
```

## Usage
### Sending Packets
Packets are routed based on `PacketIdentifier` and `PacketRoute`.
```csharp
var packet = Packet<MyData>.Create(PacketType.MyAction, new MyData(data));
await client.SendPacketAsync(packet, encrypt: true);
```

### Handling Packets
Both the `PortlyClient` and `PortlyServer` expose a `Router` property. You can register handlers for specific `PacketType` identifiers to automatically route incoming packets to the correct logic.

**Server Registration:**
```csharp
server.Router.Register(PacketType.MyAction, PacketExecutionMode.Immediate, async (client, packet) => 
{
    // Handle server-side logic
});
```

**Client Registration:**
```csharp
client.Router.Register(PacketType.MyAction, PacketExecutionMode.Immediate, async (client, packet) => 
{
    // Handle client-side logic
});
```

### Tick System
Portly includes a `TickClock` for synchronized simulation logic.
- **Server**: The `PortlyServer` runs a background tick loop at a configured frequency. You can subscribe to the `OnTick` event to execute logic every tick.
- **Client**: The `PortlyClient` synchronizes its `TickClock` with the server's clock during the handshake, ensuring both sides are aligned for time-sensitive operations.

## Configuration
Configuration can be managed via `ServerConfiguration` or `ClientConfiguration` objects. Key settings include:
- **Connection**: Host, port, and keep-alive settings.
- **Rate Limits**: Define thresholds for client requests.
- **Serialization**: Choose between `JsonProvider` or `MessagePackSerializationProvider`.
- **Logging**: Configure `LogProvider` (Console, File, or Composite).

## Development
### Build
```bash
dotnet build -c Release
```

### Test
```bash
dotnet test
```

### Contributing
1. Fork the repository.
2. Create a feature branch.
3. Ensure all tests pass.
4. Submit a Pull Request.

## License
[Check the LICENSE file for details]
