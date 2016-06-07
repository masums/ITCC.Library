﻿using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Griffin.Net.Protocols.Http;
using ITCC.HTTP.Common;

namespace ITCC.HTTP.Server
{
    /// <summary>
    ///     Server response factory
    /// </summary>
    internal static class ResponseFactory
    {
        /// <summary>
        ///     Status code -> reason phrase map
        /// </summary>
        private static readonly Dictionary<HttpStatusCode, string> ReasonPhrases = new Dictionary
            <HttpStatusCode, string>
        {
            {HttpStatusCode.OK, "OK"},
            {HttpStatusCode.Created, "Created"},
            {HttpStatusCode.NoContent, "No content"},
            {HttpStatusCode.BadRequest, "Bad request"},
            {HttpStatusCode.Unauthorized, "Unauthorized"},
            {HttpStatusCode.Forbidden, "Forbidden"},
            {HttpStatusCode.NotFound, "Not found"},
            {HttpStatusCode.Conflict, "Conflict"},
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

        /// <summary>
        /// </summary>
        /// <param name="code"></param>
        /// <param name="body"></param>
        /// <returns></returns>
        public static HttpResponse CreateResponse(HttpStatusCode code, object body, bool alreadyEncoded = false)
        {
            var httpResponse = new HttpResponse(code, SelectReasonPhrase(code), "HTTP/1.1");
            if (_commonHeaders != null)
            {
                foreach (var header in _commonHeaders)
                {
                    httpResponse.AddHeader(header.Key, header.Value);
                }
            }

            if (body != null)
            {
                string bodyString;
                if (! alreadyEncoded)
                    bodyString = _bodySerializer == null ? body.ToString() : _bodySerializer(body);
                else
                {
                    bodyString = body as string;
                }

                var encoding = _bodyEncoding;
                httpResponse.Body = new MemoryStream(encoding.GetBytes(bodyString ?? string.Empty));
                httpResponse.ContentType = "application/json";
                httpResponse.ContentCharset = encoding;
            }

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
                builder.AppendLine($"<File {((FileStream) response.Body).Name} content>");
            }
            else
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