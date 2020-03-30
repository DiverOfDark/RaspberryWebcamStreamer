using System;

namespace RaspberryStreamer
{
    public class FileInfoStatus
    {
        public int Err { get; set; }
        public int Size { get; set; }
        public DateTime LastModified { get; set; }
        public double Height { get; set; }
        public double FirstLayerHeight { get; set; }
        public double LayerHeight { get; set; }
        public int PrintTime { get; set; }
        public double[] Filament { get; set; }
        public int PrintDuration { get; set; }
        public string FileName { get; set; }
        public string GeneratedBy { get; set; }

        public string GetFileNameWithoutPath()
        {
            return FileName?.Substring((int) (FileName?.LastIndexOf('/') + 1));
        }
    }
}