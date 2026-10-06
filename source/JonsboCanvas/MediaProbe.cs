using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Control;

namespace JonsboCanvas
{
    internal static class MediaProbe
    {
        public static int Main()
        {
            GlobalSystemMediaTransportControlsSessionManager manager =
                GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().Result;
            foreach (GlobalSystemMediaTransportControlsSession session in manager.GetSessions())
            {
                GlobalSystemMediaTransportControlsSessionMediaProperties props =
                    session.TryGetMediaPropertiesAsync().AsTask().Result;
                Console.WriteLine("APP=" + session.SourceAppUserModelId);
                Console.WriteLine("TITLE=" + props.Title);
                Console.WriteLine("ARTIST=" + props.Artist);
                Console.WriteLine("ALBUM=" + props.AlbumTitle);
                Console.WriteLine("THUMB=" + (props.Thumbnail != null));
                if (props.Thumbnail != null)
                {
                    using (Stream input = props.Thumbnail.OpenReadAsync().AsTask().Result.AsStreamForRead())
                    using (FileStream output = File.Create(Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "media-cover.jpg")))
                        input.CopyTo(output);
                }
            }
            return 0;
        }
    }
}
