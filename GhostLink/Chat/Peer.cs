using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GhostLink.Chat
{
    public class Peer
    {
        private readonly int _port;
        private TcpListener? _listener;

        public event Action<string>? MessageReceived;

        public Peer(int port)
        {
            _port = port;
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            Task.Run(async () =>
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClient(client));
                }
            });
        }

        private async Task HandleClient(TcpClient client)
        {
            using var stream = client.GetStream();
            var buffer = new byte[1024];
            int length = await stream.ReadAsync(buffer);
            string msg = Encoding.UTF8.GetString(buffer, 0, length);
            MessageReceived?.Invoke($"[Peer]: {msg}");
        }

        public async Task SendMessage(string ip, int port, string message)
        {
            using TcpClient client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse(ip), port);
            using var stream = client.GetStream();
            byte[] data = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(data);
        }
    }
}
