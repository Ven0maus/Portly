namespace Portly.Chatbox
{
    public partial class Chatbox : Form
    {
        private int _port;

        public Chatbox()
        {
            InitializeComponent();
        }

        public void Initialize(int port)
        {
            _port = port;
        }
    }
}
