using System;
using FFmpeg.AutoGen;

namespace RaspberryStreamer
{
    public unsafe class VideoFlipperConverter : IDisposable
    {
        private readonly AVFilterContext* _filterSourceContext;
        private readonly AVFilterContext* _filterSinkContext;
        private readonly AVFilterGraph* _filterGraph;
        private readonly AVFrame* _flippedFrame;

        public VideoFlipperConverter(int width, int height, AVPixelFormat inputPixelFormat, StreamerSettings settings)
        {
            string filters = $"buffer=width={width}:height={height}:pix_fmt={(int)inputPixelFormat}:time_base=1/1:pixel_aspect=1/1 [in]; [out] buffersink;[in] format=pix_fmts=0 [in1];";
            int inputCount = 1;
            if (settings.FlipY)
            {
                filters += $"[in{inputCount}] vflip [in{++inputCount}];";
            }
            if (settings.FlipX)
            {
                filters += $"[in{inputCount}] hflip [in{++inputCount}];";
            }

            filters += $"[in{inputCount}] copy [out]";
            AVFilterInOut* gis = null;
            AVFilterInOut* gos = null;

            _filterGraph = ffmpeg.avfilter_graph_alloc();
            ffmpeg.avfilter_graph_parse2(_filterGraph, filters, &gis, &gos).ThrowExceptionIfError();
            ffmpeg.avfilter_graph_config(_filterGraph, null).ThrowExceptionIfError();

            _filterSourceContext = ffmpeg.avfilter_graph_get_filter(_filterGraph, "Parsed_buffer_0");
            _filterSinkContext = ffmpeg.avfilter_graph_get_filter(_filterGraph, "Parsed_buffersink_1");
            if (_filterSourceContext == null || _filterSinkContext == null)
                throw new Exception("Failed to create filter sinks");

            _flippedFrame = ffmpeg.av_frame_alloc();
            var flippedFrameBuffer = (byte*)ffmpeg.av_malloc((ulong)ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_YUV420P, width, height, 1));
            var dataArr = new byte_ptrArray4();
            dataArr.UpdateFrom(_flippedFrame->data);
            var linesizeArr = new int_array4();
            linesizeArr.UpdateFrom(_flippedFrame->linesize);
            ffmpeg.av_image_fill_arrays(ref dataArr, ref linesizeArr, flippedFrameBuffer, AVPixelFormat.AV_PIX_FMT_YUV420P, width, height, 1);
            _flippedFrame->data.UpdateFrom(dataArr);
            _flippedFrame->linesize.UpdateFrom(linesizeArr);
        }

        public AVFrame* FlipFrame(AVFrame* source)
        {
            ffmpeg.av_buffersrc_add_frame(_filterSourceContext, source).ThrowExceptionIfError();
            ffmpeg.av_buffersink_get_frame(_filterSinkContext, _flippedFrame).ThrowExceptionIfError();
            return _flippedFrame;
        }

        public void Dispose()
        {
            fixed (AVFilterGraph** filterGraphAddr = &_filterGraph)
            {
                ffmpeg.avfilter_graph_free(filterGraphAddr);
            }
        }
    }
}