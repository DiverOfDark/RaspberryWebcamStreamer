using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace RaspberryStreamer
{
    public class ByteReader
    {
        long _currentPosition;

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly avio_alloc_context_read_packet _readDelegate;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly avio_alloc_context_seek _seekDelegate;

        public unsafe ByteReader()
        {
            _currentPosition = 0;
            _readDelegate = Read;
            _seekDelegate = Seek;
            ReadFunc = new avio_alloc_context_read_packet_func {Pointer = Marshal.GetFunctionPointerForDelegate(_readDelegate)};
            SeekFunc = new avio_alloc_context_seek_func {Pointer = Marshal.GetFunctionPointerForDelegate(_seekDelegate)};
         }

        public byte[] Buffer { get; set; }
        public avio_alloc_context_read_packet_func ReadFunc { get; }
        public avio_alloc_context_seek_func SeekFunc { get; }

        private unsafe int Read(void* _, byte* buf, int bufSize)
        {
            int size = bufSize;
            if (Buffer.Length - _currentPosition < bufSize)
                size = (int) (Buffer.Length - _currentPosition);
            if (size > 0)
            {
                fixed (byte* bytePtr = Buffer)
                {
                    System.Runtime.CompilerServices.Unsafe.CopyBlockUnaligned(buf, bytePtr + _currentPosition, (uint) size);
                }

                _currentPosition += size;
            }

            return size;
        }

        private unsafe long Seek(void* _, long offset, int whence)
        {
            switch (whence)
            {
                case 0:
                    _currentPosition = offset;
                    break;
                case 1:
                    _currentPosition += offset;
                    break;
                case 2:
                    _currentPosition = Buffer.Length - offset;
                    break;
                case ffmpeg.AVSEEK_SIZE:
                    return Buffer.Length;
            }

            return _currentPosition;
        }
    }
}