using System;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace RaspberryStreamer
{
    // TODO Guess width/height from stream instead of configuring it in code
    // TODO Add FlipX/FlipY filters
    // TODO Get picture from v4l2 instead of URL / jpeg decode
    public unsafe class VideoWriter : IDisposable
    {
        private readonly int _fps;
        private readonly AVCodec* _h264Codec;
        private readonly AVStream* _h264Stream;
        private readonly AVFormatContext* _h264AvFormatContext = null;
        private SwsContext* _convertContext = null;
        private readonly void* _convertBuffer;
        private readonly AVFilterGraph* _filterGraph;
        private readonly AVFilterContext* _filterSourceContext;
        private readonly AVFilterContext* _filterSinkContext;
        private int _frameCounter;

        public VideoWriter(string filename, StreamerSettings settings)
        {
            _fps = settings.FPS;
            var width = settings.Width;
            var height = settings.Height;

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

            var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_YUV420P, _h264Stream->codec->width, _h264Stream->codec->height, 1);
            _convertBuffer = ffmpeg.av_malloc((ulong)convertedFrameBufferSize);

            {
                string filters = $"buffer=width={width}:height={height}:pix_fmt=13:time_base=1/1:pixel_aspect=1/1 [in]; [out] buffersink;[in] vflip [in1];[in1] format=pix_fmts=0 [out]";
                AVFilterInOut* gis = null;
                AVFilterInOut* gos = null;

                _filterGraph = ffmpeg.avfilter_graph_alloc();
                ffmpeg.avfilter_graph_parse2(_filterGraph, filters, &gis, &gos).ThrowExceptionIfError();
                ffmpeg.avfilter_graph_config(_filterGraph, null).ThrowExceptionIfError();

                _filterSourceContext = ffmpeg.avfilter_graph_get_filter(_filterGraph, "Parsed_buffer_0");
                _filterSinkContext = ffmpeg.avfilter_graph_get_filter(_filterGraph, "Parsed_buffersink_1");
                if (_filterSourceContext == null || _filterSinkContext == null)
                    throw new Exception("Failed to create filter sinks");
            }
        }

        public void WriteFrame(byte[] bytes)
        {
            var webcamFrame = GetAVFrameFromWebcamBytes(bytes, out var webcamWidth, out var webcamHeight, out var webcamPixFormat);
            try
            {
                var videoFrame = ConvertFromWebcamToVideo(webcamFrame, webcamWidth, webcamHeight, webcamPixFormat);
                try
                {
                    videoFrame->pts = _frameCounter;
                    videoFrame->format = webcamFrame->format;
                    var flippedFrame = FlipFrame(webcamFrame);
                    try
                    {
                        flippedFrame->pts = _frameCounter;
                        flippedFrame->format = webcamFrame->format;
                        WriteVideoFrame(flippedFrame);
                    }
                    finally
                    {
                        ffmpeg.av_frame_free(&flippedFrame);
                    }
                }
                finally
                {
                    ffmpeg.av_frame_free(&videoFrame);
                }
            }
            finally
            {
                ffmpeg.av_frame_free(&webcamFrame);
            }
            _frameCounter++;
        }

        public void Dispose()
        {
            if (_convertContext != null)
            {
                ffmpeg.sws_freeContext(_convertContext);
            }

            ffmpeg.av_write_trailer(_h264AvFormatContext); // Writing the end of the file.
            ffmpeg.avio_closep(&_h264AvFormatContext->pb); // Closing the file.
            ffmpeg.avcodec_close(_h264Stream->codec);
            ffmpeg.av_free(_h264Stream->codec);
            ffmpeg.av_free(_h264Codec);
            ffmpeg.av_free(_convertBuffer);
            fixed (AVFilterGraph** filterGraphAddr = &_filterGraph)
            {
                ffmpeg.avfilter_graph_free(filterGraphAddr);
            }
        }

        private AVFrame* FlipFrame(AVFrame* source)
        {
            var flippedFrame = ffmpeg.av_frame_alloc();
            var flippedFrameBuffer = (byte*) ffmpeg.av_malloc((ulong) ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_YUV420P, _h264Stream->codec->width, _h264Stream->codec->height, 1));
            var dataArr = new byte_ptrArray4();
            dataArr.UpdateFrom(flippedFrame->data);
            var linesizeArr = new int_array4();
            linesizeArr.UpdateFrom(flippedFrame->linesize);
            ffmpeg.av_image_fill_arrays(ref dataArr, ref linesizeArr, flippedFrameBuffer, AVPixelFormat.AV_PIX_FMT_YUV420P, _h264Stream->codec->width, _h264Stream->codec->height, 1);
            flippedFrame->data.UpdateFrom(dataArr);
            flippedFrame->linesize.UpdateFrom(linesizeArr);

            ffmpeg.av_buffersrc_add_frame(_filterSourceContext, source).ThrowExceptionIfError();
            ffmpeg.av_buffersink_get_frame(_filterSinkContext, flippedFrame).ThrowExceptionIfError();

            return flippedFrame;
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

        private AVFrame* ConvertFromWebcamToVideo(AVFrame* webcamFrame, int webcamWidth, int webcamHeight, AVPixelFormat webcamPixFormat)
        {
            if (_convertContext == null)
            {
                _convertContext = ffmpeg.sws_getContext(webcamWidth,
                    webcamHeight,
                    webcamPixFormat,
                    _h264Stream->codec->width,
                    _h264Stream->codec->height,
                    AVPixelFormat.AV_PIX_FMT_YUV420P,
                    ffmpeg.SWS_FAST_BILINEAR,
                    null,
                    null,
                    null);
            }

            var convertDstData = new byte_ptrArray4();
            var convertDstLinesize = new int_array4();

            ffmpeg.av_image_fill_arrays(ref convertDstData, ref convertDstLinesize, (byte*)_convertBuffer, AVPixelFormat.AV_PIX_FMT_YUV420P, _h264Stream->codec->width, _h264Stream->codec->height, 1).ThrowExceptionIfError();
            ffmpeg.sws_scale(_convertContext, webcamFrame->data, webcamFrame->linesize, 0, webcamFrame->height, convertDstData, convertDstLinesize).ThrowExceptionIfError();

            var data = new byte_ptrArray8();
            data.UpdateFrom(convertDstData);
            var linesize = new int_array8();
            linesize.UpdateFrom(convertDstLinesize);

            var convertedFrame = ffmpeg.av_frame_alloc();
            convertedFrame->data = data;
            convertedFrame->linesize = linesize;
            convertedFrame->width = _h264Stream->codec->width;
            convertedFrame->height = _h264Stream->codec->height;
            convertedFrame->pts = _frameCounter;
            return convertedFrame;
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