using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace RaspberryStreamer
{
    public class DuetWifiStatusProvider
    {
        private readonly StreamerSettings _settings;
        private readonly ILogger<DuetWifiStatusProvider> _logger;
        private readonly HttpClient _httpClient;
        public DuetWifiStatusProvider(StreamerSettings settings, ILogger<DuetWifiStatusProvider> logger)
        {
            _settings = settings;
            _logger = logger;
            _httpClient = new HttpClient();
        }

        public DuetWebControlStatus Status { get; set; }

        public FileInfoStatus FileInfo { get; set; }

        public void Start(in CancellationToken stoppingToken) => new Thread(WorkerThread).Start(stoppingToken);

        private async void WorkerThread(object state)
        {
            _logger.LogInformation("DuetWifi Status Provider started...");
            var cancellationToken = (CancellationToken) state;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var status = await _httpClient.GetStringAsync($"http://{_settings.DuetWifiHost}/rr_status?type=2");
                    var statusJ = JsonConvert.DeserializeObject<DuetWebControlStatus>(status);

                    Status = statusJ;

                    var file = await _httpClient.GetStringAsync($"http://{_settings.DuetWifiHost}/rr_fileinfo?type=2");
                    var fileJ = JsonConvert.DeserializeObject<FileInfoStatus>(file);

                    FileInfo = fileJ;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get DuetWifi status");
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
    }
}