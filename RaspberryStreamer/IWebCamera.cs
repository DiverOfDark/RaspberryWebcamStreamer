using System;
using System.Threading;
using FFmpeg.AutoGen;

namespace RaspberryStreamer
{
    interface IWebCamera : IDisposable
    {
        public unsafe AVFrame* GetFrame();

        public void Start(CancellationToken token);

        public int Width { get; }
        public int Height { get; }
        public AVPixelFormat PixelFormat { get; }

    }
}