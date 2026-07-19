using Portly.Runtime;
using System.Net;

namespace Portly.Chatbox
{
    public partial class ServerPanel : Form
    {
        private PortlyServer? _server;
        private Task? _serverTask;
        private TaskCompletionSource _startedTcs = new();

        private int _port;

        public ServerPanel()
        {
            InitializeComponent();
        }

        private void ServerPanel_Load(object sender, EventArgs e)
        {
            _server = new PortlyServer();
            _server.OnServerStarted += Server_OnServerStarted;
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
                MessageBox.Show("Invalid username", "Invalid username", MessageBoxButtons.OK);
                return;
            }

            // TODO: Validate with the server if username is available.

            var chatbox = new Chatbox();
            chatbox.Initialize(_port);
            chatbox.Show();
        }
    }
}
