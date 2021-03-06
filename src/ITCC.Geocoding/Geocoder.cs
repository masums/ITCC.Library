﻿// This is an open source non-commercial project. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Geocoding.Google;
using ITCC.Geocoding.Enums;
using ITCC.Geocoding.Utils;
using ITCC.Geocoding.Yandex;
using ITCC.Logging.Core;

namespace ITCC.Geocoding
{
    public static class Geocoder
    {
        #region public

        public static async Task<Point> GeocodeAsync(string location, GeocodingApi apiType)
        {
            Point result;
            switch (apiType)
            {
                case GeocodingApi.Yandex:
                    var codingResult = await YandexGeocoder.GeocodeAsync(location);
                    if (!codingResult.Any())
                        return null;
                    var firstPoint = codingResult.First().Point;
                    result = new Point
                    {
                        Latitude = firstPoint.Latitude,
                        Longitude = firstPoint.Longitude
                    };
                    break;
                case GeocodingApi.Google:
                    var geocoder = new GoogleGeocoder();
                    lock (KeyLock)
                    {
                        if (!string.IsNullOrEmpty(_googleApiKey))
                            geocoder.ApiKey = _googleApiKey;
                    }
                    IEnumerable<GoogleAddress> addresses;
                    try
                    {
                        addresses = await geocoder.GeocodeAsync(location);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException("GOOGLE GEO", LogLevel.Debug, ex);
                        return null;
                    }
                    
                    var firstAddress = addresses?.FirstOrDefault();
                    if (firstAddress == null)
                        return null;
                    result = new Point
                    {
                        Latitude = firstAddress.Coordinates.Latitude,
                        Longitude = firstAddress.Coordinates.Longitude
                    };

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(apiType), apiType, null);
            }
            return result;
        }

        public static void SetApiKey(string key, GeocodingApi apiType)
        {
            if (string.IsNullOrEmpty(key))
                return;
            switch (apiType)
            {
                case GeocodingApi.Yandex:
                    YandexApiKey = key;
                    break;
                case GeocodingApi.Google:
                    GoogleApiKey = key;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(apiType), apiType, null);
            }
        }

        public static string YandexApiKey
        {
            get
            {
                lock (KeyLock)
                {
                    return _yandexApiKey;
                }
            }
            set
            {
                lock (KeyLock)
                {
                    _yandexApiKey = value;
                    YandexGeocoder.Key = _yandexApiKey;
                }
            }
        }

        public static string GoogleApiKey
        {
            get
            {
                lock (KeyLock)
                {
                    return _googleApiKey;
                }
            }
            set
            {
                lock (KeyLock)
                {
                    _googleApiKey = value;
                }
            }
        }

        #endregion

        #region private

        private static string _yandexApiKey;
        private static string _googleApiKey;
        private static readonly object KeyLock = new object();

        #endregion
    }
}
