using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using Microsoft.Xna.Framework.Content.Pipeline.Processors;
using ToolBelt;
using System.Text;
using System.Drawing;
using System.IO;
using System.Drawing.Imaging;
using Microsoft.Win32;
using System.Xml;

namespace Playroom
{
    [ContentProcessor(DisplayName = "Prism Processor")]
    public class PrismProcessor : ContentProcessor<PrismData, TextureContent>
    {
        private static ParsedPath inkscapeCom;
        
        private class ImagePlacement
        {
            public ImagePlacement(ParsedPath pngFile, System.Drawing.Rectangle targetRectangle)
            {
                this.ImageFile = pngFile;
                this.TargetRectangle = targetRectangle;
            }

            public ParsedPath ImageFile { get; set; }
            public System.Drawing.Rectangle TargetRectangle { get; set; }
        }

        private static ParsedPath InkscapeCom 
        {
            get
            {
                if (inkscapeCom == null)
                {
                    RegistryKey inkscapeKey = Registry.ClassesRoot.OpenSubKey(@"svgfile\shell\Inkscape\command", false);

                    if (inkscapeKey == null)
                        throw new PipelineException(PlayroomResources.InkscapeNotInstalled);

                    string s = (string)inkscapeKey.GetValue("");

                    if (s == null || s.Length < 1)
                        throw new PipelineException(PlayroomResources.InkscapeNotInstalled);

                    if (s[0] == '"')
                        s = s.Substring(1, s.IndexOf('"', 1) - 1);

                    inkscapeCom = new ParsedPath(s, PathType.File).SetExtension(".com");

                    if (!File.Exists(inkscapeCom))
                        throw new PipelineException(PlayroomResources.InkscapeNotInstalled);
                }

                return inkscapeCom;
            }
        }
        private ContentProcessorContext Context { get; set; }

        public override TextureContent Process(PrismData prismData, ContentProcessorContext context)
        {
            ParsedPath intermediateDir = new ParsedPath(context.IntermediateDirectory, PathType.Directory);

            this.Context = context;

            // Make all .SVG paths absolute relative to the .prism file and add dependencies on them
            for (int i = 0; i < prismData.SvgFiles.Count; i++)
            {
                List<ParsedPath> list = prismData.SvgFiles[i];

                for (int j = 0; j < list.Count; j++)
                {
                    list[j] = list[j].MakeFullPath(prismData.PrismFile);

                    context.AddDependency(list[j]);
                }
            }

            // Grab the pinboard data
            prismData.Pinboard = ReadPinboardFile(prismData.PinboardFile);

            ParsedPath tmpPath = new ParsedPath(context.IntermediateDirectory, PathType.Directory);

            List<ImagePlacement> placements = new List<ImagePlacement>();

            try
            {
                // Go through each SVG and output a temporary PNG file.  Create an ImagePlacement for each SVG/PNG processed
                for (int row = 0; row < prismData.SvgFiles.Count; row++)
                {
                    List<ParsedPath> pathList = prismData.SvgFiles[row];

                    for (int col = 0; col < pathList.Count; col++)
                    {
                        ParsedPath svgFile;

                        svgFile = pathList[col].MakeFullPath(prismData.SvgDirectory == null ? prismData.SvgDirectory : prismData.PrismFile);

                        RectangleInfo rectInfo = prismData.Pinboard.GetRectangleInfoByName(prismData.RectangleName);

                        if (rectInfo == null)
                            throw new InvalidContentException(
                                String.Format("Rectangle '{0}' not found in pinboard '{1}'", prismData.RectangleName, prismData.PinboardFile),
                                new ContentIdentity(prismData.PinboardFile));

                        ParsedPath tmpPngFile = tmpPath.SetFileAndExtension(String.Format("{0}_{1}_{2}.png", prismData.PngFile.File, row, col));

                        placements.Add(new ImagePlacement(tmpPngFile, 
                            new Rectangle(col * rectInfo.Width, row * rectInfo.Height, rectInfo.Width, rectInfo.Height)));

                        ConvertSvgToPng(svgFile, tmpPngFile, rectInfo.Width, rectInfo.Height);
                    }
                }

                if (placements.Count == 1)
                {
                    // If there is just one PNG file, rename it the final PNG.
                    ParsedPath tmpPngFile = tmpPath.SetFileAndExtension(prismData.PngFile.File + "_0_0.png");

                    if (File.Exists(prismData.PngFile))
                        File.Delete(prismData.PngFile);

                    File.Move(tmpPngFile, prismData.PngFile);
                }
                else
                {
                    // If there are multiple, combine all the PNG files into the final PNG 
                    // using the ImagePlacements and delete the temp files.
                    Context.Logger.LogMessage("Combining images for {0}", prismData.PngFile);
                    CombineImages(placements, prismData.PngFile);
                }
            }
            finally
            {
                foreach (var placement in placements)
                {
                    if (File.Exists(placement.ImageFile))
                        File.Delete(placement.ImageFile);
                }
            }

            OpaqueDataDictionary processorParams = new OpaqueDataDictionary();
            ExternalReference<TextureContent> exRef = new ExternalReference<TextureContent>(prismData.PngFile);
            TextureContent textureContent = context.BuildAndLoadAsset<TextureContent, TextureContent>(exRef, null, processorParams, null);

            return textureContent;
        }

        private PinboardData ReadPinboardFile(ParsedPath pinboardFile)
        {
            PinboardData data = null;

            try
            {
                using (XmlReader reader = XmlReader.Create(pinboardFile))
                {
                    data = PinboardDataReaderV1.ReadXml(reader);
                }
            }
            catch (Exception e)
            {
                throw new InvalidContentException(String.Format("Unable to read pinboard file '{0}'", pinboardFile),
                    new ContentIdentity(pinboardFile), e);
            }

            return data;
        }

        private bool CombineImages(List<ImagePlacement> placements, ParsedPath imageFileName)
        {
            try
            {
                int width = 0;
                int height = 0;

                foreach (var placement in placements)
                {
                    if (placement.TargetRectangle.Right > width)
                        width = placement.TargetRectangle.Right;

                    if (placement.TargetRectangle.Bottom > height)
                        height = placement.TargetRectangle.Bottom;
                }

                using (Bitmap combinedImage = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(combinedImage))
                    {
                        foreach (var placement in placements)
                        {
                            using (Bitmap image = new Bitmap(placement.ImageFile))
                            {
                                g.DrawImage(image, placement.TargetRectangle);
                            }
                        }
                    }

                    SavePng(combinedImage, imageFileName);
                }
            }
            catch (Exception e)
            {
                throw new InvalidContentException(String.Format("Unable to combine images for image file '{0}'", imageFileName), 
                    new ContentIdentity(imageFileName), e);
            }

            return true;
        }

        private void SavePng(Image image, string pngFileName)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                image.Save(stream, ImageFormat.Png);

                using (Stream fileStream = new FileStream(pngFileName, FileMode.Create))
                {
                    stream.WriteTo(fileStream);
                }
            }
        }

        private bool ConvertSvgToPng(string svgFile, string pngFile, int width, int height)
        {
            string output;
            string command = string.Format("\"{0}\" \"{1}\" -w {2} -h {3} -e \"{4}\"",
                InkscapeCom, // 0
                svgFile, // 1
                width.ToString(), // 2
                height.ToString(), // 3
                pngFile // 4
                );

            int ret = Command.Run(command, out output);

            if (ret != 0)
            {
                // TODO-john-2012: Error message
                return false;
            }

            return true;
        }
    }
}