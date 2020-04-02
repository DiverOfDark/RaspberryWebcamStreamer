using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RaspberryStreamer
{
    public class WebCameraProvider : IDisposable
    {
        private readonly StreamerSettings _settings;
        private readonly HttpClient _httpClient;

        public WebCameraProvider(StreamerSettings settings)
        {
            _settings = settings;
            _httpClient = new HttpClient();
        }

        public void Start(CancellationToken token) => new Thread(Worker).Start(token);

        private async void Worker(object obj)
        {
            var ct = (CancellationToken) obj;
            while (!ct.IsCancellationRequested)
            {
                var bytes = await _httpClient.GetByteArrayAsync(_settings.WebCamUrl);

                CurrentFrame = bytes;
                await Task.Delay(1000 / _settings.FPS, ct);
            }
        }

        public byte[] CurrentFrame { get; private set; }

        public void Dispose() => _httpClient?.Dispose();
    }
}