using Portly.Abstractions;
using Portly.Chatbox.Packets;
using Portly.Runtime;
using System.Net;

namespace Portly.Chatbox
{
    public partial class ServerPanel : Form
    {
        private PortlyServer? _server;
        private Task? _serverTask;
        private TaskCompletionSource _startedTcs = new();

        private readonly Dictionary<string, IServerClient> _connectedUsers = new(StringComparer.OrdinalIgnoreCase);

        private int _port;

        public ServerPanel()
        {
            InitializeComponent();
        }

        private void ServerPanel_Load(object sender, EventArgs e)
        {
            _server = new PortlyServer();
            _server.OnServerStarted += Server_OnServerStarted;

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

                    _connectedUsers[payload] = client;

                    await UsersListBox.InvokeAsync(() =>
                    {
                        UsersListBox.Items.Add(payload);
                    });
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
    }
}
