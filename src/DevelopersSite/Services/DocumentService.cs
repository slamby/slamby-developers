﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DevelopersSite.Helpers;
using DevelopersSite.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace DevelopersSite.Services
{
    public class DocumentService
    {
        private const string documentsFolder = "documents";
        string documentsRootFolder = string.Empty;
        SiteConfig _siteConfig;
        string _cdnUrl;
        string _webRootPath;
        string[] defaultDocs = { "index.html" };

        public List<DocumentModel> Documents { get; } = new List<DocumentModel>();

        public DocumentService(IHostingEnvironment env, IOptions<SiteConfig> options)
        {
            _webRootPath = env.WebRootPath;
            _siteConfig = options.Value;
            documentsRootFolder = Path.Combine(_webRootPath, documentsFolder);
            _cdnUrl = options.Value.CdnUrl;

            ScanDocuments();
        }

        public bool IsDefaultDoc(string filename)
        {
            return defaultDocs.Contains(filename, StringComparer.OrdinalIgnoreCase);
        }

        public void ScanDocuments()
        {
            var docList = new List<DocumentModel>();

            foreach (var typeFolder in Directory.EnumerateDirectories(documentsRootFolder))
            {
                var document = new DocumentModel();

                document.Product = Path.GetFileName(typeFolder);
                document.Title = _siteConfig.Products[document.Product];

                var versionDirectories = Directory.EnumerateDirectories(typeFolder)
                    .Where(directory => !directory.EndsWith("-sprint", StringComparison.OrdinalIgnoreCase));

                foreach (var versionDirectory in versionDirectories)
                {
                    var dir = versionDirectory.Replace(_webRootPath, string.Empty).Replace('\\', '/').TrimEnd('/') + "/";
                    var documentVersionModel = new DocumentVersionModel()
                    {
                        Path = versionDirectory,
                        ContentFilenames = Directory.EnumerateFiles(versionDirectory, "*.html", SearchOption.AllDirectories).Select(Path.GetFileName).ToList(),
                        Version = Path.GetFileName(versionDirectory),
                        StaticBase = $"{_cdnUrl}{dir}"
                    };

                    documentVersionModel.StartFilename = documentVersionModel
                        .ContentFilenames
                        .Select(filename => new { Filename = filename, Rank = IsDefaultDoc(filename) ? 1 : 0 })
                        .OrderByDescending(o => o.Rank)
                        .Select(s => s.Filename)
                        .FirstOrDefault();
                    documentVersionModel.TocFilename = documentVersionModel
                        .ContentFilenames.Where(file => string.CompareOrdinal(file, "toc.html") == 0)
                        .FirstOrDefault();

                    document.DocumentVersions.Add(documentVersionModel);
                }

                document.Versions = document.DocumentVersions
                    .Select(s => s.Version)
                    .OrderByDescending(o => o, new AlphanumComparator())
                    .ToList();

                docList.Add(document);
            }

            Documents.Clear();
            Documents.AddRange(docList);
        }

        public DocumentModel GetProduct(string product)
        {
            return Documents.FirstOrDefault(d => string.Equals(d.Product, product, System.StringComparison.OrdinalIgnoreCase));
        }

        public DocumentVersionModel GetVersion(DocumentModel document, string version)
        {
            DocumentVersionModel docVersion;

            if (string.IsNullOrWhiteSpace(version) || version == "latest")
            {
                var ver = document.Versions.First();
                docVersion = document.DocumentVersions.First(dv => dv.Version == ver);
            }
            else
            {
                docVersion = document.DocumentVersions.FirstOrDefault(v => v.Version == version);

                if (docVersion == null)
                {
                    var ver = document.Versions.First();
                    docVersion = document.DocumentVersions.First(dv => dv.Version == ver);
                }
            }

            return docVersion;
        }

        public string ValidateFilename(DocumentVersionModel documentVersion, string file)
        {
            var filename = documentVersion.StartFilename;
            if (!string.IsNullOrEmpty(file))
            {
                var selectedFilename = documentVersion.ContentFilenames
                    .FirstOrDefault(contentFilename => string.CompareOrdinal(file, Path.GetFileNameWithoutExtension(contentFilename)) == 0);
                if (!string.IsNullOrEmpty(selectedFilename))
                {
                    filename = selectedFilename;
                }
            }

            return filename;
        }

        public string GetContent(DocumentVersionModel documentVersion, string filename)
        {
            var content = string.Empty;
            var combinedPath = Path.Combine(documentVersion.Path, filename);

            if (File.Exists(combinedPath))
            {
                content = File.ReadAllText(combinedPath);
            }

            content = content.KeepAfter("<body>").KeepUntil("</body>");

            var imgRegex = new Regex("(?<=<img.*?src=[\"'])[^\"']*", RegexOptions.IgnoreCase);
            content = imgRegex.Replace(content, m => {
                if (m.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return m.Value;
                }

                return documentVersion.StaticBase + m.Value; 
            });

            content = content.Replace("<table>", "<table class=\"table table-bordered\">");
            content = content.Replace("{{version}}", documentVersion.Version.Substring(1)); // version without "v"
            content = content.Replace("{{docversion}}", documentVersion.Version);

            return content;
        }

        public DocumentRouteParams GetRouteParams(string product)
        {
            var document = GetProduct(product);
            return GetRouteParams(product, document.Versions.First());
        }

        public DocumentRouteParams GetRouteParams(string product, string version)
        {
            var document = GetProduct(product);
            var documentVersion = GetVersion(document, version);

            return new DocumentRouteParams { Product = document.Product, Version = documentVersion.Version, File = Path.GetFileNameWithoutExtension(documentVersion.StartFilename) };
        }
    }
}
