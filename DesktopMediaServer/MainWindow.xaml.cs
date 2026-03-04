using DesktopMediaServer.Macros;
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
        private readonly MacroRegistry _macros = new();

        public MainWindow()
        {
            InitializeComponent();

            Loaded += async (_, __) =>
            {
                // Register allowlisted macros once at startup
                RegisterMacros();

                await _media.InitializeAsync();
                await Refresh();

                // Optional: default buttons state
                StopBtn.IsEnabled = false;
                StartBtn.IsEnabled = true;
            };
        }

        private void RegisterMacros()
        {
            // IMPORTANT: These ids must match what the phone calls.
            // Add/remove macros here.

            _macros.Add(
                id: "mute",
                name: "Mute",
                description: "Ctrl+Shift+M",
                run: () => KeySender.KeyCombo(VK.CONTROL, VK.SHIFT, VK.M)
            );

            _macros.Add(
                id: "show-desktop",
                name: "Show Desktop",
                description: "Win + D",
                run: () => KeySender.KeyCombo(VK.LWIN, VK.D)
            );

            _macros.Add(
                id: "alt-tab",
                name: "Alt + Tab",
                description: "Alt + Tab",
                run: () => KeySender.KeyCombo(VK.ALT, VK.TAB)
            );

            _macros.Add(
                id: "left",
                name: "Left Arrow",
                description: "←",
                run: () => KeySender.KeyCombo(VK.LEFT)
            );

            _macros.Add(
                id: "right",
                name: "Right Arrow",
                description: "→",
                run: () => KeySender.KeyCombo(VK.RIGHT)
            );
            _macros.Add(
                id: "beep",
                name: "Beep (test)",
                description: "Plays a sound if macros run",
                run: () => System.Media.SystemSounds.Beep.Play()
            );
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
            if (!int.TryParse(PortBox.Text, out var port) || port < 1024 || port > 65535)
            {
                StatusText.Text = "Invalid port (use 1024–65535)";
                return;
            }

            var token = (TokenBox.Text ?? "").Trim();
            if (token.Length < 6)
            {
                StatusText.Text = "Token too short (min 6 chars)";
                return;
            }

            try
            {
                await _server.StartAsync(_media, port, token, _macros);

                StartBtn.IsEnabled = false;
                StopBtn.IsEnabled = true;

                await Refresh();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to start: {ex.Message}";
            }
        }

        private async void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _server.StopAsync();

                StartBtn.IsEnabled = true;
                StopBtn.IsEnabled = false;

                await Refresh();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to stop: {ex.Message}";
            }
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