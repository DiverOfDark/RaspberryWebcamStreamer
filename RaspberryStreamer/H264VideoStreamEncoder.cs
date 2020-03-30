using System;
using System.Drawing;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace RaspberryStreamer
{
    internal static class FFmpegHelper
    {
        public static unsafe string av_strerror(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong) bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr) buffer);
            return message;
        }

        public static int ThrowExceptionIfError(this int error)
        {
            if (error < 0) throw new ApplicationException(av_strerror(error));
            return error;
        }
    }

    public sealed unsafe class H264VideoStreamEncoder : IDisposable
    {
        private readonly Size _frameSize;
        private readonly int _linesizeU;
        private readonly int _linesizeV;
        private readonly int _linesizeY;
        private readonly AVStream* stream;
        private readonly AVCodec* _pCodec;
        private readonly AVFormatContext* _oc = null;
        private readonly int _uSize;
        private readonly int _ySize;

        public H264VideoStreamEncoder(String filename, int fps, Size frameSize)
        {
            _frameSize = frameSize;

            var codecId = AVCodecID.AV_CODEC_ID_H264;
            _pCodec = ffmpeg.avcodec_find_encoder(codecId);
            if (_pCodec == null) throw new InvalidOperationException("Codec not found.");

            fixed (AVFormatContext** _occ = &_oc)
            {
                ffmpeg.av_register_all(); // Loads the whole database of available codecs and formats.
                AVOutputFormat* fmt = ffmpeg.av_guess_format("mp4", null, null);
                ffmpeg.avformat_alloc_output_context2(_occ, fmt, null, null);
                stream = ffmpeg.avformat_new_stream(_oc, _pCodec);

                stream->codec->width = frameSize.Width;
                stream->codec->height = frameSize.Height;
                stream->codec->time_base = new AVRational {num = 1, den = fps};
                stream->codec->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                ffmpeg.av_opt_set(stream->codec->priv_data, "preset", "slow", 0);

                if ((_oc->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0) // Some formats require a global header.
                    stream->codec->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                ffmpeg.avcodec_open2(stream->codec, _pCodec, null).ThrowExceptionIfError();
                stream->time_base = new AVRational() { num = 1, den = VideoWriter.Fps};
                stream->codec = stream->codec; // Once the codec is set up, we need to let the container know which codec are the streams using, in this case the only (video) stream.

                ffmpeg.av_dump_format(_oc, 0, filename, 1);
                ffmpeg.avio_open(&_oc->pb, filename, ffmpeg.AVIO_FLAG_WRITE);
                ffmpeg.avformat_write_header(_oc, null).ThrowExceptionIfError();

                _linesizeY = frameSize.Width;
                _linesizeU = frameSize.Width / 2;
                _linesizeV = frameSize.Width / 2;

                _ySize = _linesizeY * frameSize.Height;
                _uSize = _linesizeU * frameSize.Height / 2;
            }
        }

        public void Encode(AVFrame frame)
        {
            var pPacket = ffmpeg.av_packet_alloc();
            try
            {
                int error;
                do
                {
                    ffmpeg.avcodec_send_frame(stream->codec, &frame).ThrowExceptionIfError();

                    error = ffmpeg.avcodec_receive_packet(stream->codec, pPacket);
                } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

                error.ThrowExceptionIfError();

                ffmpeg.av_packet_rescale_ts(pPacket, new AVRational {num = 1, den = VideoWriter.Fps}, stream->time_base); // We set the packet PTS and DTS taking in the account our FPS (second argument) and the time base that our selected format uses (third argument).
                pPacket->stream_index = stream->index;
                ffmpeg.av_interleaved_write_frame(_oc, pPacket).ThrowExceptionIfError();
            }
            finally
            {
                ffmpeg.av_packet_unref(pPacket);
            }
        }

        public void Dispose()
        {
            ffmpeg.av_write_trailer(_oc); // Writing the end of the file.
            ffmpeg.avio_closep(&_oc->pb); // Closing the file.
            ffmpeg.avcodec_close(stream->codec);
            ffmpeg.av_free(stream->codec);
            ffmpeg.av_free(_pCodec);
        }
    }
}