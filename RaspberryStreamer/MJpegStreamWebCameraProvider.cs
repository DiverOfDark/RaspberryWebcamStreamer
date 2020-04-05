using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace RaspberryStreamer
{
    public class MJpegStreamWebCameraProvider : IWebCamera
    {
        private readonly ILogger<MJpegStreamWebCameraProvider> _logger;
        private readonly StreamerSettings _settings;
        private readonly HttpClient _httpClient;
        private byte[] _currentFrame;

        public MJpegStreamWebCameraProvider(ILogger<MJpegStreamWebCameraProvider> logger, StreamerSettings settings)
        {
            _logger = logger;
            _settings = settings;
            _httpClient = new HttpClient();
        }

        public void Start(CancellationToken token) => new Thread(Worker).Start(token);

        private async void Worker(object obj)
        {
            var ct = (CancellationToken) obj;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var bytes = await _httpClient.GetByteArrayAsync(_settings.WebCamUrl);

                    if (_currentFrame == null)
                    {
                        unsafe
                        {
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get snapshot from camera");
                }
            }
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public AVPixelFormat PixelFormat { get; private set; }

        public unsafe AVFrame* GetFrame() => GetFrame(_currentFrame, out _, out _, out _);

        private unsafe AVFrame* GetFrame(byte[] bytes, out int width, out int height, out AVPixelFormat pixFormat)
        {
            var webcamFormatContext = ffmpeg.avformat_alloc_context();
            var webcamByteReader = new ByteReader();
            var bufSize = 1048576u;
            var webcamBuffer = ffmpeg.av_malloc(bufSize);

            var webcamAllocContext = ffmpeg.avio_alloc_context((byte*)webcamBuffer, (int) bufSize, 0, null, webcamByteReader.ReadFunc, null, webcamByteReader.SeekFunc);
            if (webcamAllocContext == null)
            {
                throw new NullReferenceException(nameof(webcamAllocContext));
            }

            webcamFormatContext->pb = webcamAllocContext;
            {
                webcamByteReader.Buffer = bytes;
                // Decode image from byte array;
                ffmpeg.avformat_open_input(&webcamFormatContext, "nofile.jpg", null, null).ThrowExceptionIfError();
                ffmpeg.avformat_find_stream_info(webcamFormatContext, null).ThrowExceptionIfError();

                var webcamCodecCtx = webcamFormatContext->streams[0]->codec;

                AVCodec* webcamCodec = ffmpeg.avcodec_find_decoder(webcamCodecCtx->codec_id);

                AVPacket pkt;
                try
                {
                    ffmpeg.avcodec_open2(webcamCodecCtx, webcamCodec, null).ThrowExceptionIfError();
                    var webcamFrame = ffmpeg.av_frame_alloc();
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
                    ffmpeg.avformat_free_context(webcamFormatContext);
                    ffmpeg.av_free(webcamAllocContext->buffer); // webcamBuffer seems like realloc'ed
                    ffmpeg.avio_context_free(&webcamAllocContext);
                    ffmpeg.av_packet_unref(&pkt);
                }

                width = 0;
                height = 0;
                pixFormat = 0;
                return null;
            }
        }


        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}