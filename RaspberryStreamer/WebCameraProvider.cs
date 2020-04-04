using System;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

namespace RaspberryStreamer
{
    public class WebCameraProvider : IDisposable
    {
        private readonly StreamerSettings _settings;
        private readonly HttpClient _httpClient;
        private byte[] _currentFrame;

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

                if (_currentFrame == null)
                {
                    unsafe { 
                        var framePtr = GetFrame(bytes, out var width, out var height, out var pixFormat);
                        ffmpeg.av_frame_free(&framePtr);
                        Width = width;
                        Height = height;
                        PixelFormat = pixFormat;
                    }
                }

                _currentFrame = bytes;
                await Task.Delay(1000 / _settings.FPS, ct);
            }
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public AVPixelFormat PixelFormat { get; private set; }

        public unsafe AVFrame* GetFrame() => GetFrame(_currentFrame, out _, out _, out _);

        private unsafe AVFrame* GetFrame(byte[] bytes, out int width, out int height, out AVPixelFormat pixFormat)
        {
            // Decode image from byte array;
            AVFormatContext* webcamFormatContext = ffmpeg.avformat_alloc_context();
            var webcamByteReader = new ByteReader();
            webcamByteReader.Buffer = bytes;
            var webcamBuffer = ffmpeg.av_malloc(4096);
            var webcamAllocContext = ffmpeg.avio_alloc_context((byte*)webcamBuffer, 4096, 0, null,
                (avio_alloc_context_read_packet_func)webcamByteReader.Read, null,
                (avio_alloc_context_seek_func)webcamByteReader.Seek);
            if (webcamAllocContext == null)
            {
                throw new NullReferenceException(nameof(webcamAllocContext));
            }

            webcamFormatContext->pb = webcamAllocContext;

            ffmpeg.avformat_open_input(&webcamFormatContext, "nofile.jpg", null, null).ThrowExceptionIfError();
            ffmpeg.avformat_find_stream_info(webcamFormatContext, null).ThrowExceptionIfError();

            var webcamCodecCtx = webcamFormatContext->streams[0]->codec;

            AVCodec* webcamCodec = ffmpeg.avcodec_find_decoder(webcamCodecCtx->codec_id);

            try
            {
                ffmpeg.avcodec_open2(webcamCodecCtx, webcamCodec, null).ThrowExceptionIfError();
                var webcamFrame = ffmpeg.av_frame_alloc();
                AVPacket pkt;
                while (ffmpeg.av_read_frame(webcamFormatContext, &pkt).ThrowExceptionIfError() >= 0)
                {
                    if (pkt.stream_index != 0)
                        continue;

                    int error = 0;
                    do
                    {
                        ffmpeg.avcodec_send_packet(webcamCodecCtx, &pkt).ThrowExceptionIfError();
                        ffmpeg.avcodec_receive_frame(webcamCodecCtx, webcamFrame).ThrowExceptionIfError();
                    } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

                    error.ThrowExceptionIfError();

                    width = webcamCodecCtx->width;
                    height = webcamCodecCtx->height;
                    pixFormat = webcamCodecCtx->pix_fmt;

                    return webcamFrame;
                }
            }
            finally
            {
                GC.KeepAlive(webcamByteReader);

                ffmpeg.avcodec_close(webcamCodecCtx);
                ffmpeg.avformat_close_input(&webcamFormatContext);
                ffmpeg.avio_context_free(&webcamAllocContext);
                ffmpeg.avformat_free_context(webcamFormatContext);
            }

            width = 0;
            height = 0;
            pixFormat = 0;
            return null;
        }


        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}