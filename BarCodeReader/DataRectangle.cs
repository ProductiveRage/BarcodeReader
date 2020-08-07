using System;
using System.Collections.Generic;
using System.Drawing;

namespace BarCodeReader
{
    public static class DataRectangle
    {
        public static DataRectangle<T> For<T>(T[,] values) => new DataRectangle<T>(values);
    }

    public sealed class DataRectangle<T>
    {
        private readonly T[,] _protectedValues;
        public DataRectangle(T[,] values) : this(values, isolationCopyMayBeBypassed: false) { }
        private DataRectangle(T[,] values, bool isolationCopyMayBeBypassed)
        {
            if ((values.GetLowerBound(0) != 0) || (values.GetLowerBound(1) != 0))
                throw new ArgumentException("Both dimensions must have lower bound zero");
            var arrayWidth = values.GetUpperBound(0) + 1;
            var arrayHeight = values.GetUpperBound(1) + 1;
            if ((arrayWidth == 0) || (arrayHeight == 0))
                throw new ArgumentException("zero element arrays are not supported");

            Width = arrayWidth;
            Height = arrayHeight;

            if (isolationCopyMayBeBypassed)
                _protectedValues = values;
            else
            {
                _protectedValues = new T[Width, Height];
                Array.Copy(values, _protectedValues, Width * Height);
            }
        }

        /// <summary>
        /// This will always be greater than zero
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// This will always be greater than zero
        /// </summary>
        public int Height { get; }

        public T this[int x, int y]
        {
            get
            {
                if ((x < 0) || (x >= Width))
                    throw new ArgumentOutOfRangeException(nameof(x));
                if ((y < 0) || (y >= Height))
                    throw new ArgumentOutOfRangeException(nameof(y));
                return _protectedValues[x, y];
            }
        }

        public IEnumerable<Tuple<Point, T>> Enumerate(Func<Point, T, bool>? optionalFilter = null)
        {
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                {
                    var value = _protectedValues[x, y];
                    var point = new Point(x, y);
                    if (optionalFilter?.Invoke(point, value) ?? true)
                        yield return Tuple.Create(point, value);
                }
            }
        }

        public DataRectangle<TResult> Transform<TResult>(Func<T, TResult> transformer)
        {
            return Transform((value, coordinates) => transformer(value));
        }

        public DataRectangle<TResult> Transform<TResult>(Func<T, Point, TResult> transformer)
        {
            var transformed = new TResult[Width, Height];
            for (var x = 0; x < Width; x++)
            {
                for (var y = 0; y < Height; y++)
                    transformed[x, y] = transformer(_protectedValues[x, y], new Point(x, y));
            }
            return new DataRectangle<TResult>(transformed, isolationCopyMayBeBypassed: true);
        }
    }
}