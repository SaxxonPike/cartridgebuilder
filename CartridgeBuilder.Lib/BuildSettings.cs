using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CartridgeBuilder.Lib
{
    public struct BuildSettings
    {
        public int Banks;
        public int BankSize;
        public Offset Boot;
        public string BootFile;
        public int ChipsPerBank;
        public bool Exrom;
        public int Fill;
        public bool Game;
        public bool Hirom;
        public bool LengthTC;
        public bool Lorom;
        public string Name;
        public bool OffsetBanked;
        public int Type;
    }
}
