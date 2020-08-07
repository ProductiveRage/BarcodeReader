using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BarCodeReader
{
    public static class BitmapExtensions
    {
        /// <summary>
        /// This will return values in the range 0-255 (inclusive)
        /// </summary>
        // Based on http://stackoverflow.com/a/4748383/3813189
        public static DataRectangle<double> GetGreyscale(this Bitmap image)
        {
            var values = new double[image.Width, image.Height];
            var data = image.LockBits(
                new Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb
            );
            try
            {
                var pixelData = new Byte[data.Stride];
                for (var lineIndex = 0; lineIndex < data.Height; lineIndex++)
                {
                    Marshal.Copy(
                        source: data.Scan0 + (lineIndex * data.Stride),
                        destination: pixelData,
                        startIndex: 0,
                        length: data.Stride
                    );
                    for (var pixelOffset = 0; pixelOffset < data.Width; pixelOffset++)
                    {
                        // Note: PixelFormat.Format24bppRgb means the data is stored in memory as BGR
                        const int PixelWidth = 3;
                        var r = pixelData[pixelOffset * PixelWidth + 2];
                        var g = pixelData[pixelOffset * PixelWidth + 1];
                        var b = pixelData[pixelOffset * PixelWidth];
                        values[pixelOffset, lineIndex] = (0.2989 * r) + (0.5870 * g) + (0.1140 * b);
                    }
                }
            }
            finally
            {
                image.UnlockBits(data);
            }
            return DataRectangle.For(values);
        }
    }
}