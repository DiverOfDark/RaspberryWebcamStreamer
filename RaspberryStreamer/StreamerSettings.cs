namespace RaspberryStreamer
{
    public class StreamerSettings
    {
        public StreamerSettings(string[] args)
        {
        }

        public string DuetWifiHost => "duetwifi";

        public string WebCamUrl => "http://raspberry:8081/?action=snapshot";
    }
}