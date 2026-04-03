using DesktopMediaServer.Macros;
using DesktopMediaServer.Media;
using DesktopMediaServer.Server;
using System;
using System.Diagnostics;
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

                // Attempt to start server automatically on app open
                await TryStartServerFromUiAsync();
            };
        }

        // Try to start the server using the current UI values for port and token.
        // Validation and error handling mirror the Start button behavior.
        private async Task TryStartServerFromUiAsync()
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

        private void RegisterMacros()
        {
            // IMPORTANT: These ids must match what the phone calls.
            // Add/remove macros here. The phone client expects these ids:
            // "mic-mute", "show-desktop", "desktop-left", "desktop-right",
            // "open-spotify", "open-discord", "open-firefox", "open-vscode", "open-files"

            // Mute microphone (Ctrl+Shift+M)
            _macros.Add(
                id: "mic-mute",
                name: "Mute Mic",
                description: "Ctrl+Shift+M",
                run: () => KeySender.KeyCombo(VK.CONTROL, VK.SHIFT, VK.M)
            );

            // Show desktop (Win + D)
            _macros.Add(
                id: "show-desktop",
                name: "Show Desktop",
                description: "Win + D",
                run: () => KeySender.KeyCombo(VK.LWIN, VK.D)
            );

            // Switch virtual desktop left/right (Win + Ctrl + Left/Right)
            _macros.Add(
                id: "desktop-left",
                name: "Desktop Left",
                description: "Win + Ctrl + Left",
                run: () => KeySender.KeyCombo(VK.LWIN, VK.CONTROL, VK.LEFT)
            );

            _macros.Add(
                id: "desktop-right",
                name: "Desktop Right",
                description: "Win + Ctrl + Right",
                run: () => KeySender.KeyCombo(VK.LWIN, VK.CONTROL, VK.RIGHT)
            );

            // App launchers - use shell execute so registered protocols or installed apps open
            _macros.Add(
                id: "open-spotify",
                name: "Spotify",
                description: "Open Spotify",
                run: () => Process.Start(new ProcessStartInfo { FileName = "spotify:", UseShellExecute = true })
            );

            _macros.Add(
                id: "open-discord",
                name: "Discord",
                description: "Open Discord",
                run: () => Process.Start(new ProcessStartInfo { FileName = "discord:", UseShellExecute = true })
            );

            _macros.Add(
                id: "open-firefox",
                name: "Firefox",
                description: "Open Firefox",
                run: () => Process.Start(new ProcessStartInfo { FileName = "firefox", UseShellExecute = true })
            );

            _macros.Add(
                id: "open-vscode",
                name: "VS Code",
                description: "Open VS Code",
                run: () => Process.Start(new ProcessStartInfo { FileName = "code", UseShellExecute = true })
            );

            _macros.Add(
                id: "open-files",
                name: "Files",
                description: "Open File Explorer",
                run: () => Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true })
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

        protected override void OnClosed(EventArgs e)
        {
            // Ensure native/event resources are released when the window closes.
            try
            {
                // Stop the server synchronously to ensure Kestrel releases file handles
                _server.StopAsync().GetAwaiter().GetResult();
            }
            catch { }

            try { _server.Dispose(); } catch { }
            try { _media.Dispose(); } catch { }

            base.OnClosed(e);

            // Ensure the process actually exits and doesn't leave background threads
            // (prevents publish/build file locking from orphaned processes)
            try { Application.Current.Shutdown(); } catch { }
        }
    }
}