using CartridgeBuilder.Lib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CartridgeBuilder
{
    class Program
    {
        static BuildSettings buildSettings;
        static FileSettings fileSettings;
        static bool cartDefined = false;
        static string basePath;

        static List<string> sectionNames = new List<string>();

        static void Main(string[] args)
        {
            //args = new string[]
            //{
            //    @"C:\Users\Tony\Desktop\C64CART\C64CART\combined\list.txt"
            //};

            if (args.Length > 0)
            {
                buildSettings = new BuildSettings();
                buildSettings.Banks = 0;
                buildSettings.BankSize = 0x4000;
                buildSettings.Boot = new Offset(0);
                buildSettings.BootFile = "";
                buildSettings.ChipsPerBank = 2;
                buildSettings.Exrom = false;
                buildSettings.Fill = 0xFF;
                buildSettings.Game = false;
                buildSettings.Hirom = true;
                buildSettings.LengthTC = false;
                buildSettings.Lorom = true;
                buildSettings.Name = "";
                buildSettings.OffsetBanked = false;
                buildSettings.Type = 0;

                fileSettings.AddLoad = false;
                fileSettings.Align = 0;
                fileSettings.Basic = false;
                fileSettings.Fill = buildSettings.Fill;
                fileSettings.HasLoad = false;
                fileSettings.Hidden = false;
                fileSettings.Hirom = buildSettings.Hirom;
                fileSettings.Length = -1;
                fileSettings.Load = 0;
                fileSettings.Lorom = buildSettings.Lorom;
                fileSettings.Offset = new Offset(-1);
                fileSettings.RemoveLoad = false;
                fileSettings.Section = "";
                fileSettings.Source = "";
                fileSettings.Start = -1;
                fileSettings.Target = "";

                basePath = Path.GetDirectoryName(args[0]);
                Parser parser = Parser.Parse(File.ReadAllLines(args[0]));
                if (!Debugger.IsAttached)
                {
                    try
                    {
                        Build(Parse(parser));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("FAILED at line " + parser.CurrentIndex.ToString() + ": " + ex.Message);
                    }
                }
                else
                {
                    Build(Parse(parser));
                }
            }
        }

        static void Build(ParseResult config)
        {
            Builder builder = new Builder(config.Build);
            BuildApply(builder, builder.WritePatch, builder.WriteDir, config.Patches);
            BuildApply(builder, builder.WriteFile,builder.WriteDir, config.Files);
            byte[] exported = builder.Export();
            File.WriteAllBytes(Path.Combine(basePath, "@-output.bin"), exported);
            File.WriteAllBytes(Path.Combine(basePath, "@-output.crt"), CRT.Build(buildSettings, exported));
        }

        static void BuildApply(Builder builder, Action<FileSettings, byte[]> function, Action<FileSettings> dirFunction, FileSettings[] list)
        {
            foreach (FileSettings fileEntry in list)
            {
                FileSettings file = fileEntry;
                int dataLength = 0;
                byte[] data = new byte[0];
                bool dir = false;

                // get file data
                if (file.Source.Contains(":"))
                {
                    if (file.Source.Length > 0 && File.Exists(file.Source))
                        data = File.ReadAllBytes(file.Source);
                    else
                    {
                        dir = true;
                        data = new byte[256];
                    }
                }
                else
                {
                    if (file.Source.Length > 0 && File.Exists(Path.Combine(basePath, file.Source)))
                        data = File.ReadAllBytes(Path.Combine(basePath, file.Source));
                }
                dataLength = data.Length;

                // pad the file if desired
                if (file.Length > 0)
                    Array.Resize<byte>(ref data, file.Length);
                int dataArrayLength = data.Length;
                for (int i = dataLength; i < dataArrayLength; i++)
                {
                    data[i] = (byte)(file.Fill);
                }

                // remove load address
                if (file.HasLoad && file.RemoveLoad && data.Length >= 2)
                {
                    byte[] newData = new byte[data.Length - 2];
                    Array.Copy(data, 2, newData, 0, newData.Length);
                    data = newData;
                    file.HasLoad = false;
                }

                // add load address
                if (!file.HasLoad && file.AddLoad)
                {
                    byte[] newData = new byte[data.Length + 2];
                    Array.Copy(data, 0, newData, 2, data.Length);
                    newData[0] = (byte)(file.Load & 0xFF);
                    newData[1] = (byte)((file.Load >> 8) & 0xFF);
                    data = newData;
                    file.HasLoad = true;
                }

                // relocate BASIC things
                if (file.Basic)
                {
                    if (!file.HasLoad)
                    {
                        byte[] basicBytes = new byte[data.Length - 2];
                        Array.Copy(data, 2, basicBytes, 0, basicBytes.Length);
                        basicBytes = Basic.Relocate(basicBytes, file.Load, file.HasLoad);
                        data = new byte[basicBytes.Length + 2];
                        Array.Copy(basicBytes, 0, data, 2, basicBytes.Length);
                        data[0] = (byte)(file.Load & 0xFF);
                        data[1] = (byte)(file.Load >> 8);
                    }
                    else
                    {
                        data = Basic.Relocate(data, file.Load, file.HasLoad);
                    }
                }

                if (file.HasLoad)
                {
                    file.Load = data[0] | ((int)data[1] << 8);
                }

                if (!dir)
                    function(file, data);
                else
                    dirFunction(file);
            }
        }

        static ParseResult Parse(Parser parser)
        {
            ParseResult result = new ParseResult();
            List<FileSettings> files = new List<FileSettings>();
            List<FileSettings> patches = new List<FileSettings>();

            while (!parser.EOF)
            {
                string containerType = parser.GetString();
                switch (containerType.ToUpperInvariant())
                {
                    case "CARTRIDGE":
                        Debug.Assert(!cartDefined, "Multiple CARTRIDGE sections");
                        ParseCartridge(parser);
                        break;
                    case "PATCHES":
                        Debug.Assert(cartDefined, "CARTRIDGE section must be defined before PATCHES");
                        patches.AddRange(ParsePatches(parser));
                        break;
                    case "FILES":
                        Debug.Assert(cartDefined, "CARTRIDGE section must be defined before FILES");
                        files.AddRange(ParseFiles(parser));
                        break;
                    default:
                        Debug.Assert(false, "Unknown section");
                        break;
                }
            }

            result.Build = buildSettings;
            result.Files = files.ToArray();
            result.Patches = patches.ToArray();
            return result;
        }

        static void ParseCartridge(Parser parser)
        {
            string sectionName = parser.GetString();
            Debug.Assert(sectionName.Length <= 0 || !sectionNames.Contains(sectionName), "Duplicate section");
            parser.AssertOpen();

            while (!(parser.CurrentLine == "}"))
            {
                Debug.Assert(parser.CurrentLine != "{", "Unexpected {");
                Debug.Assert(!parser.EOF, "Unexpected EOF");

                string entryKey = parser.GetCompoundString();
                parser.AssertEquals();

                switch (entryKey.ToLowerInvariant())
                {
                    case "banks":
                        buildSettings.Banks = parser.GetInteger();
                        break;
                    case "banksize":
                        buildSettings.BankSize = parser.GetInteger();
                        break;
                    case "boot":
                        buildSettings.Boot = parser.GetOffset();
                        break;
                    case "bootfile":
                        buildSettings.BootFile = parser.GetCompoundString();
                        break;
                    case "chipsperbank":
                        buildSettings.ChipsPerBank = parser.GetInteger();
                        break;
                    case "exrom":
                        buildSettings.Exrom = parser.GetBool();
                        break;
                    case "fill":
                        buildSettings.Fill = parser.GetInteger();
                        break;
                    case "game":
                        buildSettings.Game = parser.GetBool();
                        break;
                    case "lengthtc":
                        buildSettings.LengthTC = parser.GetBool();
                        break;
                    case "name":
                        buildSettings.Name = parser.GetCompoundString();
                        break;
                    case "offsetbanked":
                        buildSettings.OffsetBanked = parser.GetBool();
                        break;
                    case "roms":
                        switch (parser.GetCompoundString().ToLowerInvariant())
                        {
                            case "low":
                                buildSettings.Lorom = true;
                                buildSettings.Hirom = false;
                                break;
                            case "high":
                                buildSettings.Lorom = false;
                                buildSettings.Hirom = true;
                                break;
                            case "both":
                                buildSettings.Lorom = true;
                                buildSettings.Hirom = true;
                                break;
                            default:
                                Debug.Assert(false, "Invalid ROMS type");
                                break;
                        }
                        break;
                    case "type":
                        buildSettings.Type = parser.GetInteger();
                        break;
                    default:
                        Debug.Assert(false, "Unknown CARTRIDGE key");
                        break;
                }
                parser.AssertEnd();
            }
            parser.AssertClose();
            cartDefined = true;
        }

        static FileSettings ParseFileInfo(Parser parser)
        {
            string sourceFile = parser.GetCompoundString();
            FileSettings fs = ParseFileInfoSettings(parser);
            fs.Source = sourceFile;
            return fs;
        }

        static FileSettings ParseFileInfoSettings(Parser parser)
        {
            FileSettings result = fileSettings;
            while (true)
            {
                string key = parser.GetParameter();
                if (key == "")
                    break;
                switch (key.ToLowerInvariant())
                {
                    case "addload":
                        result.AddLoad = parser.GetBool();
                        break;
                    case "align":
                        result.Align = parser.GetInteger();
                        break;
                    case "basic":
                        result.Basic = parser.GetBool();
                        break;
                    case "fill":
                        result.Fill = parser.GetInteger();
                        break;
                    case "hasload":
                        result.HasLoad = parser.GetBool();
                        break;
                    case "hidden":
                        result.Hidden = parser.GetBool();
                        break;
                    case "length":
                        result.Length = parser.GetInteger();
                        break;
                    case "load":
                        result.Load = parser.GetInteger();
                        break;
                    case "offset":
                        result.Offset = parser.GetOffset();
                        break;
                    case "removeload":
                        result.RemoveLoad = parser.GetBool();
                        break;
                    case "roms":
                        switch (parser.GetCompoundString().ToLowerInvariant())
                        {
                            case "low":
                                result.Lorom = true;
                                result.Hirom = false;
                                break;
                            case "high":
                                result.Lorom = false;
                                result.Hirom = true;
                                break;
                            case "both":
                                result.Lorom = true;
                                result.Hirom = true;
                                break;
                            default:
                                Debug.Assert(false, "Invalid ROMS type");
                                break;
                        }
                        break;
                    case "source":
                        result.Source = parser.GetString();
                        break;
                    case "start":
                        result.Start = parser.GetInteger();
                        break;
                    default:
                        Debug.Assert(false, "Invalid file property");
                        break;
                }
            }
            return result;
        }

        static FileSettings[] ParseFiles(Parser parser)
        {
            fileSettings = new FileSettings();
            fileSettings.Fill = buildSettings.Fill;
            fileSettings.Hirom = buildSettings.Hirom;
            fileSettings.Lorom = buildSettings.Lorom;

            List<FileSettings> result = new List<FileSettings>();
            string sectionName = parser.GetString();
            Debug.Assert(sectionName.Length <= 0 || !sectionNames.Contains(sectionName), "Duplicate section");
            parser.AssertOpen();

            while (!(parser.CurrentLine == "}"))
            {
                Debug.Assert(parser.CurrentLine != "{", "Unexpected {");
                Debug.Assert(!parser.EOF, "Unexpected EOF");

                string targetMode = parser.GetString();
                parser.AssertColon();

                string targetfile;
                FileSettings fs;

                switch (targetMode.ToLowerInvariant())
                {
                    case "file":
                        targetfile = parser.GetCompoundString();
                        parser.AssertEquals();
                        fs = ParseFileInfo(parser);
                        fs.Target = targetfile;
                        fs.Section = sectionName;
                        result.Add(fs);
                        break;
                    case "option":
                        fileSettings = ParseFileInfoSettings(parser);
                        break;
                }
                parser.AssertEnd();
            }
            parser.AssertClose();

            return result.ToArray();
        }

        static FileSettings[] ParsePatches(Parser parser)
        {
            List<FileSettings> result = new List<FileSettings>();
            string sectionName = parser.GetString();
            Debug.Assert(sectionName.Length <= 0 || !sectionNames.Contains(sectionName), "Duplicate section");
            parser.AssertOpen();

            while (!(parser.CurrentLine == "}"))
            {
                Debug.Assert(parser.CurrentLine != "{", "Unexpected {");
                Debug.Assert(!parser.EOF, "Unexpected EOF");

                FileSettings fs;

                Offset offs = parser.GetOffset();
                parser.AssertEquals();
                fs = ParseFileInfo(parser);
                fs.Target = "";
                fs.Offset = offs;
                fs.Section = sectionName;
                result.Add(fs);
                parser.AssertEnd();
            }
            parser.AssertClose();

            return result.ToArray();
        }
    }
}
