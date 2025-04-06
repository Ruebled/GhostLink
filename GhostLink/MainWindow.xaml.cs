using System.Windows;
using GhostLink.Chat;

namespace GhostLink
{
    public partial class MainWindow : Window
    {
        private Peer peer;

        public MainWindow()
        {
            InitializeComponent();
            peer = new Peer(50000);
            peer.MessageReceived += OnMessageReceived;
            peer.Start();
            AppendChat("[System] Listening on port 5000");
        }

        private void AppendChat(string message)
        {
            Dispatcher.Invoke(() => ChatBox.AppendText($"{message}\n"));
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string ip = IpTextBox.Text;
            if (!int.TryParse(PortTextBox.Text, out int port)) return;

            string message = InputBox.Text;
            await peer.SendMessage(ip, port, message);
            AppendChat($"[You]: {message}");
            InputBox.Clear();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            AppendChat("[System] Manual connect not needed — messages are sent directly.");
        }

        private void OnMessageReceived(string message)
        {
            AppendChat(message);
        }
    }
}
