using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace RaspberryStreamer
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DuetWifiStatusProvider _statusProvider;
        private readonly WebCameraProvider _webCameraProvider;
        private readonly StreamerSettings _streamerSettings;

        public Worker(ILogger<Worker> logger, DuetWifiStatusProvider statusProvider, WebCameraProvider webCameraProvider, StreamerSettings streamerSettings)
        {
            _logger = logger;
            _statusProvider = statusProvider;
            _webCameraProvider = webCameraProvider;
            _streamerSettings = streamerSettings;
            FFMpegSetup.Init(logger);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _statusProvider.Start(stoppingToken);
            _webCameraProvider.Start(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, stoppingToken);

                    if (_statusProvider.Status == null || _statusProvider.Status.IsIdle)
                    {
                        continue;
                    }

                    StartRecording(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception at worker");
                }
            }
            _webCameraProvider.Dispose();
        }

        private void StartRecording(CancellationToken stoppingToken)
        {
            while (_statusProvider.FileInfo == null)
            {
                Thread.Sleep(1000 / _streamerSettings.FPS);
            }
            
            var filename = _statusProvider.FileInfo.GetFileNameWithoutPath();
            filename = GenerateVideoFileName(filename);

            using var writer = new VideoWriter(filename, _streamerSettings);

            _logger.LogInformation($"Non-Idle, starting recording of {filename}");
            var sw = new Stopwatch();
            while (!_statusProvider.Status.IsIdle && !stoppingToken.IsCancellationRequested)
            {
                if (_statusProvider.Status.IsPaused)
                {
                    Thread.Sleep(100);
                    continue;
                }
                sw.Restart();
                writer.WriteFrame(_webCameraProvider.CurrentFrame);
                var delay = (int) (1000.0 / _streamerSettings.FPS - sw.ElapsedMilliseconds);
                if (delay > 0)
                {
                    Thread.Sleep(delay);
                }
            }
            _logger.LogInformation($"Completed recording of {filename}");
        }

        private string GenerateVideoFileName(string filename)
        {
            filename = Path.GetFileNameWithoutExtension(filename);
            if (string.IsNullOrWhiteSpace(filename))
                filename = "Unknown";

            if (!File.Exists(filename + ".mp4"))
                return filename + ".mp4";
            int count = 1;
            while (true)
            {
                var newFilename = filename + " (" + count + ").mp4";
                if (!File.Exists(newFilename))
                    return newFilename;
                count++;
            }
        }
    }
}