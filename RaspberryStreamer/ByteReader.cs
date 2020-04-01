using FFmpeg.AutoGen;

namespace RaspberryStreamer
{
    public struct ByteReader
    {
        private readonly byte[] _buffer;
        long _currentPosition;

        public ByteReader(byte[] buffer)
        {
            _buffer = buffer;
            _currentPosition = 0;
        }

        public unsafe int Read(void* opaque, byte* buf, int buf_size)
        {
            int size = buf_size;
            if (_buffer.Length - _currentPosition < buf_size)
                size = (int) (_buffer.Length - _currentPosition);
            if (size > 0)
            {
                fixed (byte* bytePtr = _buffer)
                {
                    System.Runtime.CompilerServices.Unsafe.CopyBlockUnaligned(buf, bytePtr + _currentPosition, (uint) size);
                }

                _currentPosition += size;
            }

            return size;
        }

        public unsafe long Seek(void* opaque, long offset, int whence)
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
                    _currentPosition = _buffer.Length - offset;
                    break;
                case ffmpeg.AVSEEK_SIZE:
                    return _buffer.Length;
            }

            return _currentPosition;
        }
    }
}