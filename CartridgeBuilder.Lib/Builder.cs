using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CartridgeBuilder.Lib
{
    public class Builder
    {
        private int cartSize;
        private int chipSize;
        private List<QueuedFile> queue = new List<QueuedFile>();
        private List<ChipRegion> regions = new List<ChipRegion>();
        private BuildSettings settings;

        public Builder(BuildSettings settings)
        {
            this.settings = settings;
            chipSize = settings.BankSize / 2;
            cartSize = 0;
        }

        // create a checksum for data
        int Checksum(byte[] data)
        {
            int sum = 0x12345678;
            foreach (byte b in data)
            {
                sum ^= b;
                sum <<= 1;
                if (sum < 0)
                    sum ^= 1;
            }
            return sum;
        }

        // add a bank
        void Expand()
        {
            regions.Add(new ChipRegion(chipSize));
            regions.Add(new ChipRegion(chipSize));
            cartSize += settings.BankSize;
        }

        // export to bytes
        public byte[] Export()
        {
            Update();
            using (MemoryStream mem = new MemoryStream())
            {
                byte[] buffer = new byte[chipSize];
                foreach (ChipRegion region in regions)
                {
                    Array.Copy(region.Data, buffer, chipSize);
                    for (int i = 0; i < chipSize; i++)
                    {
                        if (!region.Used[i])
                            buffer[i] = (byte)(settings.Fill & 0xFF);
                    }
                    mem.Write(buffer, 0, chipSize);
                    mem.Flush();
                }
                return mem.ToArray();
            }
        }

        // find best fit for data of a given size and rom restriction
        int FitRegion(int start, int size, bool lorom, bool hirom)
        {
            int largestSpace = 0;
            int largestSpaceOffset = regions.Count * chipSize;
            int space = 0;
            int spaceOffset = 0;
            int index = 0;

            foreach (ChipRegion chip in regions)
            {
                if ((lorom || ((index & 1) == 1)) && (hirom || ((index & 1) == 0)))
                {
                    foreach (Region region in chip.GetFreeRegions())
                    {
                        int thisOffset = GetOffset(new Offset(index / 2, index & 1, region.Start));
                        if (thisOffset < start)
                            continue;

                        if (!region.Used)
                        {
                            if (space == 0)
                            {
                                spaceOffset = thisOffset;
                            }
                            space += region.Length;
                        }
                        else
                        {
                            // see if the region we found is the largest
                            if (space > largestSpace)
                            {
                                largestSpace = space;
                                largestSpaceOffset = spaceOffset;
                            }
                            space = 0;
                        }
                    }
                }
                index++;

                // break early if we found a suitable location
                if (space >= size)
                    break;
            }

            if (space > largestSpace)
            {
                largestSpace = space;
                largestSpaceOffset = spaceOffset;
            }

            if (largestSpace < size)
                largestSpaceOffset = regions.Count * chipSize;

            return largestSpaceOffset;
        }

        // convert Offset to absolute
        int GetOffset(Offset offset)
        {
            return (offset.Bank * settings.BankSize) + (offset.Chip * chipSize) + (offset.Offs);
        }

        // find region that matches this offset
        ChipRegion GetRegion(int offset)
        {
            int index = offset / chipSize;

            while (regions.Count <= index)
            {
                Expand();
            }

            return regions[offset / chipSize];
        }

        // get the real length of the data (without load address)
        int GetTrueLength(QueuedFile file)
        {
            if (file.Settings.HasLoad || file.Settings.AddLoad)
                return file.Data.Length - 2;
            return file.Data.Length;
        }

        // get the real offset of the data (without load address)
        int GetTrueOffset(QueuedFile file)
        {
            if (file.Settings.HasLoad || file.Settings.AddLoad)
                return file.Offset + 2;
            return file.Offset;
        }

        // apply all queued files
        public void Update()
        {
            Encoding enc = Encoding.GetEncoding(437);

            List<QueuedFile> patches = new List<QueuedFile>();
            List<QueuedFile> files = new List<QueuedFile>();
            List<QueuedFile> dirs = new List<QueuedFile>();
            List<QueuedFile> unsortedFiles = new List<QueuedFile>();
            Dictionary<int, QueuedFile> fileHashTable = new Dictionary<int, QueuedFile>();

            // separate patches from files and dirs
            foreach (QueuedFile file in queue)
            {
                if (file.NoOverwrite)
                    files.Add(file);
                else
                    if (file.Dir)
                        dirs.Add(file);
                    else
                        patches.Add(file);
            }
            unsortedFiles.AddRange(files.ToArray());

            // create directory map
            int fileCount = files.Count;
            int[] fileMap = new int[fileCount];
            for (int i = 0; i < fileCount; i++)
                fileMap[i] = i;

            // sort files by size (decreasing)
            while (true)
            {
                bool finished = true;

                for (int i = 1; i < fileCount; i++)
                {
                    if (files[i].Data.Length > files[i - 1].Data.Length)
                    {
                        QueuedFile temp = files[i];
                        files[i] = files[i - 1];
                        files[i - 1] = temp;
                        int tempint = fileMap[i];
                        fileMap[i] = fileMap[i - 1];
                        fileMap[i - 1] = tempint;
                        finished = false;
                    }
                }

                if (finished)
                    break;
            }

            // check for duplicates
            for (int i = 0; i < fileCount; i++)
            {
                QueuedFile file = files[i];
                if (!file.Dir)
                {
                    if (!fileHashTable.ContainsKey(file.Checksum))
                    {
                        fileHashTable.Add(file.Checksum, file);
                    }
                    else
                    {
                        files[i] = fileHashTable[file.Checksum];
                    }
                }
            }

            // apply patches
            foreach (QueuedFile file in patches)
            {
                WriteRegion(file.Settings, file.Data, file.Offset, false);
            }

            // blank dirs are written the first time so files can navigate around them
            foreach (QueuedFile file in dirs)
            {
                if (file.Settings.Source.Contains(":") && file.Settings.Source.Split(':')[0].ToLowerInvariant() == "easyfs")
                    file.Data = new byte[0x18 * (fileCount + 1)];
                WriteRegion(file.Settings, file.Data, file.Offset, false);
            }

            // apply files
            List<QueuedFile> addedFiles = new List<QueuedFile>();
            for (int i = 0; i < fileCount; i++)
            {
                if (fileMap[i] == 70)
                {
                }
                QueuedFile file = files[i];
                if (!addedFiles.Contains(file))
                {
                    file.Offset = FitRegion(file.Offset, file.Data.Length, file.Settings.Lorom, file.Settings.Hirom);
                    WriteRegion(file.Settings, file.Data, file.Offset, true);
                    addedFiles.Add(file);
                }
                else
                {
                    unsortedFiles[fileMap[i]].Offset = file.Offset;
                }
            }

            // process and write dirs now that we know where the files are
            foreach (QueuedFile file in dirs)
            {
                string dirKey;
                string dirName;

                if (file.Settings.Source.Contains(":"))
                {
                    var keySections = file.Settings.Source.Split(':');
                    dirKey = keySections[0];
                    dirName = keySections[keySections.Length - 1].ToLowerInvariant();
                    fileCount = 0;
                    foreach (QueuedFile f in files)
                    {
                        if (f.Settings.Section.ToLowerInvariant() == dirName)
                            fileCount++;
                    }
                }
                else
                {
                    dirKey = file.Settings.Source;
                    dirName = "";
                    fileCount = files.Count;
                }

                for (int i = 0; i < file.Data.Length; i++)
                {
                    file.Data[i] = (byte)(settings.Fill & 0xFF);
                }

                int idx = 0;
                switch (dirKey.ToLowerInvariant())
                {
                    case "bankhigh":
                        foreach (QueuedFile f in unsortedFiles)
                        {
                            if (dirName == "" || dirName == f.Settings.Section.ToLowerInvariant())
                            {
                                file.Data[idx] = (byte)((GetTrueOffset(f) / settings.BankSize) >> 8);
                                idx++;
                            }
                        }
                        break;
                    case "banklow":
                        foreach (QueuedFile f in unsortedFiles)
                        {
                            if (dirName == "" || dirName == f.Settings.Section.ToLowerInvariant())
                            {
                                file.Data[idx] = (byte)((GetTrueOffset(f) / settings.BankSize) & 0xFF);
                                idx++;
                            }
                        }
                        break;
                    case "easyfs":
                        for (idx = 0; idx < fileCount; idx++)
                        {
                            QueuedFile currentFile = unsortedFiles[idx];
                            byte[] byteName = enc.GetBytes(currentFile.Settings.Target);
                            int offset = idx * 0x18;
                            Array.Resize(ref byteName, 0x10);
                            Array.Copy(byteName, 0, file.Data, offset + 0, 16);

                            if (!currentFile.Settings.Lorom)
                                file.Data[offset + 16] = 0x03;
                            else if (!currentFile.Settings.Hirom)
                                file.Data[offset + 16] = 0x02;
                            else
                                file.Data[offset + 16] = 0x01;
                            if (currentFile.Settings.Hidden)
                                file.Data[offset + 16] |= 0x80;
                            file.Data[offset + 16] |= 0x60;
                            file.Data[offset + 17] = (byte)(currentFile.Offset / settings.BankSize);
                            file.Data[offset + 18] = 0;
                            file.Data[offset + 19] = (byte)(currentFile.Offset & 0xFF);
                            file.Data[offset + 20] = (byte)((currentFile.Offset & 0x3F00) >> 8);
                            file.Data[offset + 21] = (byte)((currentFile.Data.Length) & 0xFF);
                            file.Data[offset + 22] = (byte)((currentFile.Data.Length >> 8) & 0xFF);
                            file.Data[offset + 23] = (byte)((currentFile.Data.Length >> 16) & 0xFF);
                        }
                        break;
                    case "lengthhigh":
                        foreach (QueuedFile f in unsortedFiles)
                        {
                            if (dirName == "" || dirName == f.Settings.Section.ToLowerInvariant())
                            {
                                int offset = GetTrueLength(f);
                                if (settings.LengthTC)
                                    offset = (offset ^ 0xFFFF) + 1;
                                file.Data[idx] = (byte)((offset >> 8) & 0xFF);
                                idx++;
                            }
                        }
                        break;
                    case "lengthlow":
                        foreach (QueuedFile f in unsortedFiles)
                        {
                            if (dirName == "" || dirName == f.Settings.Section.ToLowerInvariant())
                            {
                                int offset = GetTrueLength(f);
                                if (settings.LengthTC)
                                    offset = (offset ^ 0xFFFF) + 1;
                                file.Data[idx] = (byte)(offset & 0xFF);
                                idx++;
                            }
                        }
                        break;
                    case "loadhigh":
                        foreach (QueuedFile f in unsortedFiles)
                        {
                            if (dirName == "" || dirName == f.Settings.Section.ToLowerInvariant())
                            {
                                file.Data[idx] = (byte)((f.Settings.Load >> 8) & 0xFF);
                                idx++;
                            }
                        }
                        break;
                    case "loadlow":
                        foreach (QueuedFile f in unsortedFiles)
                        {
                            if (dirName == "" || dirName == f.Settings.Section.ToLowerInvariant())
                            {
                                file.Data[idx] = (byte)((f.Settings.Load) & 0xFF);
                                idx++;
                            }
                        }
                        break;
                    case "offsethigh":
                        foreach (QueuedFile f in unsortedFiles)
                        {
                            if (dirName == "" || dirName == f.Settings.Section.ToLowerInvariant())
                            {
                                file.Data[idx] = (byte)(((GetTrueOffset(f) >> 8) & 0x3F) | 0x80);
                                idx++;
                            }
                        }
                        break;
                    case "offsetlow":
                        foreach (QueuedFile f in unsortedFiles)
                        {
                            if (dirName == "" || dirName == f.Settings.Section.ToLowerInvariant())
                            {
                                file.Data[idx] = (byte)(GetTrueOffset(f) & 0xFF);
                                idx++;
                            }
                        }
                        break;
                    case "name":
                        if (file.Settings.Source.Split(':').Length > 2)
                        {
                            int nameIndex = Convert.ToInt32(file.Settings.Source.Split(':')[1]);
                            foreach (QueuedFile f in unsortedFiles)
                            {
                                byte[] byteName = enc.GetBytes(f.Settings.Target);
                                if (dirName == "" || dirName == f.Settings.Section.ToLowerInvariant())
                                {
                                    if (nameIndex >= byteName.Length)
                                        file.Data[idx] = 0;
                                    else
                                        file.Data[idx] = byteName[nameIndex];
                                    idx++;
                                }
                            }
                        }
                        break;
                    default:
                        Debug.Assert(false, "Unknown directory type");
                        break;
                }

                WriteRegion(file.Settings, file.Data, file.Offset, false);
            }

            queue.Clear();
        }

        // build directories separately
        public void WriteDir(FileSettings file)
        {
            QueuedFile qf = new QueuedFile();
            qf.Data = new byte[256];
            qf.Dir = true;
            qf.NoOverwrite = false;
            qf.Offset = GetOffset(file.Offset);
            qf.Settings = file;

            if (qf.Offset < 0)
                qf.Offset = 0;

            queue.Add(qf);
        }

        // files write only in the empty space between patches
        public void WriteFile(FileSettings file, byte[] data)
        {
            QueuedFile qf = new QueuedFile();
            qf.Checksum = Checksum(data);
            qf.Data = data;
            qf.Dir = false;
            qf.NoOverwrite = true;
            qf.Offset = GetOffset(file.Offset);
            qf.Settings = file;

            //if (qf.Offset < 0)
            //    qf.Offset = 0;

            queue.Add(qf);
        }

        // patches overwrite everything in the order they are received
        public void WritePatch(FileSettings file, byte[] data)
        {
            QueuedFile qf = new QueuedFile();
            qf.Checksum = Checksum(data);
            qf.Data = data;
            qf.Dir = false;
            qf.NoOverwrite = false;
            qf.Offset = GetOffset(file.Offset);
            qf.Settings = file;

            if (qf.Offset < 0)
                qf.Offset = 0;

            queue.Add(qf);
        }

        // write region data
        void WriteRegion(FileSettings file, byte[] data, int offset, bool noOverwrite = false)
        {
            int length = data.Length;
            int writeOffset = (offset % chipSize);
            int writeSize = chipSize - writeOffset;
            int sourceOffset = 0;
            while (length > 0)
            {
                int chipIndex = offset / chipSize;
                if ((file.Lorom || ((chipIndex & 1) == 1)) && (file.Hirom || ((chipIndex & 1) == 0)))
                {
                    if (writeSize > length)
                        writeSize = length;
                    GetRegion(offset).Write(data, sourceOffset, writeOffset, noOverwrite);
                    length -= writeSize;
                    sourceOffset += writeSize;
                    writeSize = chipSize;
                    writeOffset = 0;
                }
                offset += chipSize;
            }
        }
    }
}
