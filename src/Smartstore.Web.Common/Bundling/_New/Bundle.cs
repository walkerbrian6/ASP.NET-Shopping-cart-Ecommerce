﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Smartstore.IO;
using Smartstore.Web.Bundling.Processors;

namespace Smartstore.Web.Bundling
{
    public class BundleFile
    {
        public string Path { get; init; }
        public IFileInfo File { get; init; }
        public IFileProvider FileProvider { get; init; }
    }
    
    /// <summary>
    /// Represents a list of file references to be bundled together as a single resource.
    /// </summary>
    /// <remarks>
    /// A bundle is referenced statically via the <see cref="Route"/> property (i.e. Route = ~/bundle/js/public.js).
    /// </remarks>
    public class Bundle
    {
        private static readonly ConcurrentDictionary<string, string> _minFiles = new(StringComparer.InvariantCultureIgnoreCase);
        private readonly HashSet<string> _sourceFiles = new();
        
        protected Bundle()
        {
        }

        public Bundle(string route, string contentType, params IBundleProcessor[] processors)
            : this(route, contentType, null, processors)
        {
        }

        public Bundle(string route, string contentType, IFileProvider fileProvider, params IBundleProcessor[] processors)
        {
            Guard.NotEmpty(route, nameof(route));
            Guard.NotEmpty(contentType, nameof(contentType));

            Route = ValidateRoute(NormalizeRoute(route));
            ContentType = contentType;
            FileProvider = fileProvider;

            Processors.AddRange(processors);
        }

        #region Init & Util

        private static string ValidateRoute(string route)
        {
            if (route.IndexOfAny(new[] { '*', '[', '?' }) > -1)
            {
                throw new ArgumentException($"The route \"{route}\" appears to be a globbing pattern which isn't supported for bundle routes.", nameof(route));
            }

            return route;
        }

