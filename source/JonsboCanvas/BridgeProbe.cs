using System;
using System.Threading;

namespace JonsboCanvas
{
    internal static class BridgeProbe
    {
        public static int Main()
        {
            using (NeteaseCdpBridge bridge = new NeteaseCdpBridge(38476))
            {
                for (int index = 0; index < 12; index++)
                {
                    Thread.Sleep(500);
                    NeteasePlaybackSnapshot value = bridge.GetSnapshot();
                    Console.WriteLine("CONNECTED={0} ID={1} POSITION={2:F2} DURATION={3:F3} PLAYING={4}",
                        value.Connected, value.SongId, value.PositionSeconds,
                        value.DurationSeconds, value.Playing);
                }
            }
            return 0;
        }
    }

    internal static class Log
    {
        public static void Write(string message) { Console.WriteLine("LOG: " + message); }
    }
}
