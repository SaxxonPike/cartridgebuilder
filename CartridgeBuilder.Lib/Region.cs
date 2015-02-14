using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CartridgeBuilder.Lib
{
    class ChipRegion
    {
        public byte[] Data;
        public int Length;
        public bool[] Used;

        public ChipRegion(int length)
        {
            Length = length;
            Data = new byte[length];
            Used = new bool[length];
        }

        public Region[] GetAllRegions()
        {
            return GetRegions();
        }

        public Region[] GetFreeRegions()
        {
            return GetRegions(true, false);
        }

        Region[] GetRegions(bool selective = false, bool used = false)
        {
            List<Region> result = new List<Region>();
            bool state = Used[0];
            int start = 0;

            for (int i = 1; i <= Length; i++)
            {
                if (i == Length || state != Used[i])
                {
                    Region region = new Region();
                    region.Length = i - start;
                    region.Start = start;
                    region.Used = state;
                    start = i;
                    if (region.Length > 0 && (selective || region.Used == used))
                        result.Add(region);
                    state = !state;
                }
            }

            return result.ToArray();
        }

        public Region[] GetUsedRegions()
        {
            return GetRegions(true, true);
        }

        public void Write(byte[] data, int sourceOffset, int targetOffset, bool noOverwrite = false)
        {
            int targetLength = data.Length - sourceOffset;
            if (targetLength > (Length - targetOffset))
                targetLength = (Length - targetOffset);
            for (int i = 0; i < targetLength; i++)
            {
                Debug.Assert(!noOverwrite || !Used[targetOffset], "Overwritten data");
                Data[targetOffset] = data[sourceOffset];
                Used[targetOffset] = true;
                sourceOffset++;
                targetOffset++;
            }
        }
    }

    struct Region
    {
        public int Length;
        public int Start;
        public bool Used;
    }
}
