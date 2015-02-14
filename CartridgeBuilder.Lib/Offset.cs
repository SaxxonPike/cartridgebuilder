using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CartridgeBuilder.Lib
{
    public struct Offset
    {
        public int Bank;
        public int Chip;
        public int Offs;

        public Offset(int offset)
        {
            Bank = 0;
            Chip = 0;
            Offs = offset;
        }

        public Offset(int bank, int chip, int offset)
        {
            Bank = bank;
            Chip = chip;
            Offs = offset;
        }
    }
}
