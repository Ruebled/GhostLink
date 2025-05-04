using System.Net;
using System.Net.Sockets;
using System.Text;
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
        private const int ChatPort = 5005;      // Listening port for chat messages
        private const int DiscoveryPort = 5051; // Port for discovery requests

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
            int port = ChatPort;

            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            isListening = true;

            Thread listenerThread = new Thread(ListenForClientsAsync)
            {
                IsBackground = true
            };
            listenerThread.Start();

            UpdateStatus($"Listening on port {port}...");
        }

        bool IsFromSelf(IPAddress address)
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
                    UdpClient udpListener = new UdpClient(DiscoveryPort); // Correct way
                    Console.WriteLine("UDP listener bound to port " + DiscoveryPort);

                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                    while (true)
                    {
                        byte[] received = udpListener.Receive(ref remoteEP);
                        string message = Encoding.UTF8.GetString(received);

                        if (IsFromSelf(remoteEP.Address))
                        {
                            // Skip self response
                            continue;
                        }

                        if (message.Contains("GhostLink Discovery Request"))
                        {
                            string username = "Unknown";
                            Dispatcher.Invoke(() =>
                            {
                                username = UsernameTextBox?.Text ?? "Unknown";
                            });

                            string reply = $"GhostLink Response from {username}";
                            byte[] replyBytes = Encoding.UTF8.GetBytes(reply);

                            var responseEP = new IPEndPoint(remoteEP.Address, DiscoveryPort);
                            udpListener.Send(replyBytes, replyBytes.Length, responseEP);

                        }
                        else if (message.Contains("GhostLink Response from"))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                string peerName = message.Replace("GhostLink Response from ", "").Trim();
                                string display = $"{peerName} ({remoteEP.Address})";

                                if (!PeerListBox.Items.Contains(display))
                                {
                                    PeerListBox.Items.Add(display);
                                    AddMessage($"[Discovery] {display} discovered.", isOwnMessage: true);
                                }
                            });
                        }
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[UDP] Discovery listener error: {ex.Message}");
                    Dispatcher.Invoke(() => UpdateStatus("Discovery listener error."));
                }
            })
            {
                IsBackground = true
            };

            discoveryThread.Start();
        }

        private async void ListenForClientsAsync()
        {
            while (isListening)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TCP] Listener error: {ex.Message}");
                    UpdateStatus("Listener error.");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                byte[] buffer = new byte[BufferSize];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Dispatcher.Invoke(() => AddMessage(message));
                }
            }
        }

        private void AddMessage(string message, bool isOwnMessage = false)
        {
            string formatted = message;
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            formatted = $"[{timestamp}] {message}";

            var textBlock = new TextBlock
            {
                Text = formatted,
                Margin = new Thickness(5, 2, 5, 2),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = isOwnMessage
                    ? new SolidColorBrush(Color.FromRgb(144, 238, 144))
                    : new SolidColorBrush(Colors.White)
            };

            ChatPanel.Children.Add(textBlock);

            if (VisualTreeHelper.GetParent(ChatPanel) is ScrollViewer scroll)
            {
                scroll.ScrollToEnd();
            }
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            string messageText = InputBox.Text.Trim();
            string username = UsernameTextBox?.Text.Trim();

            if (string.IsNullOrEmpty(messageText))
                return;

            if (!IPAddress.TryParse(IpTextBox.Text, out var ip))
            {
                MessageBox.Show("Invalid IP address.");
                return;
            }

            string fullMessage = string.IsNullOrEmpty(username) ? messageText : $"{username}: {messageText}";

            Task.Run(() =>
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        client.Connect(ip, ChatPort);
                        using (var stream = client.GetStream())
                        {
                            byte[] data = Encoding.UTF8.GetBytes(fullMessage);
                            stream.Write(data, 0, data.Length);
                        }
                    }

                    Dispatcher.Invoke(() => AddMessage(fullMessage, isOwnMessage: true));
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[TCP] Send error: {ex.Message}");
                    Dispatcher.Invoke(() => UpdateStatus("Send failed."));
                }
            });

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
                Console.WriteLine($"[UDP] Discovery error: {ex.Message}");
                UpdateStatus("Discovery failed.");
            }
            finally
            {
                udpClient.Close();
            }
        }

        private void StartChatWithSelectedPeer(object sender, RoutedEventArgs e)
        {
            if (PeerListBox.SelectedItem is string peerName)
            {
                ChatHeader.Text = $"Chatting with: {peerName}";
                SelectedPeerText.Text = peerName;
                UpdateStatus($"Chatting with {peerName}.");
            }
            else
            {
                UpdateStatus("No peer selected.");
            }
        }

        private void UpdateStatus(string text)
        {
            StatusText.Text = text;
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Manual connection initiated.");
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            isListening = false;
            listener?.Stop();
        }
    }
}
