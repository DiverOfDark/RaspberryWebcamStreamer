using System;
using System.Threading;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace RaspberryStreamer
{
    public class Video4LinuxWebCameraProvider : IDisposable, IWebCamera
    {
        private readonly ILogger<Video4LinuxWebCameraProvider> _logger;
        private readonly StreamerSettings _settings;

        public Video4LinuxWebCameraProvider(ILogger<Video4LinuxWebCameraProvider> logger, StreamerSettings settings)
        {
            _logger = logger;
            _settings = settings;
            Width = 1280;
            Height = 720;
        }


        public unsafe AVFrame* GetFrame()
        {
            return null;
        }

        public void Start(CancellationToken token)
        {
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public AVPixelFormat PixelFormat { get; private set; }

        public void Dispose()
        {

        }
    }
}

