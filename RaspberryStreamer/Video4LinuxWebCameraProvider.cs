using System;
using System.Threading;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace RaspberryStreamer
{
    public class Video4LinuxWebCameraProvider : IWebCamera
    {
        private readonly ILogger<Video4LinuxWebCameraProvider> _logger;
        private readonly StreamerSettings _settings;
        
        private unsafe AVFormatContext* _fmtCtx;
        private readonly unsafe AVCodecContext* _videoDecCtx;
        private unsafe AVFrame* _frame;
        private unsafe AVPacket* _pkt;
        private readonly int _videoStreamIdx;

        public unsafe Video4LinuxWebCameraProvider(ILogger<Video4LinuxWebCameraProvider> logger, StreamerSettings settings)
        {
            _logger = logger;
            _settings = settings;
            Width = 1280;
            Height = 720;

            AVDictionary* options = null;

            var ifmt = ffmpeg.av_find_input_format("video4linux2");
            if (ifmt == null)
            {
                throw new Exception("Cannot find input format");
            }

            _fmtCtx = ffmpeg.avformat_alloc_context();
            if (_fmtCtx == null)
            {
                throw new Exception("Cannot allocate input format (Out of memory?)");
            }

            // Enable non-blocking mode
            _fmtCtx->flags |= ffmpeg.AVFMT_FLAG_NONBLOCK;

            // framerate needs to set before opening the v4l2 device
            ffmpeg.av_dict_set(&options, "framerate", "15", 0);
            // This will not work if the camera does not support h264. In that case
            // remove this line. I wrote this for Raspberry Pi where the camera driver
            // can stream h264.
            ffmpeg.av_dict_set(&options, "input_format", "h264", 0);
            ffmpeg.av_dict_set(&options, "video_size", "320x224", 0);

            // open input file, and allocate format context
            fixed(AVFormatContext** fmtCtxAddr = &_fmtCtx)
                ffmpeg.avformat_open_input(fmtCtxAddr, settings.WebCamDevice, ifmt, &options).ThrowExceptionIfError();

            // retrieve stream information
            ffmpeg.avformat_find_stream_info(_fmtCtx, null).ThrowExceptionIfError();

            AVDictionary* opts = null;

            var ret1 = ffmpeg.av_find_best_stream(_fmtCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            ret1.ThrowExceptionIfError();
            _videoStreamIdx = ret1;
            var st = _fmtCtx->streams[_videoStreamIdx];

            // find decoder for the stream
            var decCtx = st->codec;
            var dec = ffmpeg.avcodec_find_decoder(decCtx->codec_id);
            if (dec == null)
            {
                throw new Exception("Failed to find video codec");
            }

            // Init the decoders, with or without reference counting
            ffmpeg.av_dict_set(&opts, "refcounted_frames", "1", 0);
            ffmpeg.avcodec_open2(decCtx, dec, &opts).ThrowExceptionIfError();
            var video_stream = _fmtCtx->streams[_videoStreamIdx];
            _videoDecCtx = video_stream->codec;

            // dump input information to stderr
            ffmpeg.av_dump_format(_fmtCtx, 0, settings.WebCamDevice, 0);

            if (video_stream == null)
            {
                throw new Exception("Could not find video stream in the input, aborting");
            }

            _frame = ffmpeg.av_frame_alloc();
            
            ffmpeg.av_init_packet(_pkt);
            _pkt->data = null;
            _pkt->size = 0;
        }


        public unsafe AVFrame* GetFrame()
        {
            fixed (AVPacket* pktAddr = &_pkt)
            {
                int ret;
                do
                {
                    ret = ffmpeg.av_read_frame(_fmtCtx, pktAddr);
                } while (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN));
                ret.ThrowExceptionIfError();
                
                do
                {
                    if (_pkt->stream_index == _videoStreamIdx)
                    {
                        int error;
                        do
                        {
                            ffmpeg.avcodec_send_packet(_videoDecCtx, pktAddr).ThrowExceptionIfError();
                            error = ffmpeg.avcodec_receive_frame(_videoDecCtx, _frame);
                        } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));
                        error.ThrowExceptionIfError();
                    }

                    ret = _pkt->size;
                    if (ret < 0)
                        break;
                    _pkt->data += ret;
                    _pkt->size -= ret;
                } while (_pkt->size > 0);

                return _frame;
            }
        }

        public void Start(CancellationToken token)
        {
        }

        public int Width { get; }
        public int Height { get; }
        public AVPixelFormat PixelFormat { get; private set; }

        public unsafe void Dispose()
        {
            ffmpeg.avcodec_close(_videoDecCtx);
            fixed (AVFormatContext** fmtCtxAddr = &_fmtCtx)
                ffmpeg.avformat_close_input(fmtCtxAddr);

            fixed (AVFrame** frameAddr = &_frame) 
                ffmpeg.av_frame_free(frameAddr);
        }
    }
}

