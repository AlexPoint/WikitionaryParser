﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace WikitionaryDumpParser.Src
{
    public class DumpDownloader
    {
        private const string RootUrl = "https://dumps.wikimedia.org";
        private static readonly string PathToDownloadDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Wikimedia\\Downloads\\";


        public string DownloadFile(string fileName)
        {
            // Full file names contains wikimedia/language/date infos
            // Ex: enwiki-latest-page.sql.gz 
            var parts = fileName.Split(new[] {'-'}, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
            {
                var languageCode = parts.First().Substring(0, 2);
                var wikimedia = parts.First().Substring(2);
                var dateVersion = parts[1];

                return DownloadFile(wikimedia, languageCode, fileName, dateVersion);
            }
            else
            {
                Console.WriteLine("Couldn't extract required info (for download) from file name '{0}'", fileName);
                return null;
            }
        }

        private string DownloadFile(string wikimedia, string languageCode, string fileName, string dateVersion)
        {
            // Retrieve url of page with all versions
            var versionsPageUrl = GetVersionsPageUrl(wikimedia, languageCode);
            // Get the url of the relevant page
            var relevantVersionPageUrl = string.Format("{0}/{1}", versionsPageUrl, dateVersion);
            
            // Create directory if it doesn't exist
            if (!Directory.Exists(PathToDownloadDirectory))
            {
                Directory.CreateDirectory(PathToDownloadDirectory);
            }

            // Check if the local copy of the file already exists
            var localFilePath = PathToDownloadDirectory + fileName;
            if (File.Exists(localFilePath))
            {
                var md5Checksum = GetMd5CheckSum(relevantVersionPageUrl, dateVersion, fileName);
                if (!string.IsNullOrEmpty(md5Checksum) && GetMd5CheckSum(localFilePath) == md5Checksum)
                {
                    // The file already exists and has the correct checsum -> we don't download it
                    return localFilePath;
                }
                else
                {
                    // Otherwise we delete the file (wrong checksum of we couldn't find the checksum)
                    File.Delete(localFilePath);
                }
            }

            // We download the file
            var fileUrl = string.Format("{0}/{1}", relevantVersionPageUrl, fileName);
            Console.WriteLine("Start download of file {0}", fileName);
            using (var client = new WebClient())
            {
                client.DownloadFile(fileUrl, localFilePath);
            }
            Console.WriteLine("End of download of file {0}", fileName);

            return localFilePath;
        }

        private string GetMd5CheckSum(string localFilePath)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(localFilePath))
                {
                    return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLower();
                }
            }
        }

        private string GetMd5CheckSum(string versionPageUrl, string dateExtension, string fileName)
        {
            // Check md5 sum
            var md5Url = string.Format("{0}/enwiktionary-{1}-md5sums.txt", versionPageUrl, dateExtension);
            using (var client = new WebClient())
            {
                var md5Checksums = client.DownloadString(md5Url);
                using (var reader = new StringReader(md5Checksums))
                {
                    var line = reader.ReadLine();
                    while (line != null)
                    {
                        var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && parts.Last() == fileName)
                        {
                            var md5CheckSum = parts.First();
                            return md5CheckSum;
                        }
                        line = reader.ReadLine();
                    }
                }
            }

            Console.WriteLine("Unable to find checksum for file {0} at date {1}", fileName, dateExtension);
            return "";
        }

        private string GetVersionsPageUrl(string wikimedia, string languageCode)
        {
            return string.Format("{0}/{1}{2}", RootUrl, languageCode, wikimedia);
        }
    }

}