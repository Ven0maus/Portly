using Portly.Chatbox.Objects;
using Portly.Chatbox.Packets;
using Portly.Protocol;
using Portly.Runtime;

namespace Portly.Chatbox
{
    public partial class Chatbox : Form
    {
        private int _port;
        private string _username;

        public PortlyClient Client { get; } = new PortlyClient();

        public Chatbox()
        {
            InitializeComponent();
        }

        public async void Initialize(int port, string username)
        {
            _port = port;
            _username = username;

            foreach (var channel in Enum.GetValues<ChatChannel>())
                CmbChannels.Items.Add(channel);
            CmbChannels.SelectedIndex = 0;

            Client.OnDisconnected += Client_OnDisconnected;
            RegisterRoutes();

            await Client.ConnectAsync("localhost", port);
            await Client.SendPacketAsync(Packet.Create(ChatPacket.RequestUsername, username), true);
        }

        private void RegisterRoutes()
        {
            Client.Router.Register(ChatPacket.RequestChannelChange, PacketExecutionMode.Immediate,
                (client, packet) =>
                {
                    var payload = packet.As<List<string>>().Payload;
                    UsersListBox.Invoke(() =>
                    {
                        UsersListBox.Items.Clear();
                        foreach (var username in payload)
                            UsersListBox.Items.Add(username);
                    });
                    return Task.CompletedTask;
                });

            Client.Router.Register(ChatPacket.ChatMessage, PacketExecutionMode.Immediate,
                (client, packet) =>
                {
                    var payload = packet.As<ChatMessage>().Payload;

                    ChatListBox.Invoke(() =>
                    {
                        ChatListBox.Items.Add(payload);
                    });

                    return Task.CompletedTask;
                });
        }

        private void Client_OnDisconnected(object? sender, EventArgs e)
        {
            Close();
        }

        private async void Chatbox_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!Client.IsConnected) return;
            await Client.SendPacketAsync(Packet.Create<string?, ChatPacket>(ChatPacket.RequestLeave, null), false);
            await Client.DisconnectAsync();
        }

        private async void CmbChannels_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!Client.IsConnected) return;
            await Client.SendPacketAsync(
                Packet.Create<string?, ChatPacket>(
                    ChatPacket.RequestChannelChange,
                    CmbChannels.SelectedItem?.ToString() ?? ChatChannel.General.ToString()),
                false);
            UsersListBox.Items.Clear();
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TextInputBox.Text)) return;
            await Client.SendPacketAsync(Packet.Create(ChatPacket.ChatMessage, new ChatMessage { Message = TextInputBox.Text }), true);
            await InvokeAsync(() => TextInputBox.Text = string.Empty);
        }
    }
}
