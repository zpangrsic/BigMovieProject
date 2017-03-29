﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using System.Web.Http;
using api.Controllers;
using System.Resources;
using api.Resources.Global;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Net.Mime;

namespace api.Resources
{
    public class Streaming
    {

        public static async Task<HttpResponseMessage> Content(Movie_Data movie, RangeHeaderValue header)
        {
            // This can prevent some unnecessary accesses. 
            // These kind of file names won't be existing at all. 
            if (movie == null)
                throw new HttpResponseException(HttpStatusCode.NotFound);
            string path = "";
            foreach(var mDir in MovieGlobal.GlobalMovieDisksList)
            {
                if(Directory.Exists(mDir.value + @"\"+ movie.folder)) { path = mDir.value + @"\" + movie.folder; break; }
                if(path != "") { break;}
            }
            FileInfo fileInfo = new FileInfo(Path.Combine(path, movie.name + "." + movie.ext));

            if (!fileInfo.Exists)
                throw new HttpResponseException(HttpStatusCode.NotFound);

            long totalLength = fileInfo.Length;

            RangeHeaderValue rangeHeader = header;
            HttpResponseMessage response = new HttpResponseMessage();

            response.Headers.AcceptRanges.Add("bytes");

            // The request will be treated as normal request if there is no Range header.
            if (rangeHeader == null || !rangeHeader.Ranges.Any())
            {
                response.StatusCode = HttpStatusCode.OK;
                response.Content = new PushStreamContent(async (outputStream, httpContent, transpContext)
                =>
                {
                    using (outputStream) // Copy the file to output stream straightforward. 
                    using (Stream inputStream = fileInfo.OpenRead())
                    {
                        try
                        {
                            await inputStream.CopyToAsync(outputStream, ReadStreamBufferSize);
                            //inputStream.CopyTo(outputStream, MediaLibrary.ReadStreamBufferSize);
                        }
                        catch (Exception e)
                        {
                            await History.Create("api", new History_API()
                            {
                                api_action = "Exception caught | Message " + e.Message,
                                api_type = "Exception -> MoviesAPI.Get.MovieInfo()",
                                api_datetime = DateTime.Now
                            });
                        }
                    }
                }, GetMimeNameFromExt(fileInfo.Extension));

                response.Content.Headers.ContentLength = totalLength;
                await History.Create("api", new History_API()
                {
                    api_action = "Movie "+ movie.Movie_Info.title + " served successfully.",
                    api_type = "Task -> Streaming movie ",
                    api_datetime = DateTime.Now
                });
                return response;
            }

            long start = 0, end = 0;

            // 1. If the unit is not 'bytes'.
            // 2. If there are multiple ranges in header value.
            // 3. If start or end position is greater than file length.
            if (rangeHeader.Unit != "bytes" || rangeHeader.Ranges.Count > 1 ||
                !TryReadRangeItem(rangeHeader.Ranges.First(), totalLength, out start, out end))
            {

                response.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable;
                response.Content = new StreamContent(Stream.Null);  // No content for this status.
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(totalLength);
                response.Content.Headers.ContentType = GetMimeNameFromExt(fileInfo.Extension);
                await History.Create("api", new History_API()
                {
                    api_action = "Movie " + movie.Movie_Info.title + " served successfully.",
                    api_type = "Task -> Streaming movie ",
                    api_datetime = DateTime.Now
                });
                return response;
            }

            var contentRange = new ContentRangeHeaderValue(start, end, totalLength);

            // We are now ready to produce partial content.
            response.StatusCode = HttpStatusCode.PartialContent;
            response.Content = new PushStreamContent(async (outputStream, httpContent, transpContext)
            =>
            {
                using (outputStream) // Copy the file to output stream in indicated range.
                using (Stream inputStream = fileInfo.OpenRead())
                    await CreatePartialContent(inputStream, outputStream, start, end);

            }, GetMimeNameFromExt(fileInfo.Extension));

            response.Content.Headers.ContentLength = end - start + 1;
            response.Content.Headers.ContentRange = contentRange;
            await History.Create("api", new History_API()
            {
                api_action = "Movie " + movie.Movie_Info.title + " served successfully.",
                api_type = "Task -> Streaming movie ",
                api_datetime = DateTime.Now
            });
            return response;
        }

        public const int ReadStreamBufferSize = 1024 * 1024;
        public static readonly IReadOnlyDictionary<string, string> MimeNames;
        // We will discuss this later.
        public static readonly IReadOnlyCollection<char> InvalidFileNameChars;
        // Where are your videos located at? Change the value to any folder you want.
        public static readonly string InitialDirectory;

        static Streaming()
        {
            var mimeNames = new Dictionary<string, string>();

            mimeNames.Add(".mp3", "audio/mpeg");    // List all supported media types; 
            mimeNames.Add(".mp4", "video/mp4");
            mimeNames.Add(".ogg", "application/ogg");
            mimeNames.Add(".ogv", "video/ogg");
            mimeNames.Add(".oga", "audio/ogg");
            mimeNames.Add(".wav", "audio/x-wav");
            mimeNames.Add(".webm", "video/webm");

            MimeNames = new ReadOnlyDictionary<string, string>(mimeNames);

            InvalidFileNameChars = Array.AsReadOnly(Path.GetInvalidFileNameChars());
            //InitialDirectory = WebConfigurationManager.AppSettings["InitialDirectory"];
        }

        public static MediaTypeHeaderValue GetMimeNameFromExt(string ext)
        {
            string value = null;

            if (MimeNames.TryGetValue(ext.ToLowerInvariant(), out value))
                return new MediaTypeHeaderValue(value);
            else
                return new MediaTypeHeaderValue(MediaTypeNames.Application.Octet);
        }
        private static bool AnyInvalidFileNameChars(string fileName)
        {
            return InvalidFileNameChars.Intersect(fileName).Any();
        }

        public static bool TryReadRangeItem(RangeItemHeaderValue range, long contentLength,
            out long start, out long end)
        {
            if (range.From != null)
            {
                start = range.From.Value;
                if (range.To != null)
                    end = range.To.Value;
                else
                    end = contentLength - 1;
            }
            else
            {
                end = contentLength - 1;
                if (range.To != null)
                    start = contentLength - range.To.Value;
                else
                    start = 0;
            }
            return (start < contentLength && end < contentLength);
        }

        public static async Task CreatePartialContent(Stream inputStream, Stream outputStream,
            long start, long end)
        {
            int count = 0;
            long remainingBytes = end - start + 1;
            long position = start;
            byte[] buffer = new byte[ReadStreamBufferSize];

            inputStream.Position = start;
            do
            {
                try
                {
                    if (remainingBytes > ReadStreamBufferSize)
                        count = await inputStream.ReadAsync(buffer, 0, ReadStreamBufferSize);
                    else
                        count = await inputStream.ReadAsync(buffer, 0, (int)remainingBytes);
                    await outputStream.WriteAsync(buffer, 0, count);
                }
                catch (Exception ex)
                {
                    await History.Create("api", new History_API()
                    {
                        api_action = "Exception caught | Message " + ex.Message,
                        api_type = "Exception -> MediaLibrary.CreatePartialContent()",
                        api_datetime = DateTime.Now
                    });
                    break;
                }
                position = inputStream.Position;
                remainingBytes = end - position + 1;
            } while (position <= end);
        }

    }
}