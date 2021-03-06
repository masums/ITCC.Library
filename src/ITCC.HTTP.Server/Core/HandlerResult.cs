﻿// This is an open source non-commercial project. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com
using System.Collections.Generic;
using System.Net;

namespace ITCC.HTTP.Server.Core
{
    /// <summary>
    ///     Represents client handler result
    /// </summary>
    public class HandlerResult
    {
        public HandlerResult(HttpStatusCode statusCode, object body, IDictionary<string, string> additionalHeaders = null)
        {
            Status = statusCode;
            Body = body;
            AdditionalHeaders = additionalHeaders;
        }

        /// <summary>
        ///     Generated response
        /// </summary>
        public object Body { get; set; }

        /// <summary>
        ///     General handler status
        /// </summary>
        public HttpStatusCode Status { get; set; }

        /// <summary>
        ///     Custom additional headers. User SHOULD provide Retry-After header in case of Status == 429
        /// </summary>
        public IDictionary<string, string> AdditionalHeaders { get; set; }
    }
}