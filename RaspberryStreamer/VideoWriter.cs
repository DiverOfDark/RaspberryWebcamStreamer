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

        public VideoWriter(string filename, int width, int height, AVPixelFormat webcamPixelFormat, StreamerSettings settings)
        {
            _fps = settings.FPS;

            _videoFlipperConverter = new VideoFlipperConverter(width, height, webcamPixelFormat, settings);

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

        public void WriteFrame(AVFrame* frame)
        {
            try
            {
                var flippedFrame = _videoFlipperConverter.FlipFrame(frame);
                flippedFrame->pts = _frameCounter;
                WriteVideoFrame(flippedFrame);
                ffmpeg.av_frame_unref(flippedFrame);
            }
            finally
            {
                ffmpeg.av_frame_free(&frame);
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
                ffmpeg.av_packet_free(&pPacket);
            }
        }
    }
}