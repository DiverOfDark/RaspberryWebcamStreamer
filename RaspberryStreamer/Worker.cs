using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RaspberryStreamer
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DuetWifiStatusProvider _statusProvider;
        private readonly IWebCamera _webCamera;
        private readonly StreamerSettings _streamerSettings;

        public Worker(ILoggerFactory loggerFactory, DuetWifiStatusProvider statusProvider, StreamerSettings streamerSettings)
        {
            FFMpegSetup.Init(loggerFactory.CreateLogger<FFMpegSetup>());
            _logger = loggerFactory.CreateLogger<Worker>();
            _logger.LogInformation($"Starting with settings: {streamerSettings}");
            _statusProvider = statusProvider;
            if (streamerSettings.WebCamUrl != null)
            {
                _webCamera = new MJpegStreamWebCameraProvider(loggerFactory.CreateLogger<MJpegStreamWebCameraProvider>(), streamerSettings);
            }
            else if (streamerSettings.WebCamDevice != null)
            {
                _webCamera = new Video4LinuxWebCameraProvider(loggerFactory.CreateLogger<Video4LinuxWebCameraProvider>(), streamerSettings);
            } else throw new ArgumentOutOfRangeException("Either webcamUrl or webcamDevice should be enabled");

            _streamerSettings = streamerSettings;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _statusProvider.Start(stoppingToken);
            _webCamera.Start(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, stoppingToken);

                    if (_statusProvider.Status == null || _statusProvider.Status.IsIdle || _statusProvider.Status.IsBusy)
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
            _webCamera.Dispose();
        }

        private void StartRecording(CancellationToken stoppingToken)
        {
            while (_statusProvider.FileInfo == null || _webCamera.Width == 0)
            {
                Thread.Sleep(1000 / _streamerSettings.FPS);
            }
            
            var filename = _statusProvider.FileInfo.GetFileNameWithoutPath();
            filename = Path.GetFileNameWithoutExtension(filename);
            if (string.IsNullOrWhiteSpace(filename))
                filename = "Unknown";

            filename = $"{DateTime.Now:s} - {filename}.mp4";
            filename = filename.Replace(":", "_");
            filename = Path.Combine(_streamerSettings.OutputFolder, filename);
            
            using var writer = new VideoWriter(_logger, filename, _webCamera.Width, _webCamera.Height, _webCamera.PixelFormat, _streamerSettings);

            _logger.LogInformation($"Non-Idle status {_statusProvider.Status.DetailedStatus}, starting recording of {filename}.");
            var sw = new Stopwatch();
            while (!_statusProvider.Status.IsIdle && !stoppingToken.IsCancellationRequested)
            {
                if (_statusProvider.Status.IsPaused)
                {
                    Thread.Sleep(100);
                    continue;
                }
                sw.Restart();
                unsafe
                {
                    writer.WriteFrame(_webCamera.GetFrame());
                }

                var delay = (int) (1000.0 / _streamerSettings.FPS - sw.ElapsedMilliseconds);
                if (delay > 0)
                {
                    Thread.Sleep(delay);
                }
            }
            _logger.LogInformation($"Completed recording of {filename}");
        }
    }
}