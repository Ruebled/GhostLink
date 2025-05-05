using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace GhostLink
{
    public class PeerConnection
    {
        public string DisplayName { get; }
        public string IP { get; }
        public bool IsConnected => client?.Connected ?? false;

        private TcpClient client;
        private NetworkStream stream;
        private RSACryptoServiceProvider ownRsa;
        private Aes aes;
        private bool keysExchanged = false;

        public event Action<string> OnMessageReceived;

        public PeerConnection(string ip, string displayName = "Unknown")
        {
            IP = ip;
            DisplayName = displayName;

            ownRsa = new RSACryptoServiceProvider(2048);
            aes = Aes.Create();
            aes.GenerateKey();
            aes.GenerateIV();
        }

        // Used for outgoing connection
        public async Task ConnectAsync(int port)
        {
            client = new TcpClient();
            await client.ConnectAsync(IP, port);
            stream = client.GetStream();
            await PerformKeyExchangeAsync(asInitiator: true);
            _ = Task.Run(ReceiveLoop);
        }

        // Used for incoming connection
        public async Task HandleIncomingConnectionAsync(TcpClient incomingClient)
        {
            client = incomingClient;
            stream = client.GetStream();
            await PerformKeyExchangeAsync(asInitiator: false);
            _ = Task.Run(ReceiveLoop);
        }

        private async Task PerformKeyExchangeAsync(bool asInitiator)
        {
            if (asInitiator)
            {
                // 1. Send own public key
                string publicKey = ownRsa.ToXmlString(false);
                byte[] publicKeyBytes = Encoding.UTF8.GetBytes(publicKey);
                await stream.WriteAsync(publicKeyBytes, 0, publicKeyBytes.Length);

                // 2. Receive peer public key
                byte[] buffer = new byte[4096];
                int received = await stream.ReadAsync(buffer, 0, buffer.Length);
                string peerPublicKeyXml = Encoding.UTF8.GetString(buffer, 0, received);
                RSACryptoServiceProvider peerRsa = new RSACryptoServiceProvider();
                peerRsa.FromXmlString(peerPublicKeyXml);

                // 3. Send AES key + IV encrypted with peer's public RSA key
                byte[] aesBundle = aes.Key.Concat(aes.IV).ToArray();
                byte[] encryptedBundle = peerRsa.Encrypt(aesBundle, false);
                await stream.WriteAsync(encryptedBundle, 0, encryptedBundle.Length);
            }
            else
            {
                // 1. Receive initiator's public key
                byte[] buffer = new byte[4096];
                int received = await stream.ReadAsync(buffer, 0, buffer.Length);
                string peerPublicKeyXml = Encoding.UTF8.GetString(buffer, 0, received);
                RSACryptoServiceProvider peerRsa = new RSACryptoServiceProvider();
                peerRsa.FromXmlString(peerPublicKeyXml);

                // 2. Send our own public key
                string ownPublicKey = ownRsa.ToXmlString(false);
                byte[] ownKeyBytes = Encoding.UTF8.GetBytes(ownPublicKey);
                await stream.WriteAsync(ownKeyBytes, 0, ownKeyBytes.Length);

                // 3. Receive AES key + IV
                received = await stream.ReadAsync(buffer, 0, buffer.Length);
                byte[] decrypted = ownRsa.Decrypt(buffer.Take(received).ToArray(), false);
                aes.Key = decrypted.Take(32).ToArray();
                aes.IV = decrypted.Skip(32).Take(16).ToArray();
            }

            keysExchanged = true;
        }

        private async Task ReceiveLoop()
        {
            byte[] buffer = new byte[4096];

            try
            {
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    byte[] encrypted = buffer[..bytesRead];
                    string message = DecryptMessage(encrypted);

                    OnMessageReceived?.Invoke(message);
                }
            }
            catch
            {
                OnMessageReceived?.Invoke("[Connection closed]");
            }
        }

        public async Task SendMessageAsync(string message)
        {
            if (!IsConnected || !keysExchanged) return;

            byte[] encrypted = EncryptMessage(message);
            await stream.WriteAsync(encrypted, 0, encrypted.Length);
        }

        private byte[] EncryptMessage(string message)
        {
            using var encryptor = aes.CreateEncryptor();
            byte[] plain = Encoding.UTF8.GetBytes(message);
            return encryptor.TransformFinalBlock(plain, 0, plain.Length);
        }

        private string DecryptMessage(byte[] encrypted)
        {
            using var decryptor = aes.CreateDecryptor();
            byte[] plain = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
            return Encoding.UTF8.GetString(plain);
        }

        public void Close()
        {
            stream?.Close();
            client?.Close();
        }
    }
}
