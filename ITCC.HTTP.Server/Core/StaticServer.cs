﻿#if TRACE
    #define ITCC_LOG_REQUEST_BODIES
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ITCC.HTTP.Common.Enums;
using ITCC.HTTP.Server.Auth;
using ITCC.HTTP.Server.Common;
using ITCC.HTTP.Server.Enums;
using ITCC.HTTP.Server.Files;
using ITCC.HTTP.Server.Interfaces;
using ITCC.HTTP.Server.Service;
using ITCC.HTTP.Server.Utils;
using ITCC.Logging.Core;

// ReSharper disable StaticMemberInGenericType

namespace ITCC.HTTP.Server.Core
{
    /// <summary>
    ///     Represents static (and so, singleton HTTP(S) server
    /// </summary>
    public static class StaticServer<TAccount> where TAccount : class
    {
        #region main
        public static ServerStartStatus Start(HttpServerConfiguration<TAccount> configuration)
        {
            if (_started)
            {
                LogMessage(LogLevel.Warning, "Server is already running");
                return ServerStartStatus.AlreadyStarted;
            }
            lock (OperationLock)
            {
                if (_operationInProgress)
                {
                    LogMessage(LogLevel.Info, "Cannot start now, operation in progress");
                    return ServerStartStatus.AlreadyStarted;
                }
                _operationInProgress = true;
            }
            if (configuration == null || !configuration.IsEnough())
                return ServerStartStatus.BadParameters;

            try
            {
                PrepareStatistics(configuration);

                _optionsController = new OptionsController<TAccount>(InnerRequestProcessors);
                _pingController = new PingController();

                Protocol = configuration.Protocol;
                _authorizer = configuration.Authorizer;
                _authentificationController = new AuthentificationController(configuration.Authentificator);

                ConfigureResponseBuilding(configuration);

                if (!StartFileProcessing(configuration))
                    return ServerStartStatus.BadParameters;

                _requestMaxServeTime = configuration.RequestMaxServeTime;

                StartListenerThread(configuration);
                StartServices(configuration);
                _started = true;

                return ServerStartStatus.Ok;
            }
            catch (SocketException)
            {
                LogMessage(LogLevel.Critical, $"Error binding to port {configuration.Port}. Is it in use?");
                _fileRequestController.Stop();
                return ServerStartStatus.BindingError;
            }
            catch (Exception ex)
            {
                LogException(LogLevel.Critical, ex);
                _fileRequestController.Stop();
                return ServerStartStatus.UnknownError;
            }
            finally
            {
                lock (OperationLock)
                {
                    _operationInProgress = false;
                }
            }
        }

        private static void PrepareStatistics(HttpServerConfiguration<TAccount> configuration)
        {
            _statisticsController = configuration.StatisticsEnabled
                ? new StatisticsController<TAccount>(new ServerStatistics<TAccount>(), configuration.StatisticsAuthorizer)
                : new StatisticsController<TAccount>(null, null);
        }

        private static void ConfigureResponseBuilding(HttpServerConfiguration<TAccount> configuration)
        {
            if (configuration.LogBodyReplacePatterns != null)
                ResponseFactory.LogBodyReplacePatterns.AddRange(configuration.LogBodyReplacePatterns);
            if (configuration.LogProhibitedHeaders != null)
                ResponseFactory.LogProhibitedHeaders.AddRange(configuration.LogProhibitedHeaders);

            ResponseFactory.SetBodyEncoder(configuration.BodyEncoder);
            ResponseFactory.LogResponseBodies = configuration.LogResponseBodies;
            ResponseFactory.ResponseBodyLogLimit = configuration.ResponseBodyLogLimit;
            CommonHelper.SetSerializationLimitations(configuration.LogProhibitedQueryParams,
                configuration.LogProhibitedHeaders,
                configuration.BodyEncoder.Encoding);

            if (configuration.ServerName != null)
            {
                ResponseFactory.SetCommonHeaders(new Dictionary<string, string> {{"Server", configuration.ServerName}});
            }
        }

