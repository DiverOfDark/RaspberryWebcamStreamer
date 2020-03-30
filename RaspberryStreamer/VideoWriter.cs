using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace RaspberryStreamer
{
    public class VideoWriter : IDisposable
    {
        private readonly H264VideoStreamEncoder _encoder;
        private readonly VideoFrameConverter _vfc;
        private int _frameCounter;

        internal static int Fps = 25;

        private static ILogger globalLogger;
        private static av_log_set_callback_callback _callback;

        private static unsafe void LogCallback(void* p0, int level, string format, byte* vl)
        {
            if (level > ffmpeg.av_log_get_level()) return;

            var lineSize = 1024;
            var lineBuffer = stackalloc byte[lineSize];
            var printPrefix = 1;
            ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
            var line = Marshal.PtrToStringAnsi((IntPtr) lineBuffer);
            globalLogger.LogInformation(line.Trim());
        }

        public VideoWriter(string filename, Bitmap example, ILogger logger)
        {
            if (globalLogger == null)
            {
                unsafe
                {
                    globalLogger = logger;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        ffmpeg.RootPath = Path.Combine(Environment.CurrentDirectory, "ffmpeg/x86_64");
                    }

                    ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);
                    _callback = LogCallback;
                    ffmpeg.av_log_set_callback(_callback);
                }
            }

            _encoder = new H264VideoStreamEncoder(filename, Fps, example.Size);
            _vfc = new VideoFrameConverter(example.Size, AVPixelFormat.AV_PIX_FMT_RGB24, example.Size,
                AVPixelFormat.AV_PIX_FMT_YUV420P);
        }

        public void Dispose()
        {
            _encoder.Dispose();
            _vfc.Dispose();
        }
        
        private static byte[] GetBitmapData(Bitmap frameBitmap)
        {
            var bitmapData = frameBitmap.LockBits(new Rectangle(Point.Empty, frameBitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var length = bitmapData.Stride * bitmapData.Height;
                var data = new byte[length];
                Marshal.Copy(bitmapData.Scan0, data, 0, length);
                return data;
            }
            finally
            {
                frameBitmap.UnlockBits(bitmapData);
            }
        }

        public unsafe void WriteFrame(Bitmap bitmap)
        {
            fixed (byte* pBitmapData = GetBitmapData(bitmap))
            {
                var data = new byte_ptrArray8 {[0] = pBitmapData};
                var linesize = new int_array8 {[0] = GetBitmapData(bitmap).Length / bitmap.Height};
                var frame = new AVFrame
                {
                    data = data,
                    linesize = linesize,
                    height = bitmap.Height
                };

                var convertedFrame = _vfc.Convert(frame);
                convertedFrame.pts = _frameCounter;

                _encoder.Encode(convertedFrame);
            }

            _frameCounter++;
        }
    }
}