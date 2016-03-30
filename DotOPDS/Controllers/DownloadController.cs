﻿using DotOPDS.Models;
using DotOPDS.Utils;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;

namespace DotOPDS.Controllers
{
    public class DownloadController : ApiController
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        [Route("get/{id:guid}/{ext}")]
        [HttpGet]
        public HttpResponseMessage GetFile(Guid id, string ext)
        {
            var searcher = new LuceneSearcher();
            int total;
            var books = searcher.SearchExact(out total, "Guid", id.ToString(), take: 1);
            if (total < 1 || total > 1)
            {
                logger.Debug("File {0} not found", id);
                throw new KeyNotFoundException("Key Not Found: " + id);
            }

            logger.Debug("File {0} found in {1}ms", id, searcher.Time);

            var book = books[0];
            var converter = Settings.Instance.Converters
                .Where(x => x.From.Equals(book.Ext, StringComparison.InvariantCultureIgnoreCase) &&
                            x.To.Equals(ext, StringComparison.InvariantCultureIgnoreCase))
                .FirstOrDefault();
            if (!ext.Equals(book.Ext, StringComparison.InvariantCultureIgnoreCase) && converter == null)
            {
                logger.Warn("No converter found for '{0}'->'{1}'", book.Ext, ext);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var content = GetFileInFormat(book, converter);
            var result = new HttpResponseMessage(HttpStatusCode.OK);
            var mimeType = MimeHelper.GetMimeType(ext);
            result.Content = new StreamContent(content);
            result.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            result.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
            result.Content.Headers.ContentDisposition.FileName = UrlNameEncoder.GetSafeName(book, ext);

            logger.Info("Book {0} served to {1}", id, Request.GetOwinContext().Request.RemoteIpAddress);
            return result;
        }

        [Route("get/{*dummy}")]
        [HttpGet]
        public HttpResponseMessage CatchAll(string dummy)
        {
            return Request.CreateResponse(HttpStatusCode.NotFound);
        }

        private Stream GetFileInFormat(Book book, SettingsConverter converter)
        {
            var file = FileUtils.GetBookFile(book);

            // TODO: zip fb2 book?
            if (converter == null)
            {
                logger.Debug("File {0}.{1} served directly from archive.", book.File, book.Ext);
                return file;
            }
            else
            {
                logger.Debug("Trying to convert file {0} from {1} to {2}", book.File, book.Ext, converter.To);

                var tempDir = Path.Combine(Path.GetTempPath(), "DotOPDS_Temp");
                Directory.CreateDirectory(tempDir);
                var from = Path.Combine(tempDir, Path.GetRandomFileName() + book.File + "." + book.Ext);
                var to = Path.Combine(tempDir, Path.GetRandomFileName()) + "." + converter.To;

                using (var output = File.Create(from))
                {
                    file.CopyTo(output);
                }

                var cmd = string.Format(converter.Command, from, to);
                logger.Debug("Starting converter process: {0}", cmd);
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {

                        FileName = "cmd.exe",
                        Arguments = "/c " + cmd,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };
                process.Start();
                process.WaitForExit();
                logger.Debug("Converter process exited with code {0}", process.ExitCode);

                return File.OpenRead(to);
            }
        }
    }
}