        private static bool StartFileProcessing(HttpServerConfiguration<TAccount> configuration)
        {
            _fileRequestController = new FileRequestController<TAccount>();

            return _fileRequestController.Start(new FileRequestControllerConfiguration<TAccount>
            {
                FilesEnabled = configuration.FilesEnabled,
                FilesBaseUri = configuration.FilesBaseUri,
                ExistingFilesPreprocessingFrequency = configuration.ExistingFilesPreprocessingFrequency,
                FaviconPath = configuration.FaviconPath,
                FilesAuthorizer = configuration.FilesAuthorizer,
                FileSections = configuration.FileSections,
                FilesLocation = configuration.FilesLocation,
                FilesNeedAuthorization = configuration.FilesNeedAuthorization,
                FilesPreprocessingEnabled = configuration.FilesPreprocessingEnabled,
                FilesPreprocessorThreads = configuration.FilesPreprocessorThreads
            },
            _statisticsController.Statistics);
        }

        private static void StartListenerThread(HttpServerConfiguration<TAccount> configuration)
        {
            var protocolString = configuration.Protocol == Protocol.Http ? "http" : "https";
            ServicePointManager.DefaultConnectionLimit = 1000;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"{protocolString}://+:{configuration.Port}/");
            _listener.Start();
            _listenerThread = new Thread(() =>
            {
                LogMessage(LogLevel.Info, "Server listener thread started");
                LogMessage(LogLevel.Info, $"Started listening port {configuration.Port}");
                while (true)
                {
                    try
                    {
                        var context = _listener.GetContext();
                        LogMessage(LogLevel.Debug, $"Client connected: {context.Request.RemoteEndPoint}");
                        Task.Run(async () => await OnMessage(context));
                    }
                    catch (ThreadAbortException)
                    {
                        LogMessage(LogLevel.Info, "Server listener thread stopped");
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogException(LogLevel.Error, ex);
                    }
                }
            });

            _listenerThread.Start();
            _serverAddress = $"{protocolString}://{configuration.SubjectName}:{configuration.Port}/";
        }

        private static void StartServices(HttpServerConfiguration<TAccount> configuration)
        {
            ServiceUris.AddRange(configuration.GetReservedUris());
            if (ServiceUris.Any())
                LogMessage(LogLevel.Debug, $"Reserved sections:\n{string.Join("\n", ServiceUris)}");

            ServiceRequestProcessors.Add(_optionsController);
            ServiceRequestProcessors.Add(_fileRequestController);
            ServiceRequestProcessors.Add(_statisticsController);
            ServiceRequestProcessors.Add(_pingController);
            ServiceRequestProcessors.Add(_authentificationController);
        }

        public static void Stop()
        {
            if (!_started)
                return;
            if (FilesEnabled)
                _fileRequestController.Stop();
            lock (OperationLock)
            {
                if (_operationInProgress)
                {
                    LogMessage(LogLevel.Info, "Cannot stop now, operation in progress");
                    return;
                }
                _operationInProgress = true;
            }

            LogMessage(LogLevel.Info, "Shutting down");
            try
            {
                _listenerThread.Abort();
                _listener.Stop();
                _started = false;
                CleanUp();
                LogMessage(LogLevel.Info, "Stopped");
            }
            catch (Exception ex)
            {
                LogException(LogLevel.Warning, ex);
            }
            finally
            {
                lock (OperationLock)
                {
                    _operationInProgress = false;
                }
            }
        }

        private static void CleanUp()
        {
            InnerRequestProcessors.Clear();
            InnerStaticRedirectionTable.Clear();
            _listener = null;
            ServiceUris.Clear();
        }

        private static string GetNormalizedRequestUri(HttpListenerContext context) => "/" + context.Request.Url.LocalPath.Trim('/');

        private static bool _started;
        private static Thread _listenerThread;
        private static HttpListener _listener;
        private static bool _operationInProgress;
        private static readonly object OperationLock = new object();
        private static string _serverAddress;

        #endregion

