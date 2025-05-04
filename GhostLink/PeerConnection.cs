using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GhostLink
{
    public class PeerConnection
    {
        public string DisplayName { get; }
        public string IP { get; }
        public bool IsConnected => client?.Connected ?? false;

        private TcpClient client;
        private NetworkStream stream;
        private RSACryptoServiceProvider rsa;
        private Aes aes;

        public event Action<string> OnMessageReceived;

        public PeerConnection(string ip, string displayName = "Unknown")
        {
            IP = ip;
            DisplayName = displayName;

            rsa = new RSACryptoServiceProvider(2048);
            aes = Aes.Create();
            aes.GenerateKey();
            aes.GenerateIV();
        }

        public async Task ConnectAsync(int port)
        {
            client = new TcpClient();
            await client.ConnectAsync(IP, port);
            stream = client.GetStream();

            await ExchangeKeysAsync();
            _ = Task.Run(ReceiveLoop);
        }

        private async Task ExchangeKeysAsync()
        {
            // Step 1: Send public RSA key
            string publicKey = rsa.ToXmlString(false);
            byte[] publicKeyBytes = Encoding.UTF8.GetBytes(publicKey);
            await stream.WriteAsync(publicKeyBytes, 0, publicKeyBytes.Length);

            // Step 2: Receive peer's public RSA key
            byte[] buffer = new byte[2048];
            int read = await stream.ReadAsync(buffer, 0, buffer.Length);
            string peerPublicKey = Encoding.UTF8.GetString(buffer, 0, read);

            RSACryptoServiceProvider peerRsa = new RSACryptoServiceProvider();
            peerRsa.FromXmlString(peerPublicKey);

            // Step 3: Send AES key encrypted with peer's RSA key
            using (var ms = new MemoryStream())
            {
                ms.Write(aes.Key, 0, aes.Key.Length);
                ms.Write(aes.IV, 0, aes.IV.Length);
                byte[] encryptedAesKey = peerRsa.Encrypt(ms.ToArray(), false);
                await stream.WriteAsync(encryptedAesKey, 0, encryptedAesKey.Length);
            }
        }

        private async Task ReceiveLoop()
        {
            byte[] buffer = new byte[2048];

            while (true)
            {
                try
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0) break;

                    byte[] encrypted = new byte[read];
                    Array.Copy(buffer, encrypted, read);

                    string message = DecryptMessage(encrypted);
                    OnMessageReceived?.Invoke(message);
                }
                catch
                {
                    break;
                }
            }
        }

        public async Task SendMessageAsync(string message)
        {
            if (!IsConnected) return;

            byte[] encrypted = EncryptMessage(message);
            await stream.WriteAsync(encrypted, 0, encrypted.Length);
        }

        private byte[] EncryptMessage(string message)
        {
            using (var encryptor = aes.CreateEncryptor())
            {
                byte[] plain = Encoding.UTF8.GetBytes(message);
                return encryptor.TransformFinalBlock(plain, 0, plain.Length);
            }
        }

        private string DecryptMessage(byte[] encrypted)
        {
            using (var decryptor = aes.CreateDecryptor())
            {
                byte[] decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                return Encoding.UTF8.GetString(decrypted);
            }
        }

        public void Close()
        {
            client?.Close();
        }
    }
}
