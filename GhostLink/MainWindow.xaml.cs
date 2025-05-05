using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GhostLink
{
    public partial class MainWindow : Window
    {
        private TcpListener listener;
        private bool isListening = false;
        private const int BufferSize = 1024;
        private const int ChatPort = 5005;
        private const int DiscoveryPort = 5051;

        private PeerConnection currentPeerConnection;

        private readonly Dictionary<IPAddress, DateTime> respondedPeers = new();
        private readonly HashSet<string> discoveredPeers = new();

        public MainWindow()
        {
            InitializeComponent();
            StartListening();
            StartDiscoveryListener();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Send_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void StartListening()
        {
            listener = new TcpListener(IPAddress.Any, ChatPort);
            listener.Start();
            isListening = true;

            Thread listenerThread = new Thread(ListenForClientsAsync) { IsBackground = true };
            listenerThread.Start();

            Dispatcher.Invoke(() => UpdateStatus($"Listening on port {ChatPort}..."));
        }

        private bool IsFromSelf(IPAddress address)
        {
            var localIPs = Dns.GetHostAddresses(Dns.GetHostName());
            return localIPs.Contains(address);
        }

        private void StartDiscoveryListener()
        {
            Thread discoveryThread = new Thread(() =>
            {
                try
                {
                    UdpClient udpListener = new UdpClient(DiscoveryPort);
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                    while (true)
                    {
                        byte[] received = udpListener.Receive(ref remoteEP);
                        string message = Encoding.UTF8.GetString(received);

                        if (IsFromSelf(remoteEP.Address))
                            continue;

                        if (message.Contains("GhostLink Discovery Request"))
                        {
                            if (!respondedPeers.ContainsKey(remoteEP.Address) ||
                                (DateTime.Now - respondedPeers[remoteEP.Address]).TotalSeconds > 60)
                            {
                                respondedPeers[remoteEP.Address] = DateTime.Now;

                                string username = Dispatcher.Invoke(() => UsernameTextBox?.Text ?? "Unknown");
                                string reply = $"GhostLink Response from {username}";
                                byte[] replyBytes = Encoding.UTF8.GetBytes(reply);

                                var responseEP = new IPEndPoint(remoteEP.Address, DiscoveryPort);
                                udpListener.Send(replyBytes, replyBytes.Length, responseEP);
                            }
                        }
                        else if (message.Contains("GhostLink Response from"))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                string peerName = message.Replace("GhostLink Response from ", "").Trim();
                                string display = $"{peerName} ({remoteEP.Address})";

                                if (!discoveredPeers.Contains(display))
                                {
                                    discoveredPeers.Add(display);
                                    PeerListBox.Items.Add(display);
                                    AddMessage($"[Discovery] {display} discovered.", true);
                                }
                            });
                        }
                    }
                }
                catch (SocketException ex)
                {
                    Dispatcher.Invoke(() => UpdateStatus($"Discovery listener error: {ex.Message}"));
                }
            })
            { IsBackground = true };

            discoveryThread.Start();
        }

        private async void ListenForClientsAsync()
        {
            while (isListening)
            {
                try
                {
                    var tcpClient = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(async () =>
                    {
                        var peer = new PeerConnection(((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address.ToString());
                        await peer.HandleIncomingConnectionAsync(tcpClient);
                        peer.OnMessageReceived += msg => Dispatcher.Invoke(() => AddMessage(msg));
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => UpdateStatus($"Listener error: {ex.Message}"));
                }
            }
        }

        private void AddMessage(string message, bool isOwnMessage = false)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string formatted = $"[{timestamp}] {message}";

            var textBlock = new TextBlock
            {
                Text = formatted,
                Margin = new Thickness(5, 2, 5, 2),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(isOwnMessage ? Color.FromRgb(144, 238, 144) : Colors.White)
            };

            ChatPanel.Children.Add(textBlock);

            if (VisualTreeHelper.GetParent(ChatPanel) is ScrollViewer scroll)
            {
                scroll.ScrollToEnd();
            }
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string messageText = InputBox.Text.Trim();
            string username = UsernameTextBox?.Text?.Trim() ?? "Unknown";

            if (string.IsNullOrEmpty(messageText)) return;

            if (currentPeerConnection == null || !currentPeerConnection.IsConnected)
            {
                MessageBox.Show("Not connected. Use 'Connect' first.");
                return;
            }

            string fullMessage = $"{username}: {messageText}";

            try
            {
                await currentPeerConnection.SendMessageAsync(fullMessage);
                AddMessage(fullMessage, true);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Send failed: {ex.Message}");
            }

            InputBox.Clear();
        }

        private void BroadcastDiscovery_Click(object sender, RoutedEventArgs e)
        {
            UdpClient udpClient = new UdpClient();
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            string discoveryMessage = $"GhostLink Discovery Request from {UsernameTextBox.Text}";
            byte[] data = Encoding.UTF8.GetBytes(discoveryMessage);

            try
            {
                udpClient.EnableBroadcast = true;
                udpClient.Send(data, data.Length, endPoint);
                UpdateStatus("Discovery message sent.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Discovery failed: {ex.Message}");
            }
            finally
            {
                udpClient.Close();
            }
        }

        private void StartChatWithSelectedPeer(object sender, RoutedEventArgs e)
        {
            if (PeerListBox.SelectedItem is string peerEntry)
            {
                string ip = ExtractIpFromDisplay(peerEntry);
                IpTextBox.Text = ip;

                ChatHeader.Text = $"Chatting with: {peerEntry}";
                SelectedPeerText.Text = peerEntry;
                UpdateStatus($"Chatting with {peerEntry}.");
            }
            else
            {
                UpdateStatus("No peer selected.");
            }
        }

        private string ExtractIpFromDisplay(string display)
        {
            int start = display.IndexOf('(');
            int end = display.IndexOf(')');
            if (start != -1 && end != -1 && end > start)
            {
                return display.Substring(start + 1, end - start - 1);
            }
            return display;
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (!IPAddress.TryParse(IpTextBox.Text, out var ip))
            {
                MessageBox.Show("Invalid IP address.");
                return;
            }

            UpdateStatus($"Attempting connection to {ip}...");

            string username = UsernameTextBox?.Text?.Trim() ?? "Unknown";
            string testMessage = $"{username} is online";

            currentPeerConnection = new PeerConnection(ip.ToString());
            currentPeerConnection.OnMessageReceived += msg => Dispatcher.Invoke(() => AddMessage(msg));

            try
            {
                await currentPeerConnection.ConnectAsync(ChatPort);
                await currentPeerConnection.SendMessageAsync(testMessage);
                AddMessage(testMessage, true);
                UpdateStatus($"Connected to {ip}.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Connection failed: {ex.Message}");
                currentPeerConnection = null;
            }
        }

        private void UpdateStatus(string text)
        {
            Dispatcher.Invoke(() => StatusText.Text = text);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            isListening = false;
            listener?.Stop();
        }
    }
}
