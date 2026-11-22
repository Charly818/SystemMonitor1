using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace SystemMonitor
{
    internal sealed class MediaInfo
    {
        public bool HasSession;
        public string Title = "";
        public string Artist = "";
        public string App = "";
        public bool Playing;
        public double PositionSec;
        public double DurationSec;
        public DateTime UpdatedUtc = DateTime.UtcNow;
        public byte[] Art;
        public int ArtVersion;
    }

    /// <summary>Керування й дані про музику через системний медіа-інтерфейс Windows (Spotify, браузер, будь-який плеєр).</summary>
    internal sealed class MediaService
    {
        public volatile bool Enabled;
        public volatile MediaInfo Info = new MediaInfo();

        volatile bool stop;
        GlobalSystemMediaTransportControlsSession current;
        int artVersion;

        public MediaService()
        {
            Task.Run(LoopAsync);
        }

        public void Stop() { stop = true; }

        static GlobalSystemMediaTransportControlsSession Pick(GlobalSystemMediaTransportControlsSessionManager mgr)
        {
            foreach (GlobalSystemMediaTransportControlsSession s in mgr.GetSessions())
            {
                string id = s.SourceAppUserModelId ?? "";
                if (id.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0) return s;
            }
            return mgr.GetCurrentSession();
        }

        static async Task<byte[]> ReadThumb(GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            try
            {
                if (props.Thumbnail == null) return null;
                using (Windows.Storage.Streams.IRandomAccessStreamWithContentType stream = await props.Thumbnail.OpenReadAsync())
                using (Stream s = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream))
                using (MemoryStream ms = new MemoryStream())
                {
                    await s.CopyToAsync(ms);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log.Write("обкладинка: " + ex.Message);
                return null;
            }
        }

        async Task LoopAsync()
        {
            GlobalSystemMediaTransportControlsSessionManager mgr = null;
            string lastKey = "";
            byte[] art = null;
            while (!stop)
            {
                try
                {
                    if (!Enabled) { await Task.Delay(1000); continue; }
                    if (mgr == null) mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

                    GlobalSystemMediaTransportControlsSession session = Pick(mgr);
                    current = session;
                    if (session == null)
                    {
                        Info = new MediaInfo();
                    }
                    else
                    {
                        GlobalSystemMediaTransportControlsSessionMediaProperties props = await session.TryGetMediaPropertiesAsync();
                        GlobalSystemMediaTransportControlsSessionPlaybackInfo pb = session.GetPlaybackInfo();
                        GlobalSystemMediaTransportControlsSessionTimelineProperties tl = session.GetTimelineProperties();

                        string key = props.Title + "|" + props.Artist + "|" + props.AlbumTitle;
                        if (key != lastKey)
                        {
                            lastKey = key;
                            art = await ReadThumb(props);
                            artVersion++;
                        }

                        MediaInfo mi = new MediaInfo();
                        mi.HasSession = true;
                        mi.Title = props.Title ?? "";
                        mi.Artist = props.Artist ?? "";
                        mi.App = session.SourceAppUserModelId ?? "";
                        mi.Playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        mi.PositionSec = tl.Position.TotalSeconds;
                        mi.DurationSec = (tl.EndTime - tl.StartTime).TotalSeconds;
                        mi.UpdatedUtc = tl.LastUpdatedTime.UtcDateTime;
                        mi.Art = art;
                        mi.ArtVersion = artVersion;
                        Info = mi;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("медіа: " + ex.Message);
                    mgr = null;
                    await Task.Delay(2000);
                }
                await Task.Delay(700);
            }
        }

        public async void PlayPause()
        {
            try { GlobalSystemMediaTransportControlsSession s = current; if (s != null) await s.TryTogglePlayPauseAsync(); } catch { }
        }

        public async void Next()
        {
            try { GlobalSystemMediaTransportControlsSession s = current; if (s != null) await s.TrySkipNextAsync(); } catch { }
        }

        public async void Previous()
        {
            try { GlobalSystemMediaTransportControlsSession s = current; if (s != null) await s.TrySkipPreviousAsync(); } catch { }
        }
    }
}
