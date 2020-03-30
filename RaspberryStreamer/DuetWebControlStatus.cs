namespace RaspberryStreamer
{
    public class DuetWebControlStatus
    {
        public string Status { get; set; }

        public bool IsIdle => Status == "I";

        public bool IsPaused => Status == "S";

        public string DetailedStatus
        {
            get
            {
                switch (Status[0]) { 
                    case 'P': return "Printing";
                    case 'F': return "Flashing Firmware";
                    case 'H': return "Halted";
                    case 'D': return "Pausing/Decelerating";
                    case 'S': return "Paused/Stopped";
                    case 'R': return "Resuming";
                    case 'M': return "Simulating";
                    case 'B': return "Busy";
                    case 'T': return "Changing Tool";
                    case 'I': return "Idle";
                    default: return "Unknown";
                }
            }
        }

        public double[] Heaters { get; set; }
        public double[] Active { get; set; }
        public double[] Standby { get; set; }

        public double[] Pos { get; set; }
        public double[] Machine { get; set; }

        public string Resp { get; set; }
    }
}