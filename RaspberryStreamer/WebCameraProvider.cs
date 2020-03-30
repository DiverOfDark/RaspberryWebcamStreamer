using System;
using System.Drawing;
using System.IO;
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
                var newBitmap = (Bitmap) Image.FromStream(new MemoryStream(bytes));
                var oldBitmap = CurrentFrame;
                CurrentFrame = newBitmap;
                await Task.Delay(1000 / VideoWriter.Fps, ct);
                oldBitmap?.Dispose();
            }
        }

        public Bitmap CurrentFrame { get; private set; }

        public void Dispose() => _httpClient?.Dispose();
    }
}