        public static string NormalizeRoute(string route)
        {
            route = route.Trim();
            var normalizedRoute = route[0] == '/' || route[0] == '~'
                ? "/" + route.Trim().TrimStart('~', '/')
                : route;

            var index = normalizedRoute.IndexOfAny(new[] { '?', '#' });
            if (index > -1)
            {
                normalizedRoute = normalizedRoute.Substring(0, index);
            }

            return normalizedRoute;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Virtual path used to reference the <see cref="Bundle"/> from within a view or Web page.
        /// </summary>
        public string Route { get; }

        /// <summary>
        /// Gets the bundle content type.
        /// </summary>
        public string ContentType { get; }

        /// <summary>
        /// The token inserted between bundled files to ensure that the final bundle content is valid
        /// </summary>
        /// <remarks>
        /// By default, if <see cref="ConcatenationToken"/> is not specified, the bundling framework inserts a new line.
        /// </remarks>
        public string ConcatenationToken { get; set; } = Environment.NewLine;

        /// <summary>
        /// Source files that represent the contents of the bundle. 
        /// Globbing patterns are allowed.
        /// </summary>
        public IEnumerable<string> SourceFiles 
        {
            get => _sourceFiles.AsReadOnly();
        }

        /// <summary>
        /// The file provider to use for file resolution. 
        /// If <c>null</c>, <see cref="BundlingOptions.FileProvider"/> will be used instead.
        /// </summary>
        public IFileProvider FileProvider { get; set; }

        /// <summary>
        /// The list of processor for the bundle.
        /// </summary>
        public IList<IBundleProcessor> Processors { get; } = new List<IBundleProcessor>();

        #endregion

        #region Fluent

        /// <summary>
        /// Adds bundle processors to the bundle processing pipeline.
        /// </summary>
        public Bundle AddProcessor(params IBundleProcessor[] processors)
        {
            Processors.AddRange(processors);
            return this;
        }

        /// <summary>
        /// Specifies a set of files to be included in the <see cref="Bundle"/>.
        /// </summary>
        /// <param name="paths">The virtual path of the file or file pattern to be included in the bundle.</param>
        /// <returns>The <see cref="Bundle"/> object itself for use in subsequent method chaining.</returns>
        /// <remarks>
        /// To bundle all files from a particular folder, you can use globbing patterns like this: "css/**/*.css".
        /// </remarks>
        public virtual Bundle Include(params string[] paths)
        {
            _sourceFiles.AddRange(paths);
            return this;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets the cache key associated with this bundle.
        /// </summary>
        public virtual string GetCacheKey(HttpContext httpContext, BundlingOptions options)
        {
            Guard.NotNull(httpContext, nameof(httpContext));
            Guard.NotNull(options, nameof(options));

            var cacheKey = "bundle:" + Route;

            foreach (var processor in Processors)
            {
                try
                {
                    var processorKey = processor.GetCacheKey(httpContext, options);
                    if (processorKey.HasValue())
                    {
                        cacheKey += processorKey;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"CacheKey generation failed in '{processor.GetType().FullName}' bundle processor.", ex);
                }
            }

            return cacheKey;
        }

        public virtual IEnumerable<BundleFile> EnumerateFiles(HttpContext httpContext, BundlingOptions options)
        {
            Guard.NotNull(httpContext, nameof(httpContext));
            Guard.NotNull(options, nameof(options));

            var fileProvider = 
                FileProvider ?? 
                options.FileProvider ??
                httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;

            var files = new Dictionary<string, BundleFile>();

            foreach (var source in SourceFiles)
            {
                foreach (var file in ExpandFile(source, fileProvider, httpContext, options))
                {
                    // To prevent duplicates
                    files[file.Path] = file;
                }
            }

            return files.Values;
        }

        /// <summary>
        /// Loads an included bundle file into memory.
        /// </summary>
        /// <param name="bundleFile">The included file</param>
        /// <returns>The loaded bundle file content.</returns>
        public virtual async Task<AssetContent> LoadContentAsync(BundleFile bundleFile)
        {
            Guard.NotNull(bundleFile, nameof(bundleFile));

            using var stream = bundleFile.File.CreateReadStream();
            var content = await stream.AsStringAsync();

            return new AssetContent
            {
                Content = content,
                Path = bundleFile.Path,
                ContentType = MimeTypes.MapNameToMimeType(bundleFile.Path),
                LastModifiedUtc = bundleFile.File.LastModified
            };
        }

        public virtual async Task<BundleResponse> GenerateBundleResponseAsync(BundleContext context)
        {
            Guard.NotNull(context, nameof(context));

            foreach (var processor in Processors)
            {
                await processor.ProcessAsync(context);
            }

            if (context.Content.Count > 1)
            {
                await new ConcatProcessor().ProcessAsync(context);
            }

            var combined = context.Content.FirstOrDefault();

            var response = new BundleResponse
            {
                Route = Route,
                CreationDate = combined?.LastModifiedUtc ?? DateTimeOffset.UtcNow,
                Content = combined?.Content,
                ContentType = combined?.ContentType,
                FileProvider = context.Files.FirstOrDefault().FileProvider,
                ProcessorCodes = context.ProcessorCodes.ToArray()
            };

            return response;
        }

        protected virtual IEnumerable<BundleFile> ExpandFile(string path, IFileProvider fileProvider, HttpContext httpContext, BundlingOptions options)
        {
            var isPattern = path.IndexOf('*') > -1;
            
            if (!isPattern)
            {
                path = TryFindMinFile(path, fileProvider, options);
                yield return new BundleFile
                {
                    Path = path,
                    File = fileProvider.GetFileInfo(path),
                    FileProvider = fileProvider
                };
            }
            else
            {
                // Process glob pattern
                var fileInfo = fileProvider.GetFileInfo("/");
                var root = fileInfo.PhysicalPath;

                if (root != null)
                {
                    var dir = new DirectoryInfoWrapper(new DirectoryInfo(root));
                    var matcher = new Matcher();
                    matcher.AddInclude(path);
                    var globbingResult = matcher.Execute(dir);
                    var fileMatches = globbingResult.Files.Select(f => f.Path.Replace(root, string.Empty));

                    foreach (var fileMatch in fileMatches)
                    {
                        yield return new BundleFile
                        {
                            Path = fileMatch,
                            File = fileProvider.GetFileInfo(fileMatch),
                            FileProvider = fileProvider
                        };
                    }
                }
            }
        }

        private static string TryFindMinFile(string path, IFileProvider fileProvider, BundlingOptions options)
        {
            if (options.EnableMinification == false)
            {
                // Return path as is in dev mode
                return path;
            }

            return _minFiles.GetOrAdd(path, key => 
            {
                try
                {
                    var extension = Path.GetExtension(path);
                    if (path.EndsWith(".min" + extension, StringComparison.InvariantCultureIgnoreCase))
                    {
                        // Is already a minified file, get out!
                        return path;
                    }

                    var minPath = "{0}.min{1}".FormatInvariant(path.Substring(0, path.Length - extension.Length), extension);
                    if (fileProvider.GetFileInfo(minPath).Exists)
                    {
                        return minPath;
                    }

                    return path;
                }
                catch
                {
                    return path;
                }
            });
        }

        #endregion
    }
}
