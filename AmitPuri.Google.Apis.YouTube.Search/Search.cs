/*
 * Copyright 2015 Google Inc. All Rights Reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 *  Unless required by applicable law or agreed to in writing, software
 *  distributed under the License is distributed on an "AS IS" BASIS,
 *  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *  See the License for the specific language governing permissions and
 *  limitations under the License.
 *
 */

using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;

namespace AmitPuri.Google.Apis.YouTube
{
    internal class Search
    {
        [STAThread]
        static async Task Main(string[] args)
        {
            try
            {
                using IHost host = Host.CreateDefaultBuilder(args).Build();
                IConfiguration config = host.Services.GetRequiredService<IConfiguration>();
                string searchKeywords = config.GetValue<string>("searchKeywords");
                string apikey = config.GetValue<string>("apikey");
                string playlistId = config.GetValue<string>("playlistId");

                IAsyncEnumerable<string> videos = new Search().Run(searchKeywords, apikey);
                await foreach (var videoId in videos)
                {
                    await new Search().Addtoplaylist(playlistId, videoId);
                } 
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.InnerExceptions)
                {
                    Console.WriteLine("Error: " + e.Message);
                }
            }
        }

        private async IAsyncEnumerable<string> Run(string searchKeywords, string apikey, int maxResults = 50)
        {
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = apikey,
                ApplicationName = this.GetType().ToString()
            });

            var nextPageToken = "";
            int row = 1;

            List<string> videos = new List<string>();

            while (nextPageToken != null)
            {
                Console.WriteLine(string.Format("nextPageToken{0}=",nextPageToken));
                var searchListRequest = youtubeService.Search.List("snippet");
                searchListRequest.Q = searchKeywords;
                searchListRequest.MaxResults = maxResults;
                searchListRequest.PageToken = nextPageToken;
                SearchListResponse searchListResponse = null;
                try
                {
                  searchListResponse = await searchListRequest.ExecuteAsync();                    
                }
                catch (SystemException)
                {                  
                  // when 10000 queries/day limit exceeds, then sleep 
                  Thread.Sleep((1000*60*60*24));
                }
                if (searchListResponse != null)
                {
                foreach (var searchResult in searchListResponse.Items)
                  {
                      if (searchResult.Id.Kind == "youtube#video")
                      {
                          Console.WriteLine(String.Format("{0}-https://www.youtube.com/watch?v={1}", row, searchResult.Id.VideoId));
                          yield return searchResult.Id.VideoId;
                          row++;
                      }
                  }  
                  nextPageToken = searchListResponse.NextPageToken;
                }
                
            }
        }

        private async Task Addtoplaylist(string playlistId, string videoId)
        {
            UserCredential credential;
            using (var stream = new FileStream("client_secrets.json", FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                 new[] { YouTubeService.Scope.Youtube },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(this.GetType().ToString())
                );
            }
            var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = this.GetType().ToString()
            });

            var playlistItem = new PlaylistItem();
            playlistItem.Snippet = new PlaylistItemSnippet();
            playlistItem.Snippet.PlaylistId = playlistId;
            playlistItem.Snippet.ResourceId = new ResourceId();
            playlistItem.Snippet.ResourceId.Kind = "youtube#video";
            playlistItem.Snippet.ResourceId.VideoId = videoId;
            playlistItem = await youtubeService.PlaylistItems.Insert(playlistItem, "snippet").ExecuteAsync();

        }
    }
}
