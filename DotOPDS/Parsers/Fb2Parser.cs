﻿using System;
using System.Linq;
using DotOPDS.Models;
using System.IO;
using System.Xml.Linq;
using DotOPDS.Utils;
using NLog;

namespace DotOPDS.Parsers
{
    class Fb2Parser : IBookParser
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private void UpdateAnnotation(Book book, XDocument doc)
        {
            var annotation = doc.Descendants()
                                .Where(x => x.Name.LocalName == "annotation")
                                .FirstOrDefault();
            if (annotation != null)
            {
                book.Annotation = Util.GetInnerXml(annotation);
            }
        }

        private void UpdateCover(Book book, XDocument doc)
        {
            var coverPage = doc.Descendants()
                               .Where(x => x.Name.LocalName == "coverpage")
                               .Descendants()
                               .Where(x => x.Name.LocalName == "image")
                               .FirstOrDefault();

            if (coverPage != null)
            {
                var coverId = coverPage.Attributes()
                                       .Where(x => x.Name.LocalName == "href")
                                       .First()
                                       .Value.Substring(1);
                var cover = doc.Descendants()
                               .Where(x => x.Name.LocalName == "binary" && x.Attribute("id").Value == coverId)
                               .First();
                var ctype = cover.Attribute("content-type").Value;
                var bin = Convert.FromBase64String(cover.Value);
                book.Cover = new Cover
                {
                    Data = bin,
                    ContentType = ctype,
                    Has = true
                };
            }
        }

        public void Update(Book book)
        {
            using (var stream = FileUtils.GetBookFile(book))
            {
                var mem = new MemoryStream();
                stream.CopyTo(mem);
                var encoding = Util.DetectXmlEncoding(mem);
                logger.Trace("Book encoding detected, id:{0}, enc:{1}", book.Id, encoding);

                using (var reader = new StreamReader(mem, encoding))
                {
                    using (var sgmlReader = new Sgml.SgmlReader())
                    {
                        sgmlReader.InputStream = reader;
                        var doc = XDocument.Load(sgmlReader);
                        logger.Trace("Book file loaded, id:{0}", book.Id);

                        try
                        {
                            UpdateAnnotation(book, doc);
                        }
                        catch (Exception)
                        {
                        }

                        try
                        {
                            UpdateCover(book, doc);
                        }
                        catch (Exception)
                        {
                            book.Cover = new Cover { Has = false };
                        }
                    }
                }
            }
        }
    }
}