﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt;

namespace Playroom
{
    public class PrismData
    {
        // Set by reader
        public ParsedPath PrismFile { get; set; }
        public ParsedPath PinboardFile { get; set; }
        public string RectangleName { get; set; }
        public SvgToPngConverter Converter { get; set; }
        public ParsedPath SvgDirectory { get; set; }
        public List<List<ParsedPath>> SvgFiles { get; set; }
        public ParsedPath PngFile { get; set; }
        public PinboardData Pinboard { get; set; }
    }

    public enum SvgToPngConverter
    {
        Default = 0,
        RSvg = 1,
        Inkscape = 2,
        Gimp = 3
    }
}
