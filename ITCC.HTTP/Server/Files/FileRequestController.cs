﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;
using Griffin.Net.Channels;
using Griffin.Net.Protocols.Http;
using ITCC.HTTP.Common;
using ITCC.HTTP.Enums;
using ITCC.HTTP.Server.Files.Preprocess;
using ITCC.HTTP.Server.Files.Requests;
using ITCC.HTTP.Utils;
using ITCC.Logging;

namespace ITCC.HTTP.Server.Files
{
    internal static class FileRequestController<TAccount>
        where TAccount : class
    {
        #region config

        public static bool Start(FileRequestControllerConfiguration<TAccount> configuration, ServerStatistics<TAccount> statistics)
        {
            FilesLocation = configuration.FilesLocation;
            FaviconPath = configuration.FaviconPath;
            FilesNeedAuthorization = configuration.FilesNeedAuthorization;
            FileSections = configuration.FileSections;
            FilesPreprocessingEnabled = configuration.FilesPreprocessingEnabled;
            ExistingFilesPreprocessingFrequency = configuration.ExistingFilesPreprocessingFrequency;
            _filesAuthorizer = configuration.FilesAuthorizer;
            _statistics = statistics;

            if (!IoHelper.HasWriteAccessToDirectory(FilesLocation))
            {
                LogMessage(LogLevel.Warning, $"Cannot use file folder {FilesLocation} : no write access");
                return false;
            }

            if (FilesPreprocessingEnabled)
            {
                FilePreprocessController.Start(configuration.FilesPreprocessorThreads);
                if (ExistingFilesPreprocessingFrequency > 0)
                {
                    PreprocessExistingFiles();
                    _preprocessTimer = new Timer(1000 * ExistingFilesPreprocessingFrequency);
                    _preprocessTimer.Elapsed += (sender, args) => PreprocessExistingFiles();
                    _preprocessTimer.Start();
                }
            }

            _started = true;
            return true;
        }

        public static void Stop()
        {
            if (FilesPreprocessingEnabled)
            {
                _preprocessTimer?.Stop();
                FilePreprocessController.Stop();
            }
            _started = false;
        }

        public static string FilesLocation { get; private set; }
        public static string FaviconPath { get; private set; }
        public static bool FilesNeedAuthorization { get; private set; }
        public static List<FileSection> FileSections { get; private set; }
        public static bool FilesPreprocessingEnabled { get; private set; }
        public static double ExistingFilesPreprocessingFrequency { get; private set; }

        private static Delegates.FilesAuthorizer<TAccount> _filesAuthorizer;
        private static ServerStatistics<TAccount> _statistics;
        private static Timer _preprocessTimer;
        private static bool _started;

        #endregion

        #region requests
        public static async Task<HttpResponse> HandleFileRequest(HttpRequest request)
        {
            if (! _started)
                return ResponseFactory.CreateResponse(HttpStatusCode.NotImplemented, null);

            try
            {
                var filename = ExtractFileName(request.Uri.LocalPath);
                var section = ExtractFileSection(request.Uri.LocalPath);
                if (filename == null || section == null || filename.Contains("_CHANGED_"))
                {
                    return ResponseFactory.CreateResponse(HttpStatusCode.BadRequest, null);
                }
                var filePath = FilesLocation + Path.DirectorySeparatorChar + section.Folder + Path.DirectorySeparatorChar + filename;

                AuthorizationResult<TAccount> authResult;
                if (FilesNeedAuthorization)
                {
                    authResult = await _filesAuthorizer.Invoke(request, section, filename).ConfigureAwait(false);
                }
                else
                {
                    authResult = new AuthorizationResult<TAccount>(null, AuthorizationStatus.NotRequired);
                }
                _statistics?.AddAuthResult(authResult);
                switch (authResult.Status)
                {
                    case AuthorizationStatus.NotRequired:
                    case AuthorizationStatus.Ok:
                        if (CommonHelper.HttpMethodToEnum(request.HttpMethod) == HttpMethod.Get)
                        {
                            return await HandleFileGetRequest(request, filePath);
                        }
                        if (CommonHelper.HttpMethodToEnum(request.HttpMethod) == HttpMethod.Post)
                        {
                            return await HandleFilePostRequest(request, section, filePath).ConfigureAwait(false);
                        }
                        if (CommonHelper.HttpMethodToEnum(request.HttpMethod) == HttpMethod.Delete)
                        {
                            return HandleFileDeleteRequest(filePath);
                        }
                        return ResponseFactory.CreateResponse(HttpStatusCode.MethodNotAllowed, null);
                    default:
                        return ResponseFactory.CreateResponse(authResult);
                }
            }
            catch (Exception exception)
            {
                LogException(LogLevel.Warning, exception);
                return ResponseFactory.CreateResponse(HttpStatusCode.InternalServerError, null);
            }
        }

        public static HttpResponse HandleFavicon(HttpRequest request)
        {
            LogMessage(LogLevel.Trace, $"Favicon requested, path: {FaviconPath}");
            if (string.IsNullOrEmpty(FaviconPath) || !File.Exists(FaviconPath))
            {
                return ResponseFactory.CreateResponse(HttpStatusCode.NotFound, null);
            }
            var response = ResponseFactory.CreateResponse(HttpStatusCode.OK, null);
            var fileStream = new FileStream(FaviconPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite);
            response.ContentType = "image/x-icon";
            response.Body = fileStream;

            return response;
        }

        private static async Task<HttpResponse> HandleFileGetRequest(HttpRequest request, string filePath)
        {
            HttpResponse response;
            if (FilesPreprocessingEnabled && FilePreprocessController.FileInProgress(filePath))
            {
                LogMessage(LogLevel.Debug, $"File {filePath} was requested but is in progress");
                response = ResponseFactory.CreateResponse(HttpStatusCode.ServiceUnavailable, null);
                return response;
            }

            var fileRequest = FileRequestFactory.BuildRequest(filePath, request);
            if (fileRequest == null) 
            {
                LogMessage(LogLevel.Debug, $"Failed to build file request for {filePath}");
                response = ResponseFactory.CreateResponse(HttpStatusCode.BadRequest, null);
                return response;
            }
            return await fileRequest.BuildResponse();
        }

        private static async Task<HttpResponse> HandleFilePostRequest(HttpRequest request, FileSection section, string filePath)
        {
            HttpResponse response;
            if (File.Exists(filePath))
            {
                LogMessage(LogLevel.Debug, $"File {filePath} already exists");
                response = ResponseFactory.CreateResponse(HttpStatusCode.Conflict, null);
                return response;
            }
            var fileContent = request.Body;
            if (section.MaxFileSize > 0 && fileContent.Length > section.MaxFileSize)
            {
                LogMessage(LogLevel.Debug, $"Trying to create file of size {fileContent.Length} in section {section.Name} with max size of {section.MaxFileSize}");
                response = ResponseFactory.CreateResponse(HttpStatusCode.RequestEntityTooLarge, null);
                return response;
            }
            using (var file = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                await fileContent.CopyToAsync(file).ConfigureAwait(false);
                file.Flush();
                file.Close();
            }
            fileContent.Flush();
            fileContent.Close();
            fileContent.Dispose();
            GC.Collect();
            LogMessage(LogLevel.Trace, $"Total memory: {GC.GetTotalMemory(true)}");
            LogMessage(LogLevel.Debug, $"File {filePath} created");
            if (FilesPreprocessingEnabled)
            {
                var code = FilePreprocessController.EnqueueFile(filePath) ? HttpStatusCode.Accepted : HttpStatusCode.Created;
                response = ResponseFactory.CreateResponse(code, null);
            }
            else
                response = ResponseFactory.CreateResponse(HttpStatusCode.Created, null);

            return response;
        }

        private static HttpResponse HandleFileDeleteRequest(string filePath)
        {
            HttpResponse response;
            if (!File.Exists(filePath))
            {
                LogMessage(LogLevel.Debug, $"File {filePath} does not exist and cannot be deleted");
                response = ResponseFactory.CreateResponse(HttpStatusCode.NotFound, null);
                return response;
            }

            var compressed = IoHelper.LoadAllChanged(filePath);

            try
            {
                File.Delete(filePath);
                compressed.ForEach(File.Delete);
                response = ResponseFactory.CreateResponse(HttpStatusCode.OK, null);
            }
            catch (Exception ex)
            {
                LogException(LogLevel.Warning, ex);
                response = ResponseFactory.CreateResponse(HttpStatusCode.InternalServerError, null);
            }
            return response;
        }

        private static string ExtractFileName(string localPath)
        {
            if (localPath == null)
                return null;
            try
            {
                var result = localPath.Trim('/');
                var slashIndex = result.LastIndexOf("/", StringComparison.Ordinal);
                result = result.Remove(0, slashIndex + 1);
                LogMessage(LogLevel.Debug, $"File requested: {result}");
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static FileSection ExtractFileSection(string localPath)
        {
            if (localPath == null)
                return null;
            try
            {
                var sectionName = localPath.Trim('/');
                var slashIndex = sectionName.IndexOf("/", StringComparison.Ordinal);
                var lastSlashIndex = sectionName.LastIndexOf("/", StringComparison.Ordinal);
                sectionName = sectionName.Substring(slashIndex + 1, lastSlashIndex - slashIndex - 1);
                LogMessage(LogLevel.Debug, $"Section requested: {sectionName}");
                return FileSections.FirstOrDefault(s => s.Folder == sectionName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void PreprocessExistingFiles()
        {
            foreach (var section in FileSections)
            {
                var directory = Path.Combine(FilesLocation, section.Folder);
                foreach (var file in Directory.GetFiles(directory).Where(f => !f.Contains(Constants.ChangedString)))
                {
                    if (IoHelper.LoadAllChanged(file).Any())
                        continue;
                    FilePreprocessController.EnqueueFile(file);
                }
            }
        }

        #endregion

        #region log
        private static void LogMessage(LogLevel level, string message)
        {
            Logger.LogEntry("HTTP FILES", level, message);
        }

        private static void LogException(LogLevel level, Exception ex)
        {
            Logger.LogException("HTTP FILES", level, ex);
        }
        #endregion
    }
}
