using Portly.Chatbox.Forms;
using Portly.Chatbox.Objects;
using Portly.Chatbox.Packets;
using Portly.Protocol;
using Portly.Runtime;
using System.Collections.Concurrent;
using System.Net;

namespace Portly.Chatbox
{
    public partial class ServerPanel : Form
    {
        private PortlyServer? _server;
        private Task? _serverTask;
        private TaskCompletionSource _startedTcs = new();

        private readonly ConcurrentDictionary<string, User> _connectedUsers = new(StringComparer.OrdinalIgnoreCase);

        private int _port;

        public ServerPanel()
        {
            InitializeComponent();
        }

        private void ServerPanel_Load(object sender, EventArgs e)
        {
            _server = new PortlyServer();
            _server.OnServerStarted += Server_OnServerStarted;

            foreach (var channel in Enum.GetValues<ChatChannel>())
                CmbChannels.Items.Add(channel);
            CmbChannels.SelectedIndex = 0;

            RegisterRoutes();
        }

        private void RegisterRoutes()
        {
            if (_server == null) return;

            // Register username registration
            _server.Router.Register(ChatPacket.RequestUsername,
                Protocol.PacketExecutionMode.Immediate,
                async (client, packet) =>
                {
                    var payload = packet.As<string>().Payload;
                    if (_connectedUsers.ContainsKey(payload))
                        return;

                    _connectedUsers[payload] = new User(client, payload);

                    await UsersListBox.InvokeAsync(() =>
                    {
                        if (((ChatChannel?)CmbChannels.SelectedItem) == ChatChannel.General)
                            UsersListBox.Items.Add(payload);
                    });
                });

            _server.Router.Register(ChatPacket.RequestLeave, Protocol.PacketExecutionMode.Immediate,
                async (client, packet) =>
                {
                    var username = _connectedUsers.FirstOrDefault(a => a.Value.Client.Id == client.Id).Key;
                    if (username == null) return;

                    _connectedUsers.TryRemove(username, out _);
                    await UsersListBox.InvokeAsync(() => UsersListBox.Items.Remove(username));
                });

            _server.Router.Register(ChatPacket.RequestChannelChange, Protocol.PacketExecutionMode.Immediate,
                async (client, packet) =>
                {
                    var user = _connectedUsers.FirstOrDefault(a => a.Value.Client.Id == client.Id).Value;
                    if (user == null) return;

                    var channelPayload = Enum.Parse<ChatChannel>(packet.As<string>().Payload);
                    user.Channel = channelPayload;

                    await UsersListBox.InvokeAsync(() =>
                    {
                        if (((ChatChannel?)CmbChannels.SelectedItem) != channelPayload)
                            UsersListBox.Items.Remove(user.Username);
                        else
                            UsersListBox.Items.Add(user.Username);
                    });
                });

            _server.Router.Register(ChatPacket.ChatMessage, Protocol.PacketExecutionMode.Immediate,
                async (client, packet) =>
                {
                    var user = _connectedUsers.FirstOrDefault(a => a.Value.Client.Id == client.Id).Value;
                    if (user == null) return;

                    var payload = packet.As<ChatMessage>().Payload;
                    payload.Username = user.Username;

                    await ChatHistoryListBox.InvokeAsync(() =>
                    {
                        if (CmbChannels.SelectedItem?.ToString() == user.Channel.ToString())
                            ChatHistoryListBox.Items.Add(payload);
                    });

                    // Distribute back to all clients
                    await _server.SendToAllClientsAsync(Packet.Create(ChatPacket.ChatMessage, payload), true);
                });
        }

        private void Server_OnServerStarted(object? sender, EventArgs e)
        {
            _startedTcs.TrySetResult();
        }

        private async void BtnServerControl_Click(object sender, EventArgs e)
        {
            if (_server == null) throw new Exception("Invalid state.");
            if (_serverTask != null)
            {
                if (_server.IsRunning)
                {
                    await _server.StopAsync();
                    await _serverTask;
                }

                _serverTask = null;
                LblServerStatus.Text = "Offline";
                LblServerStatus.ForeColor = Color.Crimson;
                BtnServerControl.Text = "Start Server";

                await UsersListBox.InvokeAsync(() =>
                {
                    UsersListBox.Items.Clear();
                });
            }
            else
            {
                _startedTcs = new();
                await StartAsync();
                LblServerStatus.Text = "Online";
                LblServerStatus.ForeColor = Color.ForestGreen;
                BtnServerControl.Text = "Stop Server";
            }
        }

        private async Task StartAsync(int? port = null)
        {
            if (_server == null) throw new Exception("Invalid state.");
            _serverTask = _server.StartAsync(port: port ?? 0);
            await _startedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _port = ((IPEndPoint)_server.LocalEndpoint!).Port;
        }

        private void BtnConnectNewUser_Click(object sender, EventArgs e)
        {
            if (_server == null || !_server.IsRunning)
            {
                MessageBox.Show("Server Offline.", "Start the server before connecting a new user.", MessageBoxButtons.OK);
                return;
            }

            // Input user name and send to chatbox
            const string lblUsername = "Select username:";
            var data = MultiInputDialog.Show(lblUsername, (lblUsername, null, null));
            if (data == null || data.Count == 0)
                return;
            var username = data[lblUsername];
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Invalid username.", "Invalid username.", MessageBoxButtons.OK);
                return;
            }

            // Validate with the server if username is available.
            if (_connectedUsers.ContainsKey(username))
            {
                MessageBox.Show("Username already taken.", "Username already taken.", MessageBoxButtons.OK);
                return;
            }

            var chatbox = new Chatbox();
            chatbox.Initialize(_port, username);
            chatbox.Show();
        }

        private async void BtnKickUser_Click(object sender, EventArgs e)
        {
            if (UsersListBox.SelectedItem is string selectedUser &&
                _connectedUsers.TryGetValue(selectedUser, out var user))
            {
                await user.Client.DisconnectAsync("Kicked by admin.");
                _connectedUsers.TryRemove(user.Username, out _);
                await UsersListBox.InvokeAsync(() =>
                {
                    UsersListBox.Items.Remove(selectedUser);
                });
            }
        }

        private void CmbChannels_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Show all users of the given channel
            UsersListBox.Items.Clear();
            foreach (var user in _connectedUsers
                .Where(a => a.Value.Channel == (ChatChannel?)CmbChannels.SelectedItem))
            {
                UsersListBox.Items.Add(user.Key);
            }
        }
    }
}
