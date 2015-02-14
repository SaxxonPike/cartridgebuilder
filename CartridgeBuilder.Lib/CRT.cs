using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CartridgeBuilder.Lib
{
    public class CRT
    {
        static public byte[] Build(BuildSettings settings, byte[] image)
        {
            using (MemoryStream mem = new MemoryStream())
            {
                Encoding enc = Encoding.GetEncoding(437);
                BinaryWriterEx writer = new BinaryWriterEx(mem);

                // id
                writer.Write(enc.GetBytes("C64 CARTRIDGE   "));

                // header length
                writer.WriteS((Int32)0x00000040);

                // cartridge version
                writer.WriteS((Int16)0x0100);

                // hardware type
                writer.WriteS((Int16)settings.Type);

                // exrom line
                writer.Write((byte)(settings.Exrom ? 0x01 : 0x00));

                // game line
                writer.Write((byte)(settings.Game ? 0x01 : 0x00));

                // reserved
                writer.Write(new byte[6]);

                // cartridge name
                byte[] name = enc.GetBytes(settings.Name);
                Array.Resize(ref name, 0x20);
                writer.Write(name);

                // cartridge contents
                int bankIndex = 0;
                int bankSize = settings.BankSize;
                int chipSize = settings.BankSize / settings.ChipsPerBank;
                int chipIndex = 0;
                int chipAddress;
                int remaining = image.Length;
                int chipsPerBank = 0x4000 / chipSize;
                int imageOffset = 0;

                while (remaining > 0)
                {
                    bankIndex = (chipIndex / chipsPerBank);
                    chipAddress = 0x8000 | ((chipIndex % chipsPerBank) * chipSize);

                    // id
                    writer.Write(enc.GetBytes("CHIP"));

                    // packet length
                    writer.WriteS((Int32)(chipSize + 0x10));

                    // chip type
                    writer.WriteS((Int16)0x0002);

                    // bank number
                    writer.WriteS((Int16)bankIndex);

                    // load address
                    writer.WriteS((Int16)chipAddress);

                    // image size
                    writer.WriteS((Int16)chipSize);

                    // image data
                    writer.Flush();
                    mem.Write(image, imageOffset, chipSize);
                    imageOffset += chipSize;
                    mem.Flush();
                    remaining -= chipSize;

                    chipIndex++;
                }

                return mem.ToArray();
            }
        }
    }
}
