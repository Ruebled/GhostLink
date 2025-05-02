using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GhostLink
{
    public partial class MainWindow : Window
    {
        // TCP listener for incoming chat messages
        private TcpListener listener;
        private bool isListening = false;
        private const int BufferSize = 1024;

        public MainWindow()
        {
            InitializeComponent();
            StartListening();              // Start TCP listener
        }

        /// <summary>
        /// Starts the TCP listener on the port specified in the UI.
        /// </summary>
        private void StartListening()
        {
            int port = int.Parse(PortTextBox.Text);
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            isListening = true;

            // Run accept loop on background thread
            Thread listenerThread = new Thread(ListenForClientsAsync)
            {
                IsBackground = true
            };
            listenerThread.Start();
        }

        /// <summary>
        /// Accepts incoming TCP connections and displays messages.
        /// </summary>
        private async void ListenForClientsAsync()
        {
            while (isListening)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client); // Fire-and-forget
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TCP] Listener error: {ex.Message}");
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


        /// <summary>
        /// Displays a message in IRC-style format: [HH:mm] message
        /// </summary>
        private void AddMessage(string message, bool isOwnMessage = false)
        {
            string timestamp = DateTime.Now.ToString("HH:mm");
            string formatted = $"[{timestamp}] {message}";

            var textBlock = new TextBlock
            {
                Text = formatted,
                Margin = new Thickness(5, 2, 5, 2),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = isOwnMessage
                    ? new SolidColorBrush(Color.FromRgb(144, 238, 144)) // Light green for own messages
                    : new SolidColorBrush(Colors.White)
            };

            ChatPanel.Children.Add(textBlock);

            // Auto-scroll if ChatPanel is inside a ScrollViewer
            if (VisualTreeHelper.GetParent(ChatPanel) is ScrollViewer scroll)
            {
                scroll.ScrollToEnd();
            }
        }

        /// <summary>
        /// Sends a chat message over TCP to the specified IP and port.
        /// </summary>
        private void Send_Click(object sender, RoutedEventArgs e)
        {
            string message = InputBox.Text.Trim();

            if (string.IsNullOrEmpty(message))
                return;

            if (!IPAddress.TryParse(IpTextBox.Text, out var ip))
            {
                MessageBox.Show("Invalid IP address.");
                return;
            }
            if (!int.TryParse(PortTextBox.Text, out var port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Invalid port number.");
                return;
            }


            Task.Run(() =>
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        client.Connect(ip, port);
                        using (var stream = client.GetStream())
                        {
                            byte[] data = Encoding.UTF8.GetBytes(message);
                            stream.Write(data, 0, data.Length);
                        }
                    }

                    // Update UI with our sent message
                    Dispatcher.Invoke(() => AddMessage(message, isOwnMessage: true));
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[TCP] Send error: {ex.Message}");
                }
            });

            InputBox.Clear();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            // Reserved for future persistent connection logic
        }

        /// <summary>
        /// Ensure listener stops when window closes
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            isListening = false;
            listener?.Stop();
        }
    }
}
