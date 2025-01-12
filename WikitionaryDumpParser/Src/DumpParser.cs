﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WikitionaryDumpParser.Src
{
    /// <summary>
    /// Parser for wikimedia MySQL dump files
    /// </summary>
    public class DumpParser
    {

        /// <summary>
        /// Parses the language links in a MySQL dump file
        /// </summary>
        /// <param name="sqlDumpFilePath">The path of the dump file</param>
        /// <returns>The collection of language links in the dump file</returns>
        public List<LanguageLink> ParseLanguageLinks(string sqlDumpFilePath, string tgtLanguage)
        {
            string languageLinkPattern = @"\((\d+)\,\'(" + tgtLanguage + @")\'\,\'(.+)\'\)";
            Regex languageLinkRegex = new Regex(languageLinkPattern, RegexOptions.Compiled);

            // Split on ')' characters 
            const char splitCharacter = ')';

            var languageLinks = new List<LanguageLink>();

            // Open the file
            using (var fStream = File.OpenRead(sqlDumpFilePath))
            {
                // Decompress the stream
                using (var decompressedStream = new GZipStream(fStream, CompressionMode.Decompress))
                {
                    // Read the decompressed stream
                    using (var reader = new StreamReader(decompressedStream))
                    {
                        var line = new StringBuilder();
                        while (!reader.EndOfStream)
                        {
                            var nextChar = (char)reader.Read();
                            line.Append(nextChar);
                            if (nextChar == splitCharacter)
                            {
                                // Try to extract the page id and name
                                var languageLink = ExtractLanguageLink(line.ToString(), languageLinkRegex);
                                if (languageLink != null)
                                {
                                    languageLinks.Add(languageLink);
                                }

                                // Clear the string builder
                                line.Clear();
                            }
                        }
                    }
                }
            }

            return languageLinks;
        }


        /// <summary>
        /// Extracts a language links from a MySQL dump file line.
        /// Ex: (34778507,'es','Categoría:Futbolistas del Arlesey Town Football Club')
        /// </summary>
        private LanguageLink ExtractLanguageLink(string line, Regex regex)
        {
            var match = regex.Match(line);
            if (match.Success)
            {
                var pageId = int.Parse(match.Groups[1].Value);
                var lang = match.Groups[2].Value;
                var title = match.Groups[3].Value;
                return new LanguageLink
                {
                    Id = pageId,
                    LanguageCode = lang,
                    StoredTitle = title
                };
            }

            return null;
        }
    }
}
