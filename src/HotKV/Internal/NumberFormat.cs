// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Globalization;
using System.Text;

namespace HotKV.Internal
{
    /// <summary>
    /// The single place that turns floating point and money values into
    /// wire text. Three formatters, three server parsers:
    /// <list type="bullet">
    /// <item><see cref="Double(double)"/> / <see cref="Single(float)"/>: the general rule for nearly every
    /// numeric argument (thresholds, rates, vectors, coordinates, time series values, ...). Plain decimal
    /// with the shortest digits that round-trip, never an exponent, never a leading '+', always the
    /// invariant culture, "-0" written as "0". NaN and infinities are rejected.</item>
    /// <item><see cref="Score(double)"/>: the same, except that positive and negative infinity are written
    /// as "inf" and "-inf" (sorted set scores, score bounds and weights accept them). NaN is rejected.</item>
    /// <item><see cref="Usd(double)"/> / <see cref="Usd(decimal)"/>: the strict fixed-point USD rule of
    /// LLMBUDGET.SET and LLMSTATS.PRICE SET: digits "." up to 6 fractional digits, no sign, no exponent.
    /// A positive amount that would round to zero is rejected because 0 means "no limit" or "free".</item>
    /// </list>
    /// </summary>
    internal static class NumberFormat
    {
        private const decimal MaxUsd = 18446744073709.551615m; // ulong.MaxValue micro-dollars

        /// <summary>Formats a general double argument (see the class remarks).</summary>
        internal static string Double(double value)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentException("NaN is not a valid command argument.", nameof(value));
            }
            if (double.IsInfinity(value))
            {
                throw new ArgumentException("Infinity is not a valid argument for this command; only sorted set scores accept inf.", nameof(value));
            }
            return Plain(value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>Formats a 32-bit float argument using the float32 shortest round-trip digits (see the class remarks).</summary>
        internal static string Single(float value)
        {
            if (float.IsNaN(value))
            {
                throw new ArgumentException("NaN is not a valid command argument.", nameof(value));
            }
            if (float.IsInfinity(value))
            {
                throw new ArgumentException("Infinity is not a valid argument for this command.", nameof(value));
            }
            return Plain(value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>Formats a sorted set score, score bound or weight: like <see cref="Double(double)"/> but infinity is written "inf"/"-inf".</summary>
        internal static string Score(double value)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentException("NaN is not a valid score.", nameof(value));
            }
            if (double.IsPositiveInfinity(value))
            {
                return "inf";
            }
            if (double.IsNegativeInfinity(value))
            {
                return "-inf";
            }
            return Plain(value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>Formats a USD amount for the strict fixed-point parser (see the class remarks).</summary>
        internal static string Usd(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("A USD amount must be a finite number.", nameof(value));
            }
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A USD amount cannot be negative.");
            }
            if (value > (double)MaxUsd)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A USD amount this large does not fit in 64-bit micro-dollars.");
            }
            // The double -> decimal conversion keeps 15 significant digits, which is exactly
            // what a human typed (0.1 + 0.2 is 0.3, not 0.30000000000000004).
            decimal d = (decimal)value;
            if (d == 0m && value > 0)
            {
                // decimal cannot represent it at all (< 1e-28): still a positive amount that rounds to 0.
                throw new ArgumentOutOfRangeException(nameof(value), value, "A positive USD amount below 0.000001 would round to 0, which the server reads as \"no limit\"/\"free\"; pass an amount of at least 0.000001 or exactly 0.");
            }
            return Usd(d);
        }

        /// <summary>Formats a USD amount for the strict fixed-point parser (see the class remarks).</summary>
        internal static string Usd(decimal value)
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A USD amount cannot be negative.");
            }
            if (value > MaxUsd)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A USD amount this large does not fit in 64-bit micro-dollars.");
            }
            decimal rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
            if (rounded == 0m && value != 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A positive USD amount below 0.000001 would round to 0, which the server reads as \"no limit\"/\"free\"; pass an amount of at least 0.000001 or exactly 0.");
            }
            if (rounded > MaxUsd)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A USD amount this large does not fit in 64-bit micro-dollars.");
            }
            string s = rounded.ToString("F6", CultureInfo.InvariantCulture);
            // Trim trailing fractional zeros, keep at least one digit before the point.
            s = s.TrimEnd('0');
            if (s.EndsWith(".", StringComparison.Ordinal))
            {
                s = s.Substring(0, s.Length - 1);
            }
            return s.Length == 0 ? "0" : s;
        }

        /// <summary>
        /// Expands a .NET "R" round-trip string such as "1E-05", "1.5E+21" or "-2.5" into plain
        /// decimal notation, and normalises "-0" to "0".
        /// </summary>
        private static string Plain(string r)
        {
            int e = r.IndexOfAny(new[] { 'E', 'e' });
            string result;
            if (e < 0)
            {
                result = r;
            }
            else
            {
                string mantissa = r.Substring(0, e);
                int exp = int.Parse(r.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                bool negative = mantissa.StartsWith("-", StringComparison.Ordinal);
                if (negative)
                {
                    mantissa = mantissa.Substring(1);
                }
                int dot = mantissa.IndexOf('.');
                string digits = dot < 0 ? mantissa : mantissa.Substring(0, dot) + mantissa.Substring(dot + 1);
                int pointPos = (dot < 0 ? mantissa.Length : dot) + exp; // position of the decimal point within digits
                var sb = new StringBuilder();
                if (negative)
                {
                    sb.Append('-');
                }
                if (pointPos <= 0)
                {
                    sb.Append("0.");
                    sb.Append('0', -pointPos);
                    sb.Append(digits);
                }
                else if (pointPos >= digits.Length)
                {
                    sb.Append(digits);
                    sb.Append('0', pointPos - digits.Length);
                }
                else
                {
                    sb.Append(digits, 0, pointPos);
                    sb.Append('.');
                    sb.Append(digits, pointPos, digits.Length - pointPos);
                }
                result = sb.ToString();
            }
            if (result.IndexOf('.') >= 0)
            {
                result = result.TrimEnd('0');
                if (result.EndsWith(".", StringComparison.Ordinal))
                {
                    result = result.Substring(0, result.Length - 1);
                }
            }
            if (result == "-0" || result == "-" || result.Length == 0)
            {
                return "0";
            }
            return result;
        }
    }
}