        #region security

        public static Protocol Protocol { get; private set; }

        #endregion

        #region log

        private static void LogMessage(LogLevel level, string message) => Logger.LogEntry("HTTP SERVER", level, message);

        private static void LogException(LogLevel level, Exception ex) => Logger.LogException("HTTP SERVER", level, ex);

        #endregion

        #region handlers

        private static async Task OnMessage(HttpListenerContext context)
        {
            var request = context.Request;
            // This Stopwatch will be used by different threads, but sequentially (select processor => handle => completion)
            var stopWatch = Stopwatch.StartNew();
            try
            {
                _statisticsController.Statistics?.AddRequest(request);
                if (Logger.Level >= LogLevel.Debug)
                    LogMessage(LogLevel.Debug,
                        $"Request from {request.RemoteEndPoint}.\n{CommonHelper.SerializeHttpRequest(context)}");

                var serviceProcessor =
                    ServiceRequestProcessors.FirstOrDefault(sp => sp.RequestIsSuitable(context.Request));
                if (serviceProcessor != null)
                {
                    LogMessage(LogLevel.Debug, $"Service {serviceProcessor.Name} requested");
                    await serviceProcessor.HandleRequest(context, stopWatch, OnResponseReady);
                    return;
                }

                var requestProcessorSelectionResult = SelectRequestProcessor(request);
                if (requestProcessorSelectionResult == null)
                {
                    ResponseFactory.BuildResponse(context, HttpStatusCode.NotFound, null);
                    OnResponseReady(context, stopWatch);
                    return;
                }

                if (requestProcessorSelectionResult.IsRedirect)
                {
                    var redirectLocation = $"{_serverAddress}{requestProcessorSelectionResult.RequestProcessor.SubUri}";
                    context.Response.Redirect(redirectLocation);
                    OnResponseReady(context, stopWatch);
                    return;
                }

                if (!requestProcessorSelectionResult.MethodMatches)
                {
                    ResponseFactory.BuildResponse(context, HttpStatusCode.MethodNotAllowed, null);
                    OnResponseReady(context, stopWatch);
                    return;
                }

                var requestProcessor = requestProcessorSelectionResult.RequestProcessor;

                AuthorizationResult<TAccount> authResult;
                if (requestProcessor.AuthorizationRequired && _authorizer != null)
                {
                    authResult = await _authorizer.Invoke(request, requestProcessor);
                }
                else
                {
                    authResult = new AuthorizationResult<TAccount>(null, AuthorizationStatus.NotRequired);
                }

                _statisticsController.Statistics?.AddAuthResult(authResult);
                switch (authResult.Status)
                {
                    case AuthorizationStatus.NotRequired:
                    case AuthorizationStatus.Ok:
                        if (requestProcessor.Handler == null)
                        {
                            LogMessage(LogLevel.Debug,
                                $"{request.HttpMethod} {request.Url.LocalPath} was requested, but no handler is provided");
                            ResponseFactory.BuildResponse(context, HttpStatusCode.NotImplemented, null);
                        }
                        else
                        {
                            var handleResult =
                                await requestProcessor.Handler.Invoke(authResult.Account, request).ConfigureAwait(false);
                            ResponseFactory.BuildResponse(context, handleResult);
                        }
                        break;
                    default:
                        ResponseFactory.BuildResponse(context, authResult);
                        break;
                }
                OnResponseReady(context, stopWatch);
            }
            catch (Exception exception)
            {
                LogMessage(LogLevel.Warning, $"Error handling client request from {request.RemoteEndPoint}");
                LogException(LogLevel.Warning, exception);
                ResponseFactory.BuildResponse(context, HttpStatusCode.InternalServerError, null);
                OnResponseReady(context, stopWatch);
            }
        }

