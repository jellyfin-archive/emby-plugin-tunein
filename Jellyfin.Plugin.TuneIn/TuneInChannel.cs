﻿using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Schema;
using HtmlAgilityPack;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.TuneIn
{
    public class TuneInChannel : IChannel, IRequiresMediaInfoCallback, IHasCacheKey
    {
        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IApplicationHost _appHost;

        // private String partnerid { get; set; }

        public TuneInChannel(IHttpClient httpClient, ILoggerFactory loggerFactory, IApplicationHost appHost)
        {
            _httpClient = httpClient;
            _appHost = appHost;
            _logger = loggerFactory.CreateLogger(GetType().Name);

            // partnerid = "uD1X52pA";
        }

        public string DataVersion
        {
            get
            {
                // Increment as needed to invalidate all caches
                return "46";
            }
        }

        public string Description
        {
            get { return "Listen to online radio, find streaming music radio and streaming talk radio with TuneIn."; }
        }

        public bool IsEnabledFor(string userId)
        {
            return true;
        }

        public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var items = new List<ChannelItemInfo>();

            _logger.LogDebug("Category ID " + query.FolderId);

            try {
                if (string.IsNullOrWhiteSpace(query.FolderId))
                {
                    items = await GetMenu("", query, cancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(Plugin.Instance.Configuration.Username))
                    {
                        items.Add(new ChannelItemInfo
                        {
                            Name = "My Favorites",
                            Id = "preset_",
                            Type = ChannelItemType.Folder,
                            ImageUrl = GetDefaultImages("My Favorites")
                        });
                    }
                }
                else
                {
                    var channelID = query.FolderId.Split('_');


                    if (channelID[0] == "preset")
                    {
                        items = await GetPresets(query, cancellationToken);
                    }
                    else
                    {
                        query.FolderId = channelID[1].Replace("&amp;", "&");

                        if (channelID.Count() > 2)
                        {
                            items = await GetMenu(channelID[2], query, cancellationToken).ConfigureAwait(false);
                        }
                        else
                            items = await GetMenu("", query, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Could not load channel items for TuneIn due to fatal error!");
                _logger.LogError(ex.ToString());
                items.Add(new ChannelItemInfo
                {
                    Name = "Fatal Error!",
                    Id = "",
                    Type = ChannelItemType.Folder,
                    ImageUrl = GetDefaultImages("Fatal Error!")
                });
            }

            return new ChannelItemResult()
            {
                Items = items
            };
        }

        private async Task<List<ChannelItemInfo>> GetPresets(InternalChannelItemQuery query,
            CancellationToken cancellationToken)
        {
            var page = new HtmlDocument();
            var items = new List<ChannelItemInfo>();
            // "&partnerid=" + partnerid + ""
            var url = "https://opml.radiotime.com/Browse.ashx?c=presets&formats=mp3,aac&serial=" + _appHost.SystemId;

            if (Plugin.Instance.Configuration.Username != null)
            {
                url = url + "&username=" + Plugin.Instance.Configuration.Username;
            }

            using (var response = await _httpClient.SendAsync(new HttpRequestOptions
            {
                Url = url,
                CancellationToken = CancellationToken.None

            }, "GET").ConfigureAwait(false))
            {
                using (var site = response.Content)
                {
                    page.Load(site, Encoding.UTF8);
                    if (page.DocumentNode != null)
                    {
                        var body = page.DocumentNode.SelectSingleNode("//body");

                        if (body.SelectNodes("//outline[@url and @type=\"audio\"]") != null)
                        {
                            foreach (var node in body.SelectNodes("//outline[@url and @type=\"audio\"]"))
                            {
                                items.Add(new ChannelItemInfo
                                {
                                    Name = node.Attributes["text"].Value,
                                    Id = "stream_" + node.Attributes["url"].Value,
                                    Type = ChannelItemType.Media,
                                    ContentType = ChannelMediaContentType.Podcast,
                                    ImageUrl = node.Attributes["image"] != null ? node.Attributes["image"].Value : null,
                                    MediaType = ChannelMediaType.Audio
                                });
                            }
                        }
                        if (body.SelectNodes("//outline[@key=\"shows\"]") != null)
                        {
                            foreach (var node in body.SelectNodes("//outline[@key=\"shows\"]/outline[@url]"))
                            {
                                items.Add(new ChannelItemInfo
                                {
                                    Name = node.Attributes["text"].Value,
                                    Id = "subcat_" + node.Attributes["url"].Value,
                                    Type = ChannelItemType.Folder,
                                    ImageUrl = node.Attributes["image"] != null ? node.Attributes["image"].Value : null
                                });
                            }
                        }
                    }
                }
            }

            return items.ToList();
        }

        private
            async Task<List<ChannelItemInfo>> GetMenu(String title, InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var page = new HtmlDocument();
            var items = new List<ChannelItemInfo>();
            // "&partnerid=" + partnerid + ""
            var url = "https://opml.radiotime.com/Browse.ashx?formats=mp3,aac&serial=" + _appHost.SystemId;

            if (Plugin.Instance.Configuration.LatLon != null)
            {
                url = url + "&latlon=" + Plugin.Instance.Configuration.LatLon;
            }

            if (query.FolderId != null) url = query.FolderId.Replace("&amp;", "&");

            using (var response = await _httpClient.SendAsync(new HttpRequestOptions
            {
                Url = url,
                CancellationToken = CancellationToken.None

            }, "GET").ConfigureAwait(false))
            {
                using (var site = response.Content)
                {
                    page.Load(site, Encoding.UTF8);
                    if (page.DocumentNode != null)
                    {
                        var rootNode = page.DocumentNode.SelectSingleNode("//body");

                        var subNode = !String.IsNullOrEmpty(title) ? rootNode.SelectSingleNode("./outline[@text=\"" + title + "\"]") : null;
                        if (subNode != null) rootNode = subNode;

                        // Special case to expand a lone Stations subcategory
                        var outlines = rootNode.SelectNodes("./outline");
                        if (outlines.Count == 1 && outlines.First().GetAttributeValue("text", "none") == "Stations")
                            rootNode = outlines.First();

                        List<HtmlNode> files = new List<HtmlNode>();
                        List<HtmlNode> folders = new List<HtmlNode>();

                        var audio = rootNode.SelectNodes("./outline[@type=\"audio\" and @url]");
                        if (audio != null)
                        {
                            _logger.LogDebug("TuneIn found audio items...");
                            files.AddRange(audio);
                        }

                        var links = rootNode.SelectNodes("./outline[@type=\"link\" and @url]");
                        if (links != null)
                        {
                            _logger.LogDebug("TuneIn found links...");
                            folders.AddRange(links);
                        }

                        var subcategories = rootNode.SelectNodes("./outline[@text and not(@url) and not(@key=\"related\")]");
                        if (subcategories != null)
                        {
                            _logger.LogDebug("TuneIn found sub-categories...");
                            foreach (var node in subcategories)
                            {
                                if (node.Attributes["text"].Value == "No stations or shows available")
                                    throw new Exception("No stations or shows available");

                                items.Add(new ChannelItemInfo
                                {
                                    Name = node.Attributes["text"].Value,
                                    Id = "subcat_" + query.FolderId + "_" + node.Attributes["text"].Value,
                                    Type = ChannelItemType.Folder,
                                });
                            }
                        }

                        if (files != null)
                        {
                            foreach (var node in files)
                            {
                                items.Add(new ChannelItemInfo
                                {
                                    Name = node.Attributes["text"].Value,
                                    Id = "stream_" + node.Attributes["url"].Value,
                                    Type = ChannelItemType.Media,
                                    ContentType = ChannelMediaContentType.Podcast,
                                    MediaType = ChannelMediaType.Audio,
                                    ImageUrl = node.Attributes["image"] != null ? node.Attributes["image"].Value : GetDefaultImages(node.Attributes["text"].Value)
                                });
                            }
                        }
                        
                        if (folders != null)
                        {
                            foreach (var node in folders)
                            {
                                items.Add(new ChannelItemInfo
                                {
                                    Name = node.Attributes["text"].Value,
                                    Id = "category_" + node.Attributes["url"].Value,
                                    Type = ChannelItemType.Folder,
                                    ImageUrl = node.Attributes["image"] != null ? node.Attributes["image"].Value : GetDefaultImages(node.Attributes["text"].Value)
                                });
                            }
                        }
                        
                    }
                }
            }

            return items.ToList();
        }


        public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id,
            CancellationToken cancellationToken)
        {
            var channelID = id.Split('_');
            var items = new List<MediaSourceInfo>();

            using (var outerResponse = await _httpClient.SendAsync(new HttpRequestOptions
            {
                Url = channelID[1].Replace("&amp;", "&"),
                CancellationToken = CancellationToken.None

            }, "GET").ConfigureAwait(false))
            {
                using (var site = outerResponse.Content)
                {
                    using (var reader = new StreamReader(site))
                    {
                        while (!reader.EndOfStream)
                        {
                            var url = reader.ReadLine();
                            _logger.LogDebug("FILE NAME : " + url.Split('/').Last().Split('?').First());

                            var ext = Path.GetExtension(url.Split('/').Last().Split('?').First());
                            
                            _logger.LogDebug("URL : " + url);
                            if (!string.IsNullOrEmpty(ext))
                            {
                                _logger.LogDebug("Extension : " + ext);
                                if (ext == ".pls")
                                {
                                    _logger.LogDebug("TuneIn MediaInfo request: .pls file: " + url);
                                    try
                                    {
                                        using (var response = await _httpClient.SendAsync(new HttpRequestOptions
                                        {
                                            Url = url,
                                            CancellationToken = CancellationToken.None

                                        }, "GET").ConfigureAwait(false))
                                        {
                                            using (var value = response.Content)
                                            {
                                                var parser = new IniParser(value);
                                                var count = Convert.ToInt16(parser.GetSetting("playlist", "NumberOfEntries"));
                                                var end = count+1;
                                                for (var i = 1; i < end; i++)
                                                {
                                                    var file = parser.GetSetting("playlist", "File" + i);

                                                    if (!String.IsNullOrWhiteSpace(file))
                                                        items.Add(GetMediaInfoFromUrl(file));
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex.ToString());
                                    }
                                }
                                else if (ext == ".m3u" || ext == ".m3u8")
                                {
                                    _logger.LogDebug("TuneIn MediaInfo request: .m3u file: " + url);
                                    try
                                    {
                                        using (var response = await _httpClient.SendAsync(new HttpRequestOptions
                                        {
                                            Url = url,
                                            CancellationToken = CancellationToken.None

                                        }, "GET").ConfigureAwait(false))
                                        {
                                            using (var value = response.Content)
                                            {
                                                using (var reader2 = new StreamReader(value))
                                                {
                                                    while (!reader2.EndOfStream)
                                                    {
                                                        var url2 = reader2.ReadLine();
                                                        if (!String.IsNullOrWhiteSpace(url2) && !url2.StartsWith("#"))
                                                            items.Add(GetMediaInfoFromUrl(url2));
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex.ToString());
                                    }
                                }
                                else
                                {
                                    _logger.LogDebug("TuneIn MediaInfo request: Non-playlist file (" + ext + "): " + url);
                                    items.Add(GetMediaInfoFromUrl(url));
                                }
                            }
                            else
                            {
                                _logger.LogDebug("TuneIn MediaInfo request: No file extension: " + url);

                                items.Add(GetMediaInfoFromUrl(url));
                            }
                        }
                    }
                }
            }

            return items;
        }

        private MediaSourceInfo GetMediaInfoFromUrl(string url)
        {
            var urlLower = url.ToLowerInvariant();
            var ext = Path.GetExtension(urlLower.Split('/').Last().Split('?').First());

            var container = "";

            if (ext != null)
            {
                if (ext == ".aac") container = "aac";
                else if (ext == ".mp3") container = "mp3";
                else if (ext == ".pls" || ext == ".m3u" || ext == ".m3u8")
                    container = "playlist";
            }
            else
            {
                var pathElements = urlLower.Split('/');
                var pathEnd = pathElements.Last().Split('?').First();
                if (String.IsNullOrWhiteSpace(pathEnd)) pathEnd = pathElements[pathElements.Count()-2];

                if (pathEnd.Contains("aac")) container = "aac";
                else if (pathEnd.Contains("mp3")) container = "mp3";

                if (String.IsNullOrWhiteSpace(container))
                {
                    if (urlLower.Contains("aac")) container = "aac";
                    else if (urlLower.Contains("mp3")) container = "mp3";
                }
            }

            if (String.IsNullOrWhiteSpace(container))
                container = "aac";
            
            if (container == "playlist")
            {
                return new ChannelMediaInfo
                {
                    Path = url
                }.ToMediaSource();
            }
            else
            {
                return new ChannelMediaInfo
                {
                    Path = url,
                    Container = container,
                    AudioCodec = container,
                    AudioBitrate = 128000,
                    AudioChannels = 2,
                    SupportsDirectPlay = true
                }.ToMediaSource();
            }
        }

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            switch (type)
            {
                case ImageType.Thumb:
                case ImageType.Backdrop:
                case ImageType.Primary:
                    {
                        var path = GetType().Namespace + ".Images." + type.ToString().ToLower() + ".png";

                        return Task.FromResult(new DynamicImageResponse
                        {
                            Format = ImageFormat.Png,
                            HasImage = true,
                            Stream = GetType().Assembly.GetManifestResourceStream(path)
                        });
                    }
                default:
                    throw new ArgumentException("Unsupported image type: " + type);
            }
        }

        public IEnumerable<ImageType> GetSupportedChannelImages()
        {
            return new List<ImageType>
            {
                ImageType.Thumb,
                ImageType.Backdrop,
                ImageType.Primary
            };
        }

        public string Name
        {
            get { return "TuneIn"; }
        }

        public InternalChannelFeatures GetChannelFeatures()
        {
            return new InternalChannelFeatures
            {
                ContentTypes = new List<ChannelMediaContentType>
                {
                    ChannelMediaContentType.Song
                },
                MediaTypes = new List<ChannelMediaType>
                {
                    ChannelMediaType.Audio
                }
            };
        }

        public string HomePageUrl
        {
            get { return "https://www.tunein.com/"; }
        }

        public ChannelParentalRating ParentalRating
        {
            get { return ChannelParentalRating.GeneralAudience; }
        }

        public string GetCacheKey(string userId)
        {
            return Plugin.Instance.Configuration.LatLon + "-" + Plugin.Instance.Configuration.Username;
        }

        public String GetDefaultImages(String name)
        {
            if (name == "Local Radio")
                return GetType().Namespace + ".Images.tunein-localradio.png";
            if (name == "By Language")
                return GetType().Namespace + ".Images.tunein-bylanguage.png";
            if (name == "By Location")
                return GetType().Namespace + ".Images.tunein-bylocation.png";
            if (name == "Music")
                return GetType().Namespace + ".Images.tunein-music.png";
            if (name == "My Favorites")
                return GetType().Namespace + ".Images.tunein-myfavs.png";
            if (name == "Podcasts")
                return GetType().Namespace + ".Images.tunein-podcasts.png";
            if (name == "Sports")
                return GetType().Namespace + ".Images.tunein-sports.png";
            if (name == "Talk")
                return GetType().Namespace + ".Images.tunein-talk.png";

            return "";
        }

    }
}
