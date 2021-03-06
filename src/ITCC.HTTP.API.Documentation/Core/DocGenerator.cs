﻿// This is an open source non-commercial project. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ITCC.HTTP.API.Attributes;
using ITCC.HTTP.API.Documentation.Enums;
using ITCC.HTTP.API.Documentation.Utils;
using ITCC.HTTP.API.Enums;
using ITCC.HTTP.API.Extensions;
using ITCC.HTTP.API.Interfaces;
using ITCC.HTTP.API.Utils;
using ITCC.HTTP.Common.Interfaces;
using ITCC.Logging.Core;

namespace ITCC.HTTP.API.Documentation.Core
{
    public class DocGenerator
    {
        #region public

        public DocGenerator()
        {
            _builder = new StringBuilder();
            _viewDocGenerator = new ViewDocGenerator(_builder);
        }

        public DocGenerator(int capacity)
        {
            _builder = new StringBuilder(capacity);
            _viewDocGenerator = new ViewDocGenerator(_builder);
        }

        public async Task<bool> GenerateApiDocumentationAsync(Type markerType, Stream outputStream, IEnumerable<IExampleSerializer> serializers)
        {
            _outputStream = outputStream;

            if (!TryLoadTargetAssembly(markerType))
                return false;

            if (!AssemblyIsValid())
            {
                LogWarning("Assembly configuration is invalid, generation cancelled");
                return false;
            }

            LogDebug("Writing header");
            if (!await TryWriteHeaderAsync())
            {
                LogDebug("Failed to write header!");
                return false;
            }
            LogDebug("Header written");

            LogDebug("Configuring view doc generator");
            if (!TrySetViewDocGeneratorSettings(serializers))
            {
                LogDebug("Failed configure doc generator!");
                return false;
            }
            LogDebug("View doc generator configured");

            _builder.AppendLine();
            LogDebug("Writing sections");
            if (!WriteAllSections())
            {
                LogDebug("Failed to write sections!");
                return false;
            }
            LogDebug("Sections written");

            LogDebug("Writing footer");
            if (!await TryWriteFooterAsync())
            {
                LogDebug("Failed to write footer!");
                return false;
            }
            LogDebug("Footer written");

            return await TryWriteResultAsync();
        }

        #endregion

        #region protected

        #region methods
        /// <summary>
        ///     Method used to write global doc header (before any API sections).
        ///     Defaults to `# API Description` header
        /// </summary>
        /// <returns>Success status</returns>
        /// <remarks>Should not throw</remarks>
        protected virtual Task<bool> TryWriteHeaderAsync()
        {
            _builder.AppendLine("# API Description");
            return Task.FromResult(true);
        }

        /// <summary>
        ///     Method used to write global doc footer (after all API sections).
        ///     Defaults to empty footer.
        /// </summary>
        /// <returns>Success status</returns>
        /// <remarks>Should not throw</remarks>
        protected virtual Task<bool> TryWriteFooterAsync()
        {
            _builder.AppendLine();
            return Task.FromResult(true);
        }

        /// <summary>
        ///     Method used to describe single API contract bullet
        /// </summary>
        /// <param name="contractType">Contract type</param>
        /// <returns>Description string</returns>
        /// <remarks>Should not throw</remarks>
        protected virtual string GetApiContractPartDescription(ApiContractType contractType)
            => EnumHelper.ApiContractTypeName(contractType);

        /// <summary>
        ///     Method used to describe API method state. Defaults to <see cref="EnumInfoProvider.GetElementName"/>
        /// </summary>
        /// <param name="state">Api method state</param>
        /// <returns>State description</returns>
        protected virtual string GetApiMethodStateName(ApiMethodState state)
            => EnumInfoProvider.GetElementName(state);

