namespace RaspberryStreamer
{
    public class StreamerSettings
    {
        public StreamerSettings(string[] args)
        {
        }

        public string DuetWifiHost => "duetwifi";

        public string WebCamUrl => "http://raspberry:8081/?action=snapshot";
        public int Width { get; } = 1280;
        public int Height { get; } = 720;
        public int FPS { get; } = 25;

        public bool FlipY { get; } = true;
        public bool FlipX { get; } = false;
    }
}