        private static void OnResponseReady(HttpListenerContext context, Stopwatch requestStopwatch)
        {
            try
            {
                LogMessage(LogLevel.Debug, $"Response for {context.Request.RemoteEndPoint} ready.");
                requestStopwatch.Stop();
                var elapsedMilliseconds = requestStopwatch.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
                _statisticsController.Statistics?.AddResponse(context.Response, GetNormalizedRequestUri(context), elapsedMilliseconds);
                if (_requestMaxServeTime > 0 && _requestMaxServeTime < elapsedMilliseconds)
                {
                    LogMessage(LogLevel.Warning, $"Request /{GetNormalizedRequestUri(context)} from {context.Request.RemoteEndPoint} took {elapsedMilliseconds} milliseconds to process!");
                }
                context.Response.Close();
                LogMessage(LogLevel.Debug, $"Response for {context.Request.RemoteEndPoint} queued ({elapsedMilliseconds} milliseconds)");
            }
            catch (HttpListenerException)
            {
                LogMessage(LogLevel.Info, "Sudden client disconnect, failed to send response");
            }
            catch (Exception ex)
            {
                LogException(LogLevel.Warning, ex);
            }
        }

        private static double _requestMaxServeTime;
        #endregion

        #region service

        private static FileRequestController<TAccount> _fileRequestController;
        private static OptionsController<TAccount> _optionsController;
        private static PingController _pingController;
        private static AuthentificationController _authentificationController;
        private static StatisticsController<TAccount> _statisticsController;

        private static readonly List<IServiceController> ServiceRequestProcessors = new List<IServiceController>();

        private static readonly List<string> ServiceUris = new List<string>();

        #endregion

        #region statistics

        public static bool StatisticsEnabled => _statisticsController.StatisticsEnabled;

        public static string GetStatistics() => _statisticsController.Statistics?.Serialize();

        #endregion

        #region files

        public static bool FilesEnabled => _fileRequestController.FilesEnabled;

        public static string FilesLocation => _fileRequestController.FilesLocation;

        public static string FilesBaseUri => _fileRequestController.FilesBaseUri;

        public static bool FilesNeedAuthorization => _fileRequestController.FilesNeedAuthorization;

        public static List<FileSection> FileSections => _fileRequestController.FileSections;

        public static bool FileExists(string sectionName, string filename)
            => FilesEnabled && _fileRequestController.FileExists(sectionName, filename);

        public static Stream GetFileStream(string sectionName, string filename)
            => !FilesEnabled ? null : _fileRequestController.GetFileStream(sectionName, filename);

        public static async Task<string> GetFileString(string sectionName, string filename)
        {
            if (!FilesEnabled)
                return null;
            return await _fileRequestController.GetFileString(sectionName, filename);
        }

        public static async Task<FileOperationStatus> AddFile(string sectionName, string filename, Stream content)
        {
            if (!FilesEnabled)
                return FileOperationStatus.FilesNotEnabled;
            return await _fileRequestController.AddFile(sectionName, filename, content);
        }

        public static FileOperationStatus DeleteFile(string sectionName, string filename)
            => !FilesEnabled ? FileOperationStatus.FilesNotEnabled : _fileRequestController.DeleteFile(sectionName, filename);

        #endregion

        #region requests

        private static bool RequestProcessorDuplicates(RequestProcessor<TAccount> first,
            RequestProcessor<TAccount> second)
        {
            if (first == null || second == null)
                return false;

            return first.SubUri == second.SubUri && first.Method == second.Method;
        }

        public static bool AddRequestProcessor(RequestProcessor<TAccount> requestProcessor)
        {
            if (CheckRequestProcessor(requestProcessor) != true)
                return false;

            requestProcessor.SubUri = requestProcessor.SubUri.Trim('/');
            var duplicate = InnerRequestProcessors.FirstOrDefault(rp => RequestProcessorDuplicates(rp, requestProcessor));
            if (duplicate != null)
                InnerRequestProcessors.Remove(duplicate);

            InnerRequestProcessors.Add(requestProcessor);
            return true;
        }

        public static bool AddRequestProcessorRange(
            IEnumerable<RequestProcessor<TAccount>> requestProcessors)
            => requestProcessors != null && requestProcessors.All(AddRequestProcessor);

