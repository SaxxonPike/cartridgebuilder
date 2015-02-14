using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CartridgeBuilder.Lib
{
    public struct FileSettings
    {
        public bool AddLoad;
        public int Align;
        public bool Basic;
        public int Fill;
        public bool HasLoad;
        public bool Hidden;
        public bool Hirom;
        public int Length;
        public int Load;
        public bool Lorom;
        public Offset Offset;
        public bool RemoveLoad;
        public string Section;
        public string Source;
        public int Start;
        public string Target;
    }

    class QueuedFile
    {
        public int Checksum;
        public byte[] Data;
        public bool Dir;
        public bool NoOverwrite;
        public int Offset;
        public FileSettings Settings;
    }
}
