using FFmpeg.AutoGen;

namespace RaspberryStreamer
{
    public class ByteReader
    {
        long _currentPosition;

        public ByteReader()
        {
            _currentPosition = 0;
        }

        public byte[] Buffer { get; set; }

        public unsafe int Read(void* _, byte* buf, int buf_size)
        {
            int size = buf_size;
            if (Buffer.Length - _currentPosition < buf_size)
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

        public unsafe long Seek(void* _, long offset, int whence)
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