using CartridgeBuilder.Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CartridgeBuilder
{
    struct ParseResult
    {
        public BuildSettings Build;
        public FileSettings[] Files;
        public FileSettings[] Patches;
    }
}
