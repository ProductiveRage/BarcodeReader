using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace BarCodeReader
{
    class Program
    {
        static void Main()
        {
            const bool saveInterimProgressImages = true; // Set to true to write progress images as the processing occurs

            using var image = new Bitmap("WikiEAN13Example.png");

            var barcodeValues = new List<string>();
            var possibleBarcodeAreas = GetPossibleBarcodeAreasForBitmap(image, saveInterimProgressImages);
            if (saveInterimProgressImages)
            {
                using var possibleBarcodeAreaHighlightBitmap = new Bitmap(image.Width, image.Height);
                using (var g = Graphics.FromImage(possibleBarcodeAreaHighlightBitmap))
                {
                    var fullSourceImageRectangle = new Rectangle(0, 0, image.Width, image.Height);
                    g.DrawImage(image, destRect: fullSourceImageRectangle, srcRect: fullSourceImageRectangle, srcUnit: GraphicsUnit.Pixel);
                    if (possibleBarcodeAreas.Any())
                    {
                        // Outline identified "possible barcode" areas in the progress / preview bitmap - make the outline big enough to see on large images but not so huge that
                        // they cover much content on smaller images (that calculations here are quite arbitrary but it's only for a sanity check for it doesn't really matter)
                        var penWidth = Math.Max(2, Math.Min(8, (int)Math.Round(Math.Max(image.Width, image.Width) / 500d)));
                        using var outliner = new Pen(Color.YellowGreen, penWidth);
                        g.DrawRectangles(outliner, possibleBarcodeAreas.ToArray());
                    }
                }
                possibleBarcodeAreaHighlightBitmap.Save("PossibleBarcodeAreas.jpg", ImageFormat.Jpeg);
            }
            foreach (var area in possibleBarcodeAreas)
            {
                using var areaBitmap = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(areaBitmap))
                {
                    g.DrawImage(image, destRect: new Rectangle(0, 0, areaBitmap.Width, areaBitmap.Height), srcRect: area, srcUnit: GraphicsUnit.Pixel);
                }
                var valueFromBarcode = TryToReadBarcodeValue(areaBitmap);
                if (valueFromBarcode is object)
                    barcodeValues.Add(valueFromBarcode);
            }

            if (!barcodeValues.Any())
                Console.WriteLine("Couldn't read any bar codes from the source image :(");
            else
            {
                Console.WriteLine("Read the following bar code(s) from the image:");
                foreach (var barcodeValue in barcodeValues)
                    Console.WriteLine("- " + barcodeValue);
            }

            Console.WriteLine();
            Console.WriteLine("Press [Enter] to terminate..");
            Console.ReadLine();
        }

        private static IEnumerable<Rectangle> GetPossibleBarcodeAreasForBitmap(Bitmap image, bool saveInterimProgressImages)
        {
            var greyScaleImageData = GetGreyscaleData(
                image,
                resizeIfLargestSideGreaterThan: 450,
                resizeTo: 300
            );
            var combinedGradients = greyScaleImageData.Transform((intensity, pos) =>
            {
                // Consider gradients to be zero at the edges of the image because there aren't pixels
                // both left/right or above/below and so it's not possible to calculate a real value
                var horizontalChange = (pos.X == 0) || (pos.X == greyScaleImageData.Width - 1)
                    ? 0
                    : greyScaleImageData[pos.X + 1, pos.Y] - greyScaleImageData[pos.X - 1, pos.Y];
                var verticalChange = (pos.Y == 0) || (pos.Y == greyScaleImageData.Height - 1)
                    ? 0
                    : greyScaleImageData[pos.X, pos.Y + 1] - greyScaleImageData[pos.X, pos.Y - 1];
                return Math.Max(0, Math.Abs(horizontalChange) - Math.Abs(verticalChange));
            });

            if (saveInterimProgressImages)
            {
                using var gradientPreview = combinedGradients.RenderIntensityMap();
                gradientPreview.Save("CombinedGradients.jpg", ImageFormat.Jpeg);
            }

            const int maxRadiusForGradientBlurring = 2;
            const double thresholdForMaskingGradients = 1d / 3;

            // Determine how much the image was scaled down (if it had to be scaled down at all)
            // by comparing the width of the potentially-scaled-down data to the source image
            var reducedImageSideBy = (double)image.Width / greyScaleImageData.Width;

            var mask = Blur(Normalise(combinedGradients), maxRadiusForGradientBlurring)
                .Transform(value => (value >= thresholdForMaskingGradients));

            if (saveInterimProgressImages)
            {
                using var gradientPreview = mask.Transform(value => value ? 1d : 0).RenderIntensityMap();
                gradientPreview.Save("Mask.jpg", ImageFormat.Jpeg);
            }

            return GetOverlappingObjectBounds(GetDistinctObjects(mask))
                .Where(boundedObject => boundedObject.Width > boundedObject.Height)
                .Select(boundedObject =>
                {
                    var expandedBounds = boundedObject;
                    expandedBounds.Inflate(width: expandedBounds.Width / 10, height: 0);
                    expandedBounds.Intersect(
                        Rectangle.FromLTRB(0, 0, greyScaleImageData.Width, greyScaleImageData.Height)
                    );
                    return new Rectangle(
                        x: (int)(expandedBounds.X * reducedImageSideBy),
                        y: (int)(expandedBounds.Y * reducedImageSideBy),
                        width: (int)(expandedBounds.Width * reducedImageSideBy),
                        height: (int)(expandedBounds.Height * reducedImageSideBy)
                    );
                });
        }

        private static DataRectangle<double> GetGreyscaleData(Bitmap image, int resizeIfLargestSideGreaterThan, int resizeTo)
        {
            var largestSide = Math.Max(image.Width, image.Height);
            if (largestSide <= resizeIfLargestSideGreaterThan)
                return image.GetGreyscale();

            int width, height;
            if (image.Width > image.Height)
            {
                width = resizeTo;
                height = (int)(((double)image.Height / image.Width) * width);
            }
            else
            {
                height = resizeTo;
                width = (int)(((double)image.Width / image.Height) * height);
            }
            using var resizedImage = new Bitmap(image, width, height);
            return resizedImage.GetGreyscale();
        }

        private static DataRectangle<double> Normalise(DataRectangle<double> values)
        {
            var max = values.Enumerate().Max(pointAndValue => pointAndValue.Item2);
            return (max == 0)
                ? values
                : values.Transform(value => (value / max));
        }

        private static DataRectangle<double> Blur(DataRectangle<double> values, int maxRadius)
        {
            return values.Transform((value, point) =>
            {
                var valuesInArea = new List<double>();
                for (var x = -maxRadius; x <= maxRadius; x++)
                {
                    for (var y = -maxRadius; y <= maxRadius; y++)
                    {
                        var newPoint = new Point(point.X + x, point.Y + y);
                        if ((newPoint.X < 0) || (newPoint.Y < 0)
                        || (newPoint.X >= values.Width) || (newPoint.Y >= values.Height))
                            continue;
                        valuesInArea.Add(values[newPoint.X, newPoint.Y]);
                    }
                }
                return valuesInArea.Average();
            });
        }

        private static IEnumerable<IEnumerable<Point>> GetDistinctObjects(DataRectangle<bool> mask)
        {
            // Flood fill areas in the looks-like-bar-code mask to create distinct areas
            var allPoints = new HashSet<Point>(
                mask.Enumerate(optionalFilter: (point, isMasked) => isMasked).Select(point => point.Item1)
            );
            while (allPoints.Any())
            {
                var currentPoint = allPoints.First();
                var pointsInObject = GetPointsInObject(currentPoint).ToArray();
                foreach (var point in pointsInObject)
                    allPoints.Remove(point);
                yield return pointsInObject;
            }

            // Inspired by code at
            // https://simpledevcode.wordpress.com/2015/12/29/flood-fill-algorithm-using-c-net/
            IEnumerable<Point> GetPointsInObject(Point startAt)
            {
                var pixels = new Stack<Point>();
                pixels.Push(startAt);

                var valueAtOriginPoint = mask[startAt.X, startAt.Y];
                var filledPixels = new HashSet<Point>();
                while (pixels.Count > 0)
                {
                    var currentPoint = pixels.Pop();
                    if ((currentPoint.X < 0) || (currentPoint.X >= mask.Width)
                    || (currentPoint.Y < 0) || (currentPoint.Y >= mask.Height))
                        continue;

                    if ((mask[currentPoint.X, currentPoint.Y] == valueAtOriginPoint)
                    && !filledPixels.Contains(currentPoint))
                    {
                        filledPixels.Add(new Point(currentPoint.X, currentPoint.Y));
                        pixels.Push(new Point(currentPoint.X - 1, currentPoint.Y));
                        pixels.Push(new Point(currentPoint.X + 1, currentPoint.Y));
                        pixels.Push(new Point(currentPoint.X, currentPoint.Y - 1));
                        pixels.Push(new Point(currentPoint.X, currentPoint.Y + 1));
                    }
                }
                return filledPixels;
            }
        }

        private static IEnumerable<Rectangle> GetOverlappingObjectBounds(IEnumerable<IEnumerable<Point>> objects)
        {
            // Translate each "object" (a list of connected points) into a bounding box (squared off if
            // it was taller than it was wide)
            var squaredOffBoundedObjects = new HashSet<Rectangle>(
                objects.Select((points, index) =>
                {
                    var bounds = Rectangle.FromLTRB(
                        points.Min(p => p.X),
                        points.Min(p => p.Y),
                        points.Max(p => p.X) + 1,
                        points.Max(p => p.Y) + 1
                    );
                    if (bounds.Height > bounds.Width)
                        bounds.Inflate((bounds.Height - bounds.Width) / 2, 0);
                    return bounds;
                })
            );

            // Loop over the boundedObjects and reduce the collection by merging any two rectangles
            // that overlap and then starting again until there are no more bounds merges to perform
            while (true)
            {
                var combinedOverlappingAreas = false;
                foreach (var bounds in squaredOffBoundedObjects)
                {
                    foreach (var otherBounds in squaredOffBoundedObjects)
                    {
                        if (otherBounds == bounds)
                            continue;

                        if (bounds.IntersectsWith(otherBounds))
                        {
                            squaredOffBoundedObjects.Remove(bounds);
                            squaredOffBoundedObjects.Remove(otherBounds);
                            squaredOffBoundedObjects.Add(Rectangle.FromLTRB(
                                Math.Min(bounds.Left, otherBounds.Left),
                                Math.Min(bounds.Top, otherBounds.Top),
                                Math.Max(bounds.Right, otherBounds.Right),
                                Math.Max(bounds.Bottom, otherBounds.Bottom)
                            ));
                            combinedOverlappingAreas = true;
                            break;
                        }
                    }
                    if (combinedOverlappingAreas)
                        break;
                }
                if (!combinedOverlappingAreas)
                    break;
            }

            return squaredOffBoundedObjects.Select(bounds =>
            {
                var allPointsWithinBounds = objects
                    .Where(points => points.Any(point => bounds.Contains(point)))
                    .SelectMany(points => points)
                    .ToArray(); // Don't re-evaluate in the four accesses below
                return Rectangle.FromLTRB(
                    left: allPointsWithinBounds.Min(p => p.X),
                    right: allPointsWithinBounds.Max(p => p.X) + 1,
                    top: allPointsWithinBounds.Min(p => p.Y),
                    bottom: allPointsWithinBounds.Max(p => p.Y) + 1
                );
            });
        }

        public static string? TryToReadBarcodeValue(Bitmap subImage)
        {
            const double threshold = 0.5;

            // Black lines are considered 1 and so we set to true if it's a dark pixel (and 0 if light)
            var mask = subImage.GetGreyscale().Transform(intensity => intensity < (256 * threshold));
            for (var y = 0; y < mask.Height; y++)
            {
                var value = TryToReadBarcodeValueFromSingleLine(mask, y);
                if (value is object)
                    return value;
            }
            return null;
        }

        private static string? TryToReadBarcodeValueFromSingleLine(DataRectangle<bool> barcodeDetails, int sliceY)
        {
            if ((sliceY < 0) || (sliceY >= barcodeDetails.Height))
                throw new ArgumentOutOfRangeException(nameof(sliceY));

            var lengths = GetBarLengthsFromBarcodeSlice(barcodeDetails, sliceY).ToArray();
            if (lengths.Length < 57)
            {
                // As explained, we'd like 60 bars (which would include the final guard region) but we
                // can still make an attempt with 57 (but no fewer)
                // - There will often be another section of blank content after the barcode that we ignore
                // - If we don't want to validate the final guard region then we can work with a barcode
                //   image where some of the end is cut off, so long as the data for the 12 digits is
                //   there (this will be the case where there are only 57 lengths)
                return null;
            }

            var offset = 0;
            var extractedNumericValues = new List<int>();
            for (var i = 0; i < 14; i++)
            {
                if (i == 0)
                {
                    // This should be the first guard region and it should be a pattern of three single-
                    // width bars
                    offset += 3;
                }
                else if (i == 7)
                {
                    // This should be the guard region in the middle of the barcode and it should be a
                    // pattern of five single-width bars
                    offset += 5;
                }
                else
                {
                    var value = TryToGetValueForLengths(
                        lengths[offset],
                        lengths[offset + 1],
                        lengths[offset + 2],
                        lengths[offset + 3]
                    );
                    if (value is null)
                        return null;
                    extractedNumericValues.Add(value.Value);
                    offset += 4;
                }
            }

            // Calculate what the checksum should be based upon the first 11 numbers and ensure that
            // the 12th matches it
            if (extractedNumericValues.Last() != CalculateChecksum(extractedNumericValues.Take(11)))
                return null;

            return string.Join("", extractedNumericValues);
        }

        private static IEnumerable<int> GetBarLengthsFromBarcodeSlice(DataRectangle<bool> barcodeDetails, int sliceY)
        {
            if ((sliceY < 0) || (sliceY >= barcodeDetails.Height))
                throw new ArgumentOutOfRangeException(nameof(sliceY));

            // Take the horizontal slice of the data
            var values = new List<bool>();
            for (var x = 0; x < barcodeDetails.Width; x++)
                values.Add(barcodeDetails[x, sliceY]);

            // Split the slice into bars - we only care about how long each segment is when they
            // alternate, not whether they're dark bars or light bars
            var segments = new List<Tuple<bool, int>>();
            foreach (var value in values)
            {
                if ((segments.Count == 0) || (segments[^1].Item1 != value))
                    segments.Add(Tuple.Create(value, 1));
                else
                    segments[^1] = Tuple.Create(value, segments[^1].Item2 + 1);
            }
            if ((segments.Count > 0) && !segments[0].Item1)
            {
                // Remove the white space before the first bar
                segments.RemoveAt(0);
            }
            return segments.Select(segment => segment.Item2);
        }

        private static int? TryToGetValueForLengths(int l0, int l1, int l2, int l3)
        {
            if (l0 <= 0)
                throw new ArgumentOutOfRangeException(nameof(l0));
            if (l1 <= 0)
                throw new ArgumentOutOfRangeException(nameof(l1));
            if (l2 <= 0)
                throw new ArgumentOutOfRangeException(nameof(l2));
            if (l3 <= 0)
                throw new ArgumentOutOfRangeException(nameof(l3));

            // Take a guess at what the width of a single bar is based upon these four values
            // (the four bars that encode a number should add up to a width of seven)
            var raw = new[] { l0, l1, l2, l3 };
            var singleWidth = raw.Sum() / 7d;
            var adjustment = singleWidth / 10;
            var attemptedSingleWidths = new HashSet<double>();
            while (true)
            {
                var normalised = raw.Select(x => Math.Max(1, (int)Math.Round(x / singleWidth))).ToArray();
                var sum = normalised.Sum();
                if (sum == 7)
                    return TryToGetNumericValue(normalised[0], normalised[1], normalised[2], normalised[3]);

                attemptedSingleWidths.Add(singleWidth);
                if (sum > 7)
                    singleWidth += adjustment;
                else
                    singleWidth -= adjustment;
                if (attemptedSingleWidths.Contains(singleWidth))
                {
                    // If we've already tried this width-of-a-single-bar value then give up -
                    // it doesn't seem like we can make the input values make sense
                    return null;
                }
            }

            static int? TryToGetNumericValue(int i0, int i1, int i2, int i3)
            {
                var lookFor = string.Join("", new[] { i0, i1, i2, i3 });
                var lookup = new[]
                {
                    // These values correspond to the lookup chart shown earlier
                    "3211", "2221", "2122", "1411", "1132", "1231", "1114", "1312", "1213", "3112"
                };
                for (var i = 0; i < lookup.Length; i++)
                {
                    if (lookFor == lookup[i])
                        return i;
                }
                return null;
            }
        }

        private static int CalculateChecksum(IEnumerable<int> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (values.Count() != 11)
                throw new ArgumentException("Should be provided with precisely 11 values");

            // See https://en.wikipedia.org/wiki/Check_digit#UPC
            var checksumTotal = values
                .Select((value, index) => (index % 2 == 0) ? (value * 3) : value)
                .Sum();
            var checksumModulo = checksumTotal % 10;
            if (checksumModulo != 0)
                checksumModulo = 10 - checksumModulo;
            return checksumModulo;
        }
    }
}