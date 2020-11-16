using System;
using System.Drawing;
using System.Linq;

namespace BarCodeReader
{
    public static class DataRectangleOfDoubleExtensions
    {
        public static Bitmap RenderIntensityMap(this DataRectangle<double> source)
        {
            var minValue = source.Enumerate().Min(pointAndValue => pointAndValue.Item2);
            var maxValue = source.Enumerate().Max(pointAndValue => pointAndValue.Item2);
            var range = maxValue - minValue;
            var normalisedValues = source.Transform(value => (value - minValue) / range);
            var image = new Bitmap(source.Width, source.Height);
            normalisedValues.Enumerate().ToList().ForEach(pointAndNormalisedValue =>
            {
                var intensity = (range == 0) ? 0 : (int)Math.Round(255 * pointAndNormalisedValue.Item2);
                image.SetPixel(pointAndNormalisedValue.Item1.X, pointAndNormalisedValue.Item1.Y, Color.FromArgb(intensity, intensity, intensity));
            });
            return image;
        }
    }
}