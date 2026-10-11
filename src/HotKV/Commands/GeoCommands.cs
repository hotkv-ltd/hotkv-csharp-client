// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>Distance unit for the geospatial commands.</summary>
    public enum GeoUnit
    {
        Meters,
        Kilometers,
        Miles,
        Feet,
    }

    /// <summary>One GEOSEARCH result, with only the requested WITH* fields populated.</summary>
    public sealed record GeoSearchResult(string Member, double? DistanceUnits, long? GeoHash, (double Longitude, double Latitude)? Coordinate);

    /// <summary>Options for <see cref="GeoCommands.GeoSearchAsync"/>. Set exactly one of <see cref="FromMember"/>/<see cref="FromLonLat"/> and exactly one of <see cref="ByRadius"/>/<see cref="ByBox"/>.</summary>
    public sealed class GeoSearchOptions
    {
        public string? FromMember { get; set; }
        public (double Longitude, double Latitude)? FromLonLat { get; set; }
        public double? ByRadius { get; set; }
        public (double Width, double Height)? ByBox { get; set; }
        public GeoUnit Unit { get; set; } = GeoUnit.Meters;

        /// <summary>true = ASC, false = DESC, null = unordered.</summary>
        public bool? Ascending { get; set; }
        public long? Count { get; set; }
        public bool CountAny { get; set; }
        public bool WithCoord { get; set; }
        public bool WithDist { get; set; }
        public bool WithHash { get; set; }
    }

    /// <summary>Typed wrappers for the geospatial command family.</summary>
    public static class GeoCommands
    {
        internal static string UnitToken(GeoUnit unit) => unit switch
        {
            GeoUnit.Kilometers => "km",
            GeoUnit.Miles => "mi",
            GeoUnit.Feet => "ft",
            _ => "m",
        };

        /// <summary>GEOADD key longitude latitude member [longitude latitude member ...] -> number newly added.</summary>
        public static async Task<long> GeoAddAsync(this ICommandExecutor executor, string key, IReadOnlyList<(double Longitude, double Latitude, string Member)> entries, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(entries.Count * 3 + 2) { "GEOADD", key };
            foreach (var (lon, lat, member) in entries)
            {
                args.Add(lon);
                args.Add(lat);
                args.Add(member);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>GEOADD key longitude latitude member.</summary>
        public static Task<long> GeoAddAsync(this ICommandExecutor executor, string key, double longitude, double latitude, string member, CancellationToken cancellationToken = default) =>
            executor.GeoAddAsync(key, new[] { (longitude, latitude, member) }, cancellationToken);

        /// <summary>GEOPOS key member [member ...] -> the coordinate per member, null where the member is absent.</summary>
        public static async Task<IReadOnlyList<(double Longitude, double Latitude)?>> GeoPositionAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> members, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(members.Count + 2) { "GEOPOS", key };
            args.AddRange(members);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v =>
            {
                if (v.IsNull)
                {
                    return ((double, double)?)null;
                }
                var pair = v.AsArray();
                return (pair[0].AsDouble(), pair[1].AsDouble());
            }).ToList();
        }

        /// <summary>GEODIST key member1 member2 [unit] -> null if either member is absent.</summary>
        public static async Task<double?> GeoDistanceAsync(this ICommandExecutor executor, string key, string member1, string member2, GeoUnit unit = GeoUnit.Meters, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "GEODIST", key, member1, member2, UnitToken(unit) }, cancellationToken).ConfigureAwait(false);
            return reply.AsNullableDouble();
        }

        /// <summary>GEOHASH key member [member ...] -> the standard 11 character geohash per member, null where absent.</summary>
        public static async Task<IReadOnlyList<string?>> GeoHashAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> members, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(members.Count + 2) { "GEOHASH", key };
            args.AddRange(members);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsStringArray();
        }

        /// <summary>GEOSEARCH key FROMMEMBER|FROMLONLAT BYRADIUS|BYBOX [ASC|DESC] [COUNT count [ANY]] [WITHCOORD] [WITHDIST] [WITHHASH].</summary>
        public static async Task<IReadOnlyList<GeoSearchResult>> GeoSearchAsync(this ICommandExecutor executor, string key, GeoSearchOptions options, CancellationToken cancellationToken = default)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            var args = new List<object?> { "GEOSEARCH", key };
            if (options.FromMember is not null)
            {
                args.Add("FROMMEMBER");
                args.Add(options.FromMember);
            }
            else if (options.FromLonLat.HasValue)
            {
                args.Add("FROMLONLAT");
                args.Add(options.FromLonLat.Value.Longitude);
                args.Add(options.FromLonLat.Value.Latitude);
            }
            else
            {
                throw new ArgumentException("GEOSEARCH requires FromMember or FromLonLat", nameof(options));
            }

            if (options.ByRadius.HasValue)
            {
                args.Add("BYRADIUS");
                args.Add(options.ByRadius.Value);
                args.Add(UnitToken(options.Unit));
            }
            else if (options.ByBox.HasValue)
            {
                args.Add("BYBOX");
                args.Add(options.ByBox.Value.Width);
                args.Add(options.ByBox.Value.Height);
                args.Add(UnitToken(options.Unit));
            }
            else
            {
                throw new ArgumentException("GEOSEARCH requires ByRadius or ByBox", nameof(options));
            }

            if (options.Ascending == true)
            {
                args.Add("ASC");
            }
            else if (options.Ascending == false)
            {
                args.Add("DESC");
            }
            if (options.Count.HasValue)
            {
                args.Add("COUNT");
                args.Add(options.Count.Value);
                if (options.CountAny)
                {
                    args.Add("ANY");
                }
            }
            if (options.WithCoord)
            {
                args.Add("WITHCOORD");
            }
            if (options.WithDist)
            {
                args.Add("WITHDIST");
            }
            if (options.WithHash)
            {
                args.Add("WITHHASH");
            }

            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            bool anyWith = options.WithCoord || options.WithDist || options.WithHash;
            if (!anyWith)
            {
                return reply.AsNonNullStringArray().Select(m => new GeoSearchResult(m, null, null, null)).ToList();
            }

            var results = new List<GeoSearchResult>();
            foreach (var item in reply.AsArray())
            {
                var parts = item.AsArray();
                int idx = 1;
                string member = parts[0].AsString() ?? string.Empty;
                double? dist = options.WithDist ? parts[idx++].AsDouble() : (double?)null;
                long? hash = options.WithHash ? parts[idx++].AsInt64() : (long?)null;
                (double, double)? coord = null;
                if (options.WithCoord)
                {
                    var c = parts[idx++].AsArray();
                    coord = (c[0].AsDouble(), c[1].AsDouble());
                }
                results.Add(new GeoSearchResult(member, dist, hash, coord));
            }
            return results;
        }
    }
}
