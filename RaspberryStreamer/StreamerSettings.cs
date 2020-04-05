using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommandLine;
using Newtonsoft.Json;

namespace RaspberryStreamer
{
    public class StreamerSettings
    {
        [Option('h', "duetWiFiHost", Required = false, Default = "duetwifi")]
        public string DuetWifiHost { get; set; }

        [Option('w', "webcamUrl", Required = false, HelpText = "i.e. http://raspberry:8081/?action=snapshot")]
        public string WebCamUrl { get; set; }

        [Option('v', "webcamDevice", Required = false, HelpText = "i.e. /dev/video0")]
        public string WebCamDevice { get; set; }

        [Option('f', "fps", Required = false, Default = 10)]
        public int FPS { get; set; }

        [Option('o', "output", Required = false, Default = "/home/pi/")]
        public string OutputFolder { get; set; }

        [Option('y', "flipy", Required = false, Default = false)]
        public bool FlipY { get; set; }

        [Option('x', "flipx", Required = false, Default = false)]
        public bool FlipX { get; set; }

        public override string ToString()
        {
            return string.Join(Environment.NewLine, GetType().GetProperties().OrderBy(v => v.Name).Select(v =>
            {
                var attribute = v.GetCustomAttribute(typeof(OptionAttribute)) as OptionAttribute;
                var parameterName = attribute?.LongName ?? v.Name;
                var parameterValue = (v.GetValue(this) ?? "").ToString();

                return parameterName + "=" + parameterValue;
            }));
        }
    }
}