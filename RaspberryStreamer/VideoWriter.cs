using System;
using FFmpeg.AutoGen;

namespace RaspberryStreamer
{
    // TODO Get picture from v4l2 instead of URL / jpeg decode
    public unsafe class VideoWriter : IDisposable
    {
        private readonly VideoFlipperConverter _videoFlipperConverter;
        private readonly int _fps;
        private readonly AVCodec* _h264Codec;
        private readonly AVStream* _h264Stream;
        private readonly AVFormatContext* _h264AvFormatContext = null;
        private int _frameCounter;

        public VideoWriter(string filename, byte[] frameExample, StreamerSettings settings)
        {
            _fps = settings.FPS;

            var exampleFrame = GetAVFrameFromWebcamBytes(frameExample, out var width, out var height, out var webcamPixelFormat);
            _videoFlipperConverter = new VideoFlipperConverter(width, height, webcamPixelFormat, settings);
            ffmpeg.av_frame_free(&exampleFrame);

            _h264Codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
            if (_h264Codec == null) {
                throw new InvalidOperationException("Codec not found.");
            }

            fixed (AVFormatContext** occ = &_h264AvFormatContext)
            {   
                AVOutputFormat* fmt = ffmpeg.av_guess_format("mp4", null, null);
                ffmpeg.avformat_alloc_output_context2(occ, fmt, null, null);
                _h264Stream = ffmpeg.avformat_new_stream(_h264AvFormatContext, _h264Codec);
                _h264Stream->codec->width = width;
                _h264Stream->codec->height = height;
                _h264Stream->codec->time_base = new AVRational {num = 1, den = _fps};
                _h264Stream->codec->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                ffmpeg.av_opt_set(_h264Stream->codec->priv_data, "preset", "veryslow", 0);

                if ((_h264AvFormatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0) // Some formats require a global header.
                    _h264Stream->codec->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                ffmpeg.avcodec_open2(_h264Stream->codec, _h264Codec, null).ThrowExceptionIfError();
                _h264Stream->time_base = new AVRational() { num = 1, den = _fps};

                ffmpeg.avio_open(&_h264AvFormatContext->pb, filename, ffmpeg.AVIO_FLAG_WRITE);
                ffmpeg.avformat_write_header(_h264AvFormatContext, null).ThrowExceptionIfError();
            }
        }

        public void WriteFrame(byte[] bytes)
        {
            var webcamFrame = GetAVFrameFromWebcamBytes(bytes, out _, out _, out _);
            try
            {
                var flippedFrame = _videoFlipperConverter.FlipFrame(webcamFrame);
                flippedFrame->pts = _frameCounter;
                WriteVideoFrame(flippedFrame);
                ffmpeg.av_frame_unref(flippedFrame);
            }
            finally
            {
                ffmpeg.av_frame_free(&webcamFrame);
            }
            _frameCounter++;
        }

        public void Dispose()
        {
            ffmpeg.av_write_trailer(_h264AvFormatContext); // Writing the end of the file.
            ffmpeg.avio_closep(&_h264AvFormatContext->pb); // Closing the file.
            ffmpeg.avcodec_close(_h264Stream->codec);
            ffmpeg.av_free(_h264Stream->codec);
            ffmpeg.av_free(_h264Codec);
            _videoFlipperConverter.Dispose();
        }

       
        private AVFrame* GetAVFrameFromWebcamBytes(byte[] bytes, out int width, out int height, out AVPixelFormat pixFormat)
        {
            // Decode image from byte array;
            AVFormatContext* webcamFormatContext = ffmpeg.avformat_alloc_context();

            var webcamByteReader = new ByteReader(bytes);
            var webcamBuffer = ffmpeg.av_malloc(4096);
            var webcamAllocContext = ffmpeg.avio_alloc_context((byte*) webcamBuffer, 4096, 0, null, (avio_alloc_context_read_packet_func)webcamByteReader.Read, null, (avio_alloc_context_seek_func) webcamByteReader.Seek);
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

        private void WriteVideoFrame(AVFrame* videoFrame)
        {
            var pPacket = ffmpeg.av_packet_alloc();
            try
            {
                int error = 0;
                do
                {
                    ffmpeg.avcodec_send_frame(_h264Stream->codec, videoFrame).ThrowExceptionIfError();
                    error = ffmpeg.avcodec_receive_packet(_h264Stream->codec, pPacket);
                } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

                error.ThrowExceptionIfError();
                
                ffmpeg.av_packet_rescale_ts(pPacket, new AVRational { num = 1, den = _fps }, _h264Stream->time_base); // We set the packet PTS and DTS taking in the account our FPS (second argument) and the time base that our selected format uses (third argument).
                pPacket->stream_index = _h264Stream->index;
                ffmpeg.av_interleaved_write_frame(_h264AvFormatContext, pPacket).ThrowExceptionIfError();
            }
            finally
            {
                ffmpeg.av_packet_unref(pPacket);
            }
        }
    }
}