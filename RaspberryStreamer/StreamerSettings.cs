using CommandLine;
using Newtonsoft.Json;

namespace RaspberryStreamer
{
    public class StreamerSettings
    {
        [Option('h', "duetWiFiHost", Required = false, Default = "duetwifi")]
        public string DuetWifiHost { get; set; }

        [Option('w', "webcamUrl", Required = false, Default = "http://raspberry:8081/?action=snapshot")]
        public string WebCamUrl { get; set; }

        [Option('f', "fps", Required = false, Default = 10)]
        public int FPS { get; set; }


        [Option('y', "flipy", Required = false, Default = false)]
        public bool FlipY { get; set; }

        [Option('x', "flipx", Required = false, Default = false)]
        public bool FlipX { get; set; }

        public override string ToString()
        {
            return $"{nameof(DuetWifiHost)}: {DuetWifiHost}, {nameof(WebCamUrl)}: {WebCamUrl}, {nameof(FPS)}: {FPS}, {nameof(FlipY)}: {FlipY}, {nameof(FlipX)}: {FlipX}";
        }
    }
}