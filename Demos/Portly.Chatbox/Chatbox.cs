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

            await Client.ConnectAsync("localhost", port);
            await Client.SendPacketAsync(Packet.Create(ChatPacket.RequestUsername, username), true);
        }
    }
}
