﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt;
using System.IO;
using System.Xml;
using Cairo;

namespace Playroom
{
	/// <summary>
	/// .svg and .pinboard to .xnb converter.  Generates a .xnb containing the SVG image(s)
	/// scaled to the size of a given rectangle in the Pinboard file.  The resulting image
	/// may contain multiple rows of images.
	/// 
	/// Properties:
	/// 
	/// Rectangle - the rectangle name in the pinboard to use for scaling each SVG image
	/// Rows - number of rows in the resulting image
	/// </summary>
    class SvgAndPinboardToXnbConverter : IContentCompiler
    {
        #region IContentCompiler Members

        public string[] InputExtensions
        {
            get { return new string[] { ".svg", ".pinboard" }; }
        }

        public string[] OutputExtensions
        {
            get { return new string[] { ".xnb" }; }
        }

        public BuildContext Context { get; set; }
        public BuildTarget Target { get; set; }

        public void Compile()
		{
			IEnumerable<ParsedPath> svgFileNames = Target.InputFiles.Where(f => f.Extension == ".svg");
			ParsedPath pinboardFileName = Target.InputFiles.Where(f => f.Extension == ".pinboard").First();
			ParsedPath xnbFileName = Target.OutputFiles.Where(f => f.Extension == ".xnb").First();
			PinboardFileV1 pinboardFile = PinboardFileCache.Load(pinboardFileName);
			List<ImagePlacement> placements = new List<ImagePlacement>();
			string rectangleName = this.Target.Properties.GetRequiredValue("Rectangle");
			PinboardFileV1.RectangleInfo rectInfo = pinboardFile.GetRectangleInfoByName(rectangleName);

			if (rectInfo == null)
				throw new ContentFileException("Rectangle {0} not found in pinboard file {1}".CultureFormat(rectangleName, pinboardFileName));

			string converterName = Target.Properties.GetOptionalValue("Converter", "Inkscape").ToLower();

			if (converterName != "inkscape" && converterName != "rsvg")
				throw new ContentFileException("Unknown SVG converter '{0}'".CultureFormat(converterName));

			ParsedPath outputRootDir = new ParsedPath(this.Target.Properties.GetRequiredValue("OutputRootDir"), PathType.File);

            try
            {
				int row = 0;

				foreach (var svgFileName in svgFileNames)
                {
					int col = 0;

                    if (rectInfo == null)
                        throw new InvalidOperationException(
                            "Rectangle '{0}' not found in pinboard '{1}'".CultureFormat(rectangleName, pinboardFileName));

                    ParsedPath pngFile = outputRootDir.SetFileAndExtension(String.Format("{0}_{1}_{2}.png", 
                  		svgFileName, row, col));

                    placements.Add(new ImagePlacement(pngFile,
                        new Rectangle(col * rectInfo.Width, row * rectInfo.Height, rectInfo.Width, rectInfo.Height)));

                    switch (converterName)
                    {
                        default:
                        case "rsvg":
                            ImageTools.SvgToPngWithRSvg(svgFileName, pngFile, rectInfo.Width, rectInfo.Height);
                            break;

                        case "inkscape":
                            ImageTools.SvgToPngWithInkscape(svgFileName, pngFile, rectInfo.Width, rectInfo.Height);
                            break;
                    }
                }

				ImageTools.CombinePngs(placements, xnbFileName.SetExtension(".png"));
            }
            finally
            {
                foreach (var placement in placements)
                {
                    if (File.Exists(placement.ImageFile))
                        File.Delete(placement.ImageFile);
                }
            }
        }

        #endregion
    }
}
