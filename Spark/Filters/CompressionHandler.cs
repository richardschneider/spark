﻿#region Copyright (c) Orion Health Asia Pacific Limited and the Orion Health Group of companies (2001 - 2013).

// Original author: Richard Schneider (makaretu@gmail.com)

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
#if CommonLogging
using Common.Logging;
#endif
using Hl7.Fhir.Model;

namespace Spark.Filters // Original: Orchestral.Fhir.Http
{
    /// <summary>
    ///   A GZip encoder/decoder for a HTTP messages.
    /// </summary>
    public class CompressionHandler : DelegatingHandler
    {
#if CommonLogging
        static ILog log = LogManager.GetCurrentClassLogger();
#endif

        /// <summary>
        ///  The MIME types that will not be compressed.
        /// </summary>
        string[] mediaTypeBlacklist = new[] 
        { 
            "image/", "audio/", "video/",
            "application/x-rar-compressed", 
            "application/zip", "application/x-gzip", 
        };

        /// <summary>
        ///   The compressors that are supported.
        /// </summary>
        /// <remarks>
        ///   The key is the value of an "Accept-Encoding" HTTP header.
        /// </remarks>
        Dictionary<string, Func<HttpContent, HttpContent>> compressors = new Dictionary<string, Func<HttpContent, HttpContent>>(StringComparer.InvariantCultureIgnoreCase)
        {
            { "gzip",  (c) => new GZipContent(c) }
        };

        /// <summary>
        ///   The decompressors that are supported.
        /// </summary>
        /// <remarks>
        ///   The key is the value of an "Content-Encoding" HTTP header.
        /// </remarks>
        Dictionary<string, Func<HttpContent, HttpContent>> decompressors = new Dictionary<string, Func<HttpContent, HttpContent>>(StringComparer.InvariantCultureIgnoreCase)
        {
            { "gzip",  (c) => new GZipCompressedContent(c) }
        };

        /// <inheritdoc />
        async protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
#if CommonLogging
            if (log.IsDebugEnabled)
                log.Debug("Begin");
#endif

            // Decompress the request content, if needed.
            if (request.Content != null && request.Content.Headers.ContentEncoding.Count > 0)
            {
#if CommonLogging
                if (log.IsDebugEnabled)
                    log.Debug("Decompressing request");
#endif
                var encoding = request.Content.Headers.ContentEncoding.First();
                Func<HttpContent, HttpContent> decompressor;
                if (!decompressors.TryGetValue(encoding, out decompressor))
                {
                    var outcome = new OperationOutcome
                    {
                        Issue = new List<OperationOutcome.OperationOutcomeIssueComponent>()
                        {
                            new OperationOutcome.OperationOutcomeIssueComponent
                            {
                                Details = string.Format("The Content-Encoding '{0}' is not supported.", encoding),
                                Severity = OperationOutcome.IssueSeverity.Error,
                            }
                        }
                    };
                    throw new HttpResponseException(request.CreateResponse(HttpStatusCode.BadRequest, outcome));
                }
                request.Content = decompressor(request.Content);
            }

            // Wait for the response.
            var response = await base.SendAsync(request, cancellationToken);

#if CommonLogging
            if (log.IsDebugEnabled)
                log.Debug("Got response");
#endif

            // Is the media type blacklisted; because compression does not help?
            if (response == null
                || response.Content == null
                || response.Content.Headers.ContentType == null
                || mediaTypeBlacklist.Any(s => response.Content.Headers.ContentType.MediaType.StartsWith(s)))
                return response;

            // If the client has requested compression and the compression algorithm is known, 
            // then compress the response.
            if (request.Headers.AcceptEncoding != null)
            {
                var compressor = request.Headers.AcceptEncoding
                    .Where(e => !e.Quality.HasValue || e.Quality != 0)
                    .Where(e => compressors.ContainsKey(e.Value))
                    .OrderByDescending(e => e.Quality ?? 1.0)
                    .Select(e => compressors[e.Value])
                    .FirstOrDefault();
                if (compressor != null)
                {
                    response.Content = compressor(response.Content);
                }
            }

#if CommonLogging
            if (log.IsDebugEnabled)
                log.Debug("Compressing response");
#endif
            return response;
        }
    }
}
