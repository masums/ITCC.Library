﻿using System.Collections.Generic;
using ITCC.HTTP.Common;
using ITCC.HTTP.Enums;
using ITCC.Logging;

namespace ITCC.HTTP.Server
{
    public class HttpServerConfiguration<TAccount>
        where TAccount : class
    {
        /// <summary>
        ///     Used for certificate search
        /// </summary>
        public string SubjectName { get; set; }
        /// <summary>
        ///     TCP port to listen
        /// </summary>
        public ushort Port { get; set; }
        /// <summary>
        ///     Http/https
        /// </summary>
        public Protocol Protocol { get; set; }
        /// <summary>
        ///     Method used to find certificate. Used with https Protocol
        /// </summary>
        public Delegates.CertificateProvider CertificateProvider { get; set; }
        /// <summary>
        ///     If false, CertificateProvider cannot provide self-signed certificates
        /// </summary>
        public bool AllowSelfSignedCertificates { get; set; }
        /// <summary>
        ///     Does server support file streaming?
        /// </summary>
        public bool FilesEnabled { get; set; }
        /// <summary>
        ///     File location on disk
        /// </summary>
        public string FilesLocation { get; set; }
        /// <summary>
        ///     All files can be found at SubjectName:Port/FilesBaseUri/Filename
        /// </summary>
        public string FilesBaseUri { get; set; }
        /// <summary>
        ///     If true, every file request will be checked with Authorizer
        /// </summary>
        public bool FilesNeedAuthorization { get; set; }
        /// <summary>
        ///     File sections for separate access grants
        /// </summary>
        public List<FileSection> FileSections { get; set; } = new List<FileSection>(); 
        /// <summary>
        ///     Method used to check authorization tokens for files requests
        /// </summary>
        public Delegates.FilesAuthorizer<TAccount> FilesAuthorizer { get; set; }

        /// <summary>
        ///     Method used to receive authorization tokens
        /// </summary>
        public Delegates.Authentificator Authentificator { get; set; }
        /// <summary>
        ///     Method used to check authorization tokens
        /// </summary>
        public Delegates.Authorizer<TAccount> Authorizer { get; set; }

        /// <summary>
        ///     Method used to serialize server responses
        /// </summary>
        public Delegates.BodySerializer BodySerializer { get; set; }

        /// <summary>
        ///     If true, then unauthorized /statistics requests are enabled
        /// </summary>
        public bool StatisticsEnabled { get; set; }

        /// <summary>
        ///     Favicon requests (Just for fun)
        /// </summary>
        public string FaviconPath { get; set; }

        /// <summary>
        ///     Used for Server: header
        /// </summary>
        public string ServerName { get; set; }

        /// <summary>
        ///     Amount of 64k buffers for socket data
        /// </summary>
        public int BufferPoolSize { get; set; } = 100;

        public bool IsEnough()
        {
            if (SubjectName == null)
            {
                LogMessage(LogLevel.Warning, "You must specify server subject");
                return false;
            }
            if (Protocol == Protocol.Https && CertificateProvider == null)
            {
                LogMessage(LogLevel.Warning, "No certificate provider passed to Start()");
                return false;
            }

            if (BodySerializer == null)
            {
                LogMessage(LogLevel.Warning, "No body serializer passed to Start()");
                return false;
            }

            if (FilesNeedAuthorization && FilesAuthorizer == null)
            {
                LogMessage(LogLevel.Warning, "No files authorizer passed to Start()");
                return false;
            }

            if (FilesEnabled && FileSections.Count == 0)
            {
                LogMessage(LogLevel.Warning, "No file sections passed to Start()");
                return false;
            }

            return true;
        }

        private void LogMessage(LogLevel level, string message)
        {
            Logger.LogEntry("SERVERCONFIG", level, message);
        }
    }
}