        /// <summary>
        ///     Gets simple type name (such as int, string, List of ints...)
        /// </summary>
        /// <param name="type">Type</param>
        /// <returns>Name string</returns>
        /// <remarks>Should not throw</remarks>
        protected virtual string GetSimpleTypeName(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
                ? $"List of {type.GenericTypeArguments[0].FullName}"
                : type.FullName;

        /// <summary>
        ///     Method used to separate API method into sections (flat structure)
        /// </summary>
        /// <param name="rawPropertyInfos">All API request processor infos</param>
        /// <returns>List of method sections.</returns>
        /// <remarks>
        ///     1) Should not throw. <br/>
        ///     2) Should order infos inside each section.
        /// </remarks>
        protected virtual IEnumerable<RequestProcessorSection> SplitSections(List<PropertyInfo> rawPropertyInfos)
            => new List<RequestProcessorSection>
            {
                new RequestProcessorSection
                {
                    TitlePattern = "### All methods",
                    DescriptionPattern = null,
                    RequestProcessorInfos = rawPropertyInfos
                }
            };

        #endregion

        #region substitutions

        /// <summary>
        ///     Word used as `Yes`
        /// </summary>
        protected virtual string YesWordPattern => @"Yes";
        /// <summary>
        ///     Word used as `No`
        /// </summary>
        protected virtual string NoWordPattern => @"No";
        /// <summary>
        ///     Phrase used to mark method state
        /// </summary>
        protected virtual string StateDesccriptionPattern => @"Method state: ";
        /// <summary>
        ///     Phrase used to mark if method requires authorization
        /// </summary>
        protected virtual string AuthDescriptionPattern => @"Authorization required: ";
        /// <summary>
        ///     String to preceed request processor HTTP method and suburi
        /// </summary>
        protected virtual string MethodHeaderPrefixPattern => @"#### ";
        /// <summary>
        ///     Header used for query params description
        /// </summary>
        protected virtual string QueryParamsPattern => @"##### Query params";
        /// <summary>
        ///     Sign used to preceed every query param
        /// </summary>
        protected virtual string QueryParamPrefixPattern => @"*";
        /// <summary>
        ///     String used to signal than query parameter is required
        /// </summary>
        protected virtual string QueryParamRequiredPattern => @"**Required**";
        /// <summary>
        ///     String used to signal than query parameter is optiional
        /// </summary>
        protected virtual string QueryParamOptionalPattern => @"**Optional**";
        /// <summary>
        ///     Header used for request body description
        /// </summary>
        protected virtual string RequestBodyTypePattern => @"##### Request body";
        /// <summary>
        ///     Header used for response body description
        /// </summary>
        protected virtual string ResponseBodyTypePattern => @"##### Response body";
        /// <summary>
        ///     Phrase used when the body option is <see cref="API.Utils.Empty"/>
        /// </summary>
        protected virtual string EmptyBodyPattern => @"Empty body";
        /// <summary>
        ///     Pattern used to describe that request/response body can be a single object of a particular type
        /// </summary>
        protected virtual string SingleObjectPattern => @"Object of type";
        /// <summary>
        ///     Pattern used to describe that request/response body can be list of objects of a particular type
        /// </summary>
        protected virtual string ObjectListPattern => @"List of objects of type";
        /// <summary>
        ///     Pattern used to describe that request/response body can be either a single object or object list of a particular type
        /// </summary>
        protected virtual string SingleObjectOrListPattern => @"Single object or object list of type";
        /// <summary>
        ///     Header used for method remarks section
        /// </summary>
        protected virtual string RemarksHeaderPattern => @"##### Remarks";

        #region separators

        /// <summary>
        ///     String used to separate API method descriptions from each other
        /// </summary>
        protected virtual string MethodSeparatorPattern => @"------------------------------";
        /// <summary>
        ///     String used to separate API method sections from each other
        /// </summary>
        protected virtual string SectionSeparatorPattern => @"------------------------------";

        #endregion

        #region views

        /// <summary>
        ///     Used when no contract is applied to API view property
        /// </summary>
        protected virtual string NoPropertyContractPattern => @"None";
        /// <summary>
        ///     Used when no description (or null description) is provided
        /// </summary>
        protected virtual string NoPropertyDescriptionPattern => @"No description provided";
        /// <summary>
        ///     Set if example serializers are null or empty
        /// </summary>
        protected virtual string NoExampleAvailablePattern => @"No example available";
        /// <summary>
        ///     View example starts with a following line
        /// </summary>
        protected virtual string ExampleStartPattern => @"```";
        /// <summary>
        ///     View example ends with a following line
        /// </summary>
        protected virtual string ExampleEndPattern => @"```";
        /// <summary>
        ///     Examples section header (for method request and response bodies)
        /// </summary>
        protected virtual string ExamplesHeaderPattern => @"###### Examples";
        /// <summary>
        ///     Description and restrictions section header (for method request and response bodies)
        /// </summary>
        protected virtual string DescriptionAndRestrictionsPattern => @"###### Description and restrictions";
        /// <summary>
        ///     Header used to preceed <see cref="ApiViewCheckAttribute"/> descriptions section
        /// </summary>
        protected virtual string AdditionalChecksHeaderPattern => @"Additional checks:";

        #endregion

        #endregion

        #endregion

        #region private

        private bool TryLoadTargetAssembly(Type markerType) => Wrappers.DoSafe(() =>
        {
            _targetAssembly = Assembly.GetAssembly(markerType);
            LogDebug($"Assembly {_targetAssembly.FullName} loaded");
            return true;
        });

        private bool AssemblyIsValid() => Wrappers.DoSafe(() =>
        {
            var properties = GetAllProperties();

            var apiMethodAnnotatedProperties = properties
                .Where(p => p.GetCustomAttributes<ApiRequestProcessorAttribute>().Any())
                .ToList();
            if (!apiMethodAnnotatedProperties.Any())
            {
                LogWarning("Assembly does not contain any annotated API members, failed to generate docs");
                return false;
            }

            var staticRequestProcessorProperties = properties
                .Where(p => p.GetGetMethod().IsStatic
                    && p.ExtendsOrImplements<IRequestProcessor>())
                .ToList();

            var incorrectAnnotatedProperties = apiMethodAnnotatedProperties
                .Except(staticRequestProcessorProperties)
                .ToList();
            if (incorrectAnnotatedProperties.Any())
            {
                var incorrectAnnotatedPropertiesDescription = string.Join("\n",
                    incorrectAnnotatedProperties.Select(
                        p => $"{p.PropertyType.GetGenericArguments().First().FullName}.{p.Name}"));
                LogWarning(
                    $"The following properties are annotated as API members but do not implement IRequestProcessor:\n{incorrectAnnotatedPropertiesDescription}");
                return false;
            }

            _apiMemberPropertyInfos = apiMethodAnnotatedProperties;
            return true;
        });

        private bool TrySetViewDocGeneratorSettings(IEnumerable<IExampleSerializer> serializers)
        {
            var settings = new ViewDocGeneratorSettings
            {
                Serializers = serializers,
                NoPropertyContractPattern = NoPropertyContractPattern,
                NoPropertyDescriptionPattern = NoPropertyDescriptionPattern,
                NoExampleAvailablePattern = NoExampleAvailablePattern,
                ExampleStartPattern = ExampleStartPattern,
                ExampleEndPattern = ExampleEndPattern,
                ExamplesHeaderPattern = ExamplesHeaderPattern,
                DescriptionAndRestrictionsPattern = DescriptionAndRestrictionsPattern,
                TypeNameFunc = type => GetSimpleTypeName(type),
                AdditionalChecksHeaderPattern = AdditionalChecksHeaderPattern
            };
            return _viewDocGenerator.SetSettings(settings);
        }

        private bool WriteAllSections() => Wrappers.DoSafe(() =>
        {
            var sections = SplitSections(_apiMemberPropertyInfos);

            foreach (var section in sections)
            {
                WriteOneSection(section);
                _builder.AppendLine(SectionSeparatorPattern);
            }

            return true;
        });

        private void WriteOneSection(RequestProcessorSection section)
        {
            WriteSectionHeader(section);
            WriteSectionContents(section);
        }

        private void WriteSectionHeader(RequestProcessorSection section)
        {
            _builder.AppendLine(section.TitlePattern);
            _builder.AppendLine();

            if (section.DescriptionPattern == null)
                return;
            _builder.AppendLine(section.DescriptionPattern);
            _builder.AppendLine();
        }

        private void WriteSectionContents(RequestProcessorSection section)
        {
            foreach (var propertyInfo in section.RequestProcessorInfos)
            {
                _builder.AppendLine(MethodSeparatorPattern);
                WriteRequestProcessorInfo(propertyInfo);
                _builder.AppendLine();
            }
        }

        private void WriteRequestProcessorInfo(MemberInfo info)
        {
            WriteRequestProcessorHeader(info);
            WriteRequestProcessorQueryParams(info);

            WriteRequestProcessorRequestDescription(info);
            WriteRequestProcessorResponseDescription(info);

            WriteRequestProcessorRemarks(info);
        }

        private static ApiRequestProcessorAttribute GetApiRequestProcessorAttributeUnsafe(MemberInfo info)
        {
            var processorAttribute = info.GetCustomAttribute<ApiRequestProcessorAttribute>();
            if (processorAttribute == null)
                throw new InvalidOperationException("ApiRequestProcessorAttribute not found");

            return processorAttribute;
        }

        private void WriteRequestProcessorHeader(MemberInfo info)
        {
            var processorAttribute = GetApiRequestProcessorAttributeUnsafe(info);

            var uriInfo = $"{processorAttribute.Method.ToString().ToUpperInvariant()} {processorAttribute.SubUri}";
            _builder.AppendLine($"{MethodHeaderPrefixPattern}{uriInfo}");
            Wrappers.AppendPaddedLines(_builder, processorAttribute.Description);
            WriteRequestProcessorState(info);
            var authWord = processorAttribute.AuthRequired ? YesWordPattern : NoWordPattern;
            _builder.AppendLine($"{AuthDescriptionPattern}{authWord}");
            _builder.AppendLine();
        }

        private void WriteRequestProcessorState(MemberInfo info)
        {
            var statusAttribute = info.GetCustomAttributes<ApiMethodStatusAttribute>().FirstOrDefault();
            if (statusAttribute == null)
                return;

            var statusDescription = $"{StateDesccriptionPattern}{GetApiMethodStateName(statusAttribute.State)}";
            if (!string.IsNullOrWhiteSpace(statusAttribute.Comment))
                statusDescription += $" ({statusAttribute.Comment})";

            _builder.AppendLine(statusDescription);
        }

        private void WriteSingleQueryParamDescription(ApiQueryParamAttribute attribute)
        {
            var paramRequirement = attribute.Optional ? QueryParamOptionalPattern : QueryParamRequiredPattern;
            var line = $"{QueryParamPrefixPattern} `{attribute.Name}` - {attribute.Description}. {paramRequirement}";
            _builder.AppendLine(line);
        }

        private void WriteRequestProcessorQueryParams(MemberInfo info)
        {
            var queryParamAttributes = info.GetCustomAttributes<ApiQueryParamAttribute>().ToList();
            if (!queryParamAttributes.Any())
                return;

            Wrappers.AppendPaddedLines(_builder, QueryParamsPattern);

            foreach (var queryParamAttribute in queryParamAttributes)
            {
                WriteSingleQueryParamDescription(queryParamAttribute);
            }
            _builder.AppendLine();
        }

        private string GetCountDescription(BodyTypeInfo bodyTypeInfo)
        {
            switch (bodyTypeInfo.CountType)
            {
                case ObjectCountType.Single:
                    return SingleObjectPattern;
                case ObjectCountType.List:
                    return ObjectListPattern;
                case ObjectCountType.SingleOrList:
                    return SingleObjectOrListPattern;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void WriteBodyTypeInfo(BodyTypeInfo bodyTypeInfo)
        {
            var type = bodyTypeInfo.Type;
            if (type == typeof(Empty))
            {
                _builder.AppendLine(EmptyBodyPattern);
                return;
            }

            var countDescription = GetCountDescription(bodyTypeInfo);
            _builder.AppendLine($"{countDescription} `{type.FullName}`");

            _viewDocGenerator.WriteBodyDescription(bodyTypeInfo.Type);
        }

        private void WriteTypeAttributeDescription(ITypeAttribute attribute)
        {
            var bodyTypeInfos = attribute.GetTypes();

            foreach (var bodyTypeInfo in bodyTypeInfos)
            {
                WriteBodyTypeInfo(bodyTypeInfo);
                _builder.AppendLine();
            }
        }

        private void WriteRequestProcessorRequestDescription(MemberInfo info)
        {
            var requestBodyTypeAttribute = info.GetCustomAttributes<ApiRequestBodyTypeAttribute>().FirstOrDefault();
            if (requestBodyTypeAttribute == null)
                return;

            _builder.AppendLine(RequestBodyTypePattern);
            _builder.AppendLine();
            
            WriteTypeAttributeDescription(requestBodyTypeAttribute);
        }

        private void WriteRequestProcessorResponseDescription(MemberInfo info)
        {
            var responseBodyTypeAttribute = info.GetCustomAttributes<ApiResponseBodyTypeAttribute>().FirstOrDefault();
            if (responseBodyTypeAttribute == null)
                return;

            _builder.AppendLine(ResponseBodyTypePattern);
            _builder.AppendLine();

            WriteTypeAttributeDescription(responseBodyTypeAttribute);
        }

        private void WriteRequestProcessorRemarks(MemberInfo info)
        {
            var processorAttribute = GetApiRequestProcessorAttributeUnsafe(info);

            if (string.IsNullOrWhiteSpace(processorAttribute.Remarks))
                return;

            Wrappers.AppendPaddedLines(_builder, RemarksHeaderPattern, processorAttribute.Remarks);
        }

        private Task<bool> TryWriteResultAsync() => Wrappers.DoSafeAsync(async () =>
        {
            var result = _builder.ToString();
            LogDebug($"Total symbols: {_builder.Length} (Capacity used: {_builder.Capacity})");
            _builder.Clear();
            using (var writer = new StreamWriter(_outputStream))
            {
                await writer.WriteAsync(result);
                await writer.FlushAsync();
            }

            return true;
        });

        private List<PropertyInfo> GetAllProperties() => _targetAssembly.GetLoadableTypes().SelectMany(t => t.GetProperties()).ToList();

        private static void LogDebug(string message) => Logger.LogDebug(LogScope, message);
        private static void LogWarning(string message) => Logger.LogEntry(LogScope, LogLevel.Warning, message);

        private Stream _outputStream;
        private readonly StringBuilder _builder;
        private Assembly _targetAssembly;
        private List<PropertyInfo> _apiMemberPropertyInfos;
        private readonly ViewDocGenerator _viewDocGenerator;

        private const string LogScope = "DOC GENER";

        #endregion
    }
}
