﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using Griffin.Net.Protocols.Http;
using ITCC.HTTP.Common;
using ITCC.HTTP.Enums;

namespace ITCC.HTTP.Server
{
    /// <summary>
    ///     Server response factory
    /// </summary>
    internal static class ResponseFactory
    {
        private static readonly Dictionary<AuthorizationStatus, HttpStatusCode> AuthResultDictionary = new Dictionary<AuthorizationStatus, HttpStatusCode>
        {
            {AuthorizationStatus.NotRequired, HttpStatusCode.OK },
            {AuthorizationStatus.Ok, HttpStatusCode.OK },
            {AuthorizationStatus.Unauthorized, HttpStatusCode.Unauthorized },
            {AuthorizationStatus.Forbidden, HttpStatusCode.Forbidden },
            {AuthorizationStatus.TooManyRequests, (HttpStatusCode)429 },
            {AuthorizationStatus.InternalError, HttpStatusCode.OK }
        };

        /// <summary>
        ///     Status code -> reason phrase map
        /// </summary>
        private static readonly Dictionary<HttpStatusCode, string> ReasonPhrases = new Dictionary
            <HttpStatusCode, string>
        {
            {HttpStatusCode.OK, "OK"},
            {HttpStatusCode.Created, "Created"},
            {HttpStatusCode.NoContent, "No content"},
            {HttpStatusCode.MovedPermanently, "Moved Permanently" },
            {HttpStatusCode.Found, "Found" },
            {HttpStatusCode.BadRequest, "Bad request"},
            {HttpStatusCode.Unauthorized, "Unauthorized"},
            {HttpStatusCode.Forbidden, "Forbidden"},
            {HttpStatusCode.NotFound, "Not found"},
            {HttpStatusCode.Conflict, "Conflict"},
            {HttpStatusCode.RequestEntityTooLarge, "Request Entity Too Large" },
            {(HttpStatusCode)429, "Too many requests"},
            {HttpStatusCode.InternalServerError, "Internal Server Error"},
            {HttpStatusCode.NotImplemented, "Not implemented"},
            {HttpStatusCode.MethodNotAllowed, "Method Not Allowed" }
        };

        /// <summary>
        ///     Common response headers
        /// </summary>
        private static Dictionary<string, string> _commonHeaders;

        private static Delegates.BodySerializer _bodySerializer;

        /// <summary>
        ///     Every server response will have this headers
        /// </summary>
        /// <param name="headers">Headers dictionary</param>
        /// <returns>Operation status</returns>
        public static bool SetCommonHeaders(IDictionary<string, string> headers)
        {
            if (headers == null)
                return false;

            _commonHeaders = new Dictionary<string, string>(headers);
            return true;
        }

        public static void SetBodySerializer(Delegates.BodySerializer serializer)
        {
            _bodySerializer = serializer;
        }

        public static void SetBodyEncoding(Encoding encoding)
        {
            _bodyEncoding = encoding;
        }

        public static HttpResponse CreateResponse(AuthentificationResult authentificationResult, bool gzipResponse = false)
        {
            if (authentificationResult == null)
                throw new ArgumentNullException(nameof(authentificationResult));

            return CreateResponse(authentificationResult.Status, authentificationResult.AccountView, authentificationResult.AdditionalHeaders, gzipResponse);
        }

        public static HttpResponse CreateResponse<TAccount>(AuthorizationResult<TAccount> authorizationResult, bool gzipResponse = false)
            where TAccount : class
        {
            if (authorizationResult == null)
                throw new ArgumentNullException(nameof(authorizationResult));

            var httpStatusCode = AuthResultDictionary.ContainsKey(authorizationResult.Status)
                ? AuthResultDictionary[authorizationResult.Status]
                : HttpStatusCode.InternalServerError;

            return CreateResponse(httpStatusCode,
                (object)authorizationResult.Account ?? authorizationResult.ErrorDescription,
                authorizationResult.AdditionalHeaders,
                gzipResponse);
        }

