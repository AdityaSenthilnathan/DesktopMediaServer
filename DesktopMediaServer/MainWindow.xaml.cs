using DesktopMediaServer.Media;
using DesktopMediaServer.Server;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;

namespace DesktopMediaServer
{
    public partial class MainWindow : Window
    {
        private readonly GsmtcController _media = new();
        private readonly NowPlayingServer _server = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (_, __) =>
            {
                await _media.InitializeAsync();
                await Refresh();
            };
        }

        private async Task Refresh()
        {
            var info = await _media.GetNowPlayingAsync(false);

            TrackText.Text = !info.SpotifyFound
                ? "(Spotify not found)"
                : $"{info.Title}\n{info.Artist} — {info.Album}";

            TimeText.Text = $"{Fmt(info.PositionMs)} / {Fmt(info.DurationMs)}" +
                            (info.IsPlaying ? " (Playing)" : " (Paused)");

            StatusText.Text = _server.IsRunning
                ? $"Server running: http://{LocalIp()}:{PortBox.Text}"
                : "Stopped";
        }

        private static string Fmt(long ms)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return $"{t.Minutes}:{t.Seconds:00}";
        }

        private static string LocalIp()
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            await _server.StartAsync(_media, int.Parse(PortBox.Text), TokenBox.Text);
            StartBtn.IsEnabled = false;
            StopBtn.IsEnabled = true;
            await Refresh();
        }

        private async void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            await _server.StopAsync();
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
            await Refresh();
        }

        private async void Prev_Click(object sender, RoutedEventArgs e)
        {
            await _media.PreviousAsync();
            await Refresh();
        }

        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            await _media.PlayPauseAsync();
            await Refresh();
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            await _media.NextAsync();
            await Refresh();
        }
    }
}