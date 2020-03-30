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

        public Worker(ILogger<Worker> logger, DuetWifiStatusProvider statusProvider, WebCameraProvider webCameraProvider)
        {
            _logger = logger;
            _statusProvider = statusProvider;
            _webCameraProvider = webCameraProvider;
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

                    await StartRecording(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception at worker");
                }
            }
            _webCameraProvider.Dispose();
        }

        private async Task StartRecording(CancellationToken stoppingToken)
        {
            while (_statusProvider.FileInfo == null)
            {
                await Task.Delay(1000 / VideoWriter.Fps);
            }
            
            var filename = _statusProvider.FileInfo.GetFileNameWithoutPath();
            filename = GenerateVideoFileName(filename);

            using var writer = new VideoWriter(filename, _webCameraProvider.CurrentFrame, _logger);

            _logger.LogInformation($"Non-Idle, starting recording of {filename}");
            while (!_statusProvider.Status.IsIdle && !stoppingToken.IsCancellationRequested)
            {
                if (_statusProvider.Status.IsPaused)
                {
                    await Task.Delay(100, stoppingToken);
                    continue;
                }

                writer.WriteFrame(_webCameraProvider.CurrentFrame);
                await Task.Delay(1000 / VideoWriter.Fps, stoppingToken);
            }
            _logger.LogInformation($"Completed recording of {filename}");
        }

        private string GenerateVideoFileName(string filename)
        {
            filename = Path.GetFileNameWithoutExtension(filename);

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