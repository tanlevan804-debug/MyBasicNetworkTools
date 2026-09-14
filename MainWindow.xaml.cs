using System.Windows;
using MyBasicNetworkTools.Views;

namespace MyBasicNetworkTools
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // ==============================
        // DASHBOARD
        // ==============================

        private void Dashboard_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "Bạn đang ở Dashboard.",
                "My Basic Network Tools");
        }


        // ==============================
        // MY PING
        // ==============================

        private void Ping_Click(
            object sender,
            RoutedEventArgs e)
        {
            PingWindow pingWindow = new PingWindow();

            pingWindow.Show();
        }


        // ==============================
        // MY TRACERT
        // ==============================

        private void Tracert_Click(
            object sender,
            RoutedEventArgs e)
        {
            TracerouteWindow tracertWindow =
                new TracerouteWindow();

            tracertWindow.Show();
        }


        // ==============================
        // DNS QUERY
        // ==============================

        private void Dns_Click(
            object sender,
            RoutedEventArgs e)
        {
            DnsWindow dnsWindow =
                new DnsWindow();

            dnsWindow.Show();
        }


        // ==============================
        // SCAN IP
        // ==============================

        private void Scan_Click(
     object sender,
     RoutedEventArgs e)
        {
            ScanWindow scanWindow =
                new ScanWindow();

            scanWindow.Show();
        }


        // ==============================
        // NETWORK INFORMATION
        // ==============================

        private void Network_Click(object sender, RoutedEventArgs e)
        {
            NetworkWindow networkWindow = new NetworkWindow();
            networkWindow.Show();
        }
    }
}