        public static HttpResponse CreateResponse(HandlerResult handlerResult, bool alreadyEncoded = false, bool gzipResponse = false)
        {
            if (handlerResult == null)
                throw new ArgumentNullException(nameof(handlerResult));

            return CreateResponse(handlerResult.Status, handlerResult.Body, handlerResult.AdditionalHeaders, alreadyEncoded, gzipResponse);
        }

        public static HttpResponse CreateResponse(HttpStatusCode code, object body, IDictionary<string, string> additionalHeaders = null, bool alreadyEncoded = false, bool gzipResponse = false)
        {
            var httpResponse = new HttpResponse(code, SelectReasonPhrase(code), "HTTP/1.1");
            if (_commonHeaders != null)
            {
                foreach (var header in _commonHeaders)
                {
                    httpResponse.AddHeader(header.Key, header.Value);
                }
            }

            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    httpResponse.AddHeader(header.Key, header.Value);
                }
            }

            if (body == null)
                return httpResponse;

            string bodyString;
            if (!alreadyEncoded)
                bodyString = _bodySerializer == null ? body.ToString() : _bodySerializer(body);
            else
            {
                bodyString = body as string;
            }

            var encoding = _bodyEncoding;
            if (gzipResponse)
            {
                using (var uncompressedStream = new MemoryStream(encoding.GetBytes(bodyString ?? string.Empty)))
                {
                    httpResponse.Body = new MemoryStream(512);
                    using (var gzipStream = new GZipStream(httpResponse.Body, CompressionMode.Compress, true))
                    {
                        uncompressedStream.CopyTo(gzipStream);
                    }
                    httpResponse.Body.Position = 0;
                    httpResponse.ContentLength = (int)httpResponse.Body.Length;
                }
                httpResponse.AddHeader("Content-Encoding", "gzip");
            }
            else
            {
                httpResponse.Body = new MemoryStream(encoding.GetBytes(bodyString ?? string.Empty));
            }

            httpResponse.ContentType = "application/json";
            httpResponse.ContentCharset = encoding;
            return httpResponse;
        }

        /// <summary>
        ///     Makes string from http response
        /// </summary>
        /// <param name="response">Response</param>
        /// <returns>String representation</returns>
        public static string SerializeResponse(HttpResponse response)
        {
            if (response == null)
                return string.Empty;

            var builder = new StringBuilder();
            builder.AppendLine($"{response.StatusLine}");
            foreach (var header in response.Headers)
            {
                builder.AppendLine($"{header.Key}: {header.Value}");
            }
            if (response.Body == null)
                return builder.ToString();

            builder.AppendLine();
            if (!LogResponseBodies)
                return builder.ToString();

            if (response.Body is FileStream)
            {
                builder.AppendLine($"<File {((FileStream)response.Body).Name} content>");
            }
            else
            {
                try
                {
                    var reader = new StreamReader(response.Body);

                    if (ResponseBodyLogLimit < 1 || response.Body.Length <= ResponseBodyLogLimit)
                        builder.AppendLine(reader.ReadToEnd());
                    else
                    {
                        var buffer = new byte[ResponseBodyLogLimit];
                        response.Body.Read(buffer, 0, ResponseBodyLogLimit);
                        builder.AppendLine(_bodyEncoding.GetString(buffer));
                        builder.AppendLine($"[And {response.Body.Length - ResponseBodyLogLimit} more bytes...]");
                    }

                    response.Body.Position = 0;
                }
                catch (Exception)
                {
                    return builder.ToString();
                }
            }

            return builder.ToString();
        }

        /// <summary>
        ///     Reason phrase selector
        /// </summary>
        /// <param name="code">Statuc code (enum element)</param>
        /// <returns>Reaon phrase</returns>
        private static string SelectReasonPhrase(HttpStatusCode code)
        {
            return ReasonPhrases.ContainsKey(code) ? ReasonPhrases[code] : "UNKNOWN REASON";
        }

        private static Encoding _bodyEncoding = Encoding.UTF8;
        public static bool LogResponseBodies = true;
        public static int ResponseBodyLogLimit = -1;
    }
}