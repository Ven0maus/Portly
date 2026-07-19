using Portly.Abstractions;

namespace Portly.Chatbox.Objects
{
    internal record User
    {
        public IServerClient Client { get; }
        public string Username { get; }
        public ChatChannel Channel { get; set; }

        public User(IServerClient client, string username)
        {
            Client = client;
            Username = username;
        }
    }
}