        public static bool AddStaticRedirect(string fromUri, string toUri)
        {
            var preprocessed = PreprocessRedirect(fromUri, toUri);
            if (!preprocessed.Item3)
                return false;
            return InnerStaticRedirectionTable.TryAdd(preprocessed.Item1, preprocessed.Item2);
        }

        public static bool AddStaticRedirectRange(IDictionary<string, string> uriTable)
        {
            return uriTable.All(up => AddStaticRedirect(up.Key, up.Value));
        }

        private static bool CheckRequestProcessor(RequestProcessor<TAccount> requestProcessor)
        {
            if (requestProcessor.SubUri == null)
                return false;

            if (requestProcessor.Method == HttpMethod.Options || requestProcessor.Method == HttpMethod.Head)
            {
                LogMessage(LogLevel.Warning, $"Cannot explicitely register {requestProcessor.Method.Method.ToUpperInvariant()} processor");
                return false;
            }

            var serviceDuplicate = ServiceUris.FirstOrDefault(su => requestProcessor.SubUri.Trim('/').StartsWith(su));
            if (serviceDuplicate != null)
            {
                LogMessage(LogLevel.Warning, $"Failed to register uri '{requestProcessor.SubUri}'. Section '{serviceDuplicate}' is reserved.");
                return false;
            }

            return true;
        }

        private static Tuple<string, string, bool> PreprocessRedirect(string fromUri, string toUri)
        {
            if (fromUri == null || toUri == null)
                return new Tuple<string, string, bool>(null, null, false);

            if (InnerStaticRedirectionTable.ContainsKey(fromUri))
                return new Tuple<string, string, bool>(null, null, false);

            return new Tuple<string, string, bool>(fromUri.Trim('/'), toUri.Trim('/'), true);
        }

        private static RequestProcessorSelectionResult<TAccount> SelectRequestProcessor(HttpListenerRequest request)
        {
            var requestMethod = CommonHelper.HttpMethodToEnum(request.HttpMethod);
            var localUri = request.Url.LocalPath.Trim('/');
            if (requestMethod == HttpMethod.Get || requestMethod == HttpMethod.Head)
            {
                string redirectionTarget;
                if (InnerStaticRedirectionTable.TryGetValue(localUri, out redirectionTarget))
                {
                    var correspondedProcessor =
                        InnerRequestProcessors.FirstOrDefault(
                            rp => rp.Method == HttpMethod.Get && rp.SubUri.Trim('/') == redirectionTarget);
                    if (correspondedProcessor != null)
                    {
                        return new RequestProcessorSelectionResult<TAccount>
                        {
                            RequestProcessor = correspondedProcessor,
                            MethodMatches = true,
                            IsRedirect = true
                        };
                    }
                }
            }

            var processorsForUri = InnerRequestProcessors.Where(requestProcessor => CommonHelper.UriMatchesString(request.Url, requestProcessor.SubUri)).ToList();
            if (processorsForUri.Count == 0)
                return null;
            var suitableProcessor = processorsForUri.FirstOrDefault(rp => rp.Method == requestMethod || requestMethod == HttpMethod.Head && rp.Method == HttpMethod.Get);
            return new RequestProcessorSelectionResult<TAccount>
            {
                RequestProcessor = suitableProcessor,
                MethodMatches = suitableProcessor != null,
                IsRedirect = false
            };
        }

        private static Delegates.Authorizer<TAccount> _authorizer;

        public static Dictionary<string, string> StaticRedirectionTable => new Dictionary<string, string>(InnerStaticRedirectionTable);
        public static List<RequestProcessor<TAccount>> RequestProcessors => new List<RequestProcessor<TAccount>>(InnerRequestProcessors);

        private static readonly List<RequestProcessor<TAccount>> InnerRequestProcessors =
            new List<RequestProcessor<TAccount>>();
        private static readonly ConcurrentDictionary<string, string> InnerStaticRedirectionTable = new ConcurrentDictionary<string, string>();

        #endregion
    }
}