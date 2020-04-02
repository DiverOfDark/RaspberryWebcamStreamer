using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace RaspberryStreamer
{
    public static class FFMpegSetup
    {
        private static ILogger _globalLogger;

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private static av_log_set_callback_callback _callback;

        public static void Init(ILogger logger)
        {
            if (_globalLogger == null)
            {
                unsafe
                {
                    _globalLogger = logger;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        ffmpeg.RootPath = Path.Combine(Environment.CurrentDirectory, "ffmpeg/x86_64");
                    }

                    ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                    // ffmpeg.av_log_set_level(ffmpeg.AV_LOG_MAX_OFFSET);
                    _callback = LogCallback;
                    ffmpeg.av_log_set_callback(_callback);
                }
            }
        }

        private static unsafe void LogCallback(void* p0, int level, string format, byte* vl)
        {
            if (level > ffmpeg.av_log_get_level()) return;

            var lineSize = 1024;
            var lineBuffer = stackalloc byte[lineSize];
            var printPrefix = 1;
            ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
            var line = Marshal.PtrToStringAnsi((IntPtr) lineBuffer).Trim();
            if (!line.Contains("unable to decode APP fields: Invalid data found when processing input"))
            {
                _globalLogger.Log(ConvertLogLevel(level), line);
            }
        }

        private static LogLevel ConvertLogLevel(in int level)
        {
            switch (level)
            {
                case ffmpeg.AV_LOG_MAX_OFFSET:
                case ffmpeg.AV_LOG_TRACE:
                    return LogLevel.Trace;
                case ffmpeg.AV_LOG_DEBUG:
                case ffmpeg.AV_LOG_VERBOSE:
                    return LogLevel.Debug;
                case ffmpeg.AV_LOG_ERROR:
                    return LogLevel.Error;
                case ffmpeg.AV_LOG_PANIC:
                case ffmpeg.AV_LOG_FATAL:
                    return LogLevel.Critical;
                case ffmpeg.AV_LOG_WARNING:
                case ffmpeg.AV_LOG_QUIET:
                    return LogLevel.Warning;

                case ffmpeg.AV_LOG_INFO:
                default:
                    return LogLevel.Information;
            }
        }

        public static unsafe int ThrowExceptionIfError(this int error)
        {
            if (error < 0)
            {
                var bufferSize = 1024;
                var buffer = stackalloc byte[bufferSize];
                ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
                var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
                throw new ApplicationException(message);
            }
            return error;
        }
    }
}