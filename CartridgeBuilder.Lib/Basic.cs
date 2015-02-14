using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CartridgeBuilder.Lib
{
    static public class Basic
    {
        static public byte[] Relocate(byte[] data, int address, bool hasload)
        {
            using (MemoryStream mem = new MemoryStream(), source = new MemoryStream(data))
            {
                BinaryReader reader = new BinaryReader(source);
                BinaryWriter writer = new BinaryWriterEx(mem);
                int offset = address;
                int lineNumber;

                if (hasload)
                {
                    reader.ReadInt16();
                    writer.Write((byte)(address & 0xFF));
                    writer.Write((byte)((address >> 8) & 0xFF));
                }

                while (true)
                {
                    List<byte> lineData = new List<byte>();
                    int nextLine = reader.ReadUInt16();

                    // if our next-line data is zero, we have reached the end
                    if (nextLine <= 0)
                    {
                        writer.Write(new byte[] { 0, 0 });
                        break;
                    }

                    // read in the line number
                    lineNumber = reader.ReadUInt16();

                    // keep reading bytes until we hit 00
                    while (true)
                    {
                        byte b = reader.ReadByte();
                        lineData.Add(b);
                        if (b == 0)
                            break;
                    }

                    // need to account for the 4 info bytes per basic line
                    int length = lineData.Count + 4;

                    // write out the info bytes
                    writer.Write((Int16)(offset + length));
                    writer.Write((Int16)(lineNumber));
                    offset += length;

                    // write out the line data
                    writer.Write(lineData.ToArray());
                    writer.Flush();
                }

                // return the new relocated basic program
                mem.Flush();
                return mem.ToArray();
            }
        }
    }
}
