using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DesktopMediaServer.Media
{
    public sealed class NowPlayingInfo
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public bool IsPlaying { get; set; }
        public long PositionMs { get; set; }
        public long DurationMs { get; set; }
        public string SourceAppId { get; set; } = "";
        public bool SpotifyFound { get; set; }

        // Album art bytes encoded as base64 (raw image file bytes)
        public string? AlbumArtBase64 { get; set; }
        // Optional hint for client rendering
        public string? AlbumArtMime { get; set; } // e.g. "image/jpeg" or "image/png"
    }

    public sealed class GsmtcController : IDisposable
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private const string SpotifyMatch = "Spotify";

        public event EventHandler? Changed;

        public async Task InitializeAsync()
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += Manager_CurrentSessionChanged;

            HookSession(GetSpotifySession());
        }

        private void Manager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            HookSession(GetSpotifySession());
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private GlobalSystemMediaTransportControlsSession? GetSpotifySession()
        {
            if (_manager == null) return null;

            foreach (var s in _manager.GetSessions())
            {
                var id = s.SourceAppUserModelId ?? "";
                if (id.Contains(SpotifyMatch, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            return null;
        }

        private void HookSession(GlobalSystemMediaTransportControlsSession? session)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= Session_Changed;
                _session.PlaybackInfoChanged -= Session_Changed;
                _session.TimelinePropertiesChanged -= Session_Changed;
            }

            _session = session;

            if (_session != null)
            {
                _session.MediaPropertiesChanged += Session_Changed;
                _session.PlaybackInfoChanged += Session_Changed;
                _session.TimelinePropertiesChanged += Session_Changed;
            }
        }

        private void Session_Changed(GlobalSystemMediaTransportControlsSession sender, object args)
            => Changed?.Invoke(this, EventArgs.Empty);

        public async Task<NowPlayingInfo> GetNowPlayingAsync(bool includeArt)
        {
            await _lock.WaitAsync();
            try
            {
                var info = new NowPlayingInfo();
                if (_manager == null) return info;

                var s = GetSpotifySession();
                if (s == null)
                {
                    info.SpotifyFound = false;
                    return info;
                }

                info.SpotifyFound = true;
                info.SourceAppId = s.SourceAppUserModelId ?? "";

                var playback = s.GetPlaybackInfo();
                info.IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                var timeline = s.GetTimelineProperties();
                info.PositionMs = (long)timeline.Position.TotalMilliseconds;
                info.DurationMs = (long)timeline.EndTime.TotalMilliseconds;

                var props = await s.TryGetMediaPropertiesAsync();
                info.Title = props?.Title ?? "";
                info.Artist = props?.Artist ?? "";
                info.Album = props?.AlbumTitle ?? "";

                if (includeArt && props?.Thumbnail != null)
                {
                    var (b64, mime) = await ReadThumbnailAsBase64Async(props.Thumbnail);
                    info.AlbumArtBase64 = b64;
                    info.AlbumArtMime = mime;
                }

                return info;
            }
            finally
            {
                _lock.Release();
            }
        }

        private static async Task<(string? base64, string? mime)> ReadThumbnailAsBase64Async(IRandomAccessStreamReference thumbnail)
        {
            try
            {
                using var stream = await thumbnail.OpenReadAsync();
                if (stream.Size == 0) return (null, null);

                var bytes = new byte[stream.Size];
                using (var reader = new DataReader(stream))
                {
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(bytes);
                }

                // Best-effort MIME sniff (JPEG/PNG). If unknown, omit mime.
                string? mime = null;
                if (bytes.Length >= 4 &&
                    bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                    mime = "image/png";
                else if (bytes.Length >= 3 &&
                    bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                    mime = "image/jpeg";

                return (Convert.ToBase64String(bytes), mime);
            }
            catch
            {
                return (null, null);
            }
        }

        public Task<bool> PlayPauseAsync() => TryCommandAsync(s => s.TryTogglePlayPauseAsync());
        public Task<bool> PlayAsync() => TryCommandAsync(s => s.TryPlayAsync());
        public Task<bool> PauseAsync() => TryCommandAsync(s => s.TryPauseAsync());
        public Task<bool> NextAsync() => TryCommandAsync(s => s.TrySkipNextAsync());
        public Task<bool> PreviousAsync() => TryCommandAsync(s => s.TrySkipPreviousAsync());

        private async Task<bool> TryCommandAsync(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> op)
        {
            await _lock.WaitAsync();
            try
            {
                if (_manager == null) return false;

                var s = GetSpotifySession();
                if (s == null) return false;

                return await op(s);
            }
            catch { return false; }
            finally { _lock.Release(); }
        }

        public void Dispose()
        {
            if (_manager != null)
                _manager.CurrentSessionChanged -= Manager_CurrentSessionChanged;

            if (_session != null)
            {
                _session.MediaPropertiesChanged -= Session_Changed;
                _session.PlaybackInfoChanged -= Session_Changed;
                _session.TimelinePropertiesChanged -= Session_Changed;
            }

            _lock.Dispose();
        }
    }
}