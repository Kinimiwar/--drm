using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using static __drm.Common;

namespace __drm.Modules {
    public class disneynow {
        private static string BrandID = "009";
        private static string DeviceID = "023";
        private class Title {
            public string ID { get; set; }
            public int AccessLevel { get; set; }
            public string URL { get; set; }
            public bool IsMovie { get; set; }
            public string Name { get; set; }
            public string Name_Serial => Regex.Replace(Name, "[/\\*!?,.\'\"()<>:|]", string.Empty).Replace(" ", ".");
            public int Year { get; set; }// Movie only?
            public int TV_Season { get; set; }
            public string TV_Season_Serial => TV_Season.ToString().PadLeft(2, '0');
            public int TV_Episode { get; set; }
            public string TV_Episode_Serial => TV_Episode.ToString().PadLeft(2, '0');
            public string TV_EpisodeTitle { get; set; }
            public string TV_EpisodeTitle_Serial => Regex.Replace(TV_EpisodeTitle, "[/\\*!?,.\'\"()<>:|]", string.Empty).Replace(" ", ".");
        }
        public override string ToString() {
            Logger.Debug(
                "CLI Switches:\n" +
                "  " + string.Join("\n  ", new[] {
                    "URL can be the TV Show Page, Season Page or Episode Page.",
                    "Providing a TV Show Page (with --season) or a Season Page URL will download all episodes of that season.",
                    "--quality ('best'[str] or height[int])",
                    "--season ([int], only used if you provide the TV Show Page e.x. disneynow.go.com/shows/phineas-and-ferb)",
                    "--episode ([int], when used on a TV Show or Season page, it will skip to the set episode and download from there on)",
                    "--ap-username ([str], required for most episodes, and only required to be used if theres no cached ap data)",
                    "--ap-password ([str], required for most episodes, and only required to be used if theres no cached ap data)",
                    "--ap-msoid ([str], required for most episodes, required even if there is cached data)"
                })
            );
            return null;
        }
        public void Start() {
            SetTitle(Module.DisneyNow);
            #region Get a List of all Requested Episodes' ID's
            HashSet<Title> Episodes = new HashSet<Title>();
            // Get Page Content
            string PageContent = Fetch(Arguments.URL);
            if (PageContent == string.Empty) {
                return;
            }
            // TV Show Page (All Seasons Page)
            if (Regex.IsMatch(PageContent, "<title>Watch [^<]+ TV Show")) {
                foreach (string SeasonURL in Regex.Matches(PageContent, "data-t-item='.*?href = \"([^\"]*)").Cast<Match>().Select(x => x.Groups[1].Value)) {
                    if (Arguments.Season == -1 || int.Parse(SeasonURL.Substring(SeasonURL.LastIndexOf('-') + 1)) == Arguments.Season) {
                        foreach (Match episode in Regex.Matches(Fetch("https://disneynow.go.com" + SeasonURL).ToString(), "data-video-id=\"([^\"]+)\"")) {
                            string VideoID = episode.Groups[1].Value;
                            Console.WriteLine("Adding episode to queue... " + VideoID);
                            //Episodes.Add(VideoID);
                        }
                    }
                }
            } else {
                // Season Page
                if (Regex.IsMatch(PageContent, "<title>[^<]+ Full Episodes | Watch Season \\d+ Online")) {
                    string mID = Regex.Match(PageContent, "data-m-id=\"([^\"]+)\"data").Groups[1].Value;
                    int TotalTiles = int.Parse(Regex.Match(PageContent, "data-total-tiles=\"([^\"]+)").Groups[1].Value);
                    string ShowID = Regex.Match(PageContent, "data-show-id=\"([^\"]+)").Groups[1].Value;
                    string SeasonNumber = Regex.Match(PageContent, "data-season-number=\"([^\"]+)").Groups[1].Value;
                    do {
                        JToken module = JToken.Parse(Fetch("https://api.presentation.watchabc.go.com/api/ws/presentation/v2/module/" + mID + ".json?brand=011&device=001&authlevel=1&start=" + Episodes.Count + "&size=" + (TotalTiles - Episodes.Count) + "&group=allages&preauthchannels=004%2C008%2C009&show=" + ShowID + "&season=" + SeasonNumber));
                        foreach (JToken Tile in module["tilegroup"]["tiles"]["tile"]) {
                            Episodes.Add(new Title {
                                ID = Tile["id"].ToString().Replace("video.", string.Empty),
                                AccessLevel = int.Parse(Tile["video"]["accesslevel"].ToString()),
                                URL = Tile["link"]["value"].ToString(),
                                IsMovie = Tile["show"]["type"].ToString() != "show",
                                Name = Tile["video"][Tile["show"]["type"].ToString() + "title"].ToString(),
                                TV_Season = int.Parse(Tile["video"]["seasonnumber"].ToString()),
                                TV_Episode = int.Parse(Tile["video"]["episodenumber"].ToString()),
                                TV_EpisodeTitle = Tile["video"]["title"].ToString()
                            });
                        }
                    } while (Episodes.Count < TotalTiles);
                    Episodes = new HashSet<Title>(Episodes.Reverse());//Reverse so it starts downloading episode 1 rather than newest episode as the UI has it ordered reversed
                    if(Arguments.Episode != -1) {
                        Episodes = new HashSet<Title>(Episodes.Skip(Arguments.Episode - 1));
                    }
                } else {
                    // Episode Page
                    if (Regex.IsMatch(PageContent, "<title>Watch [^<]+ Season \\d+ Episode \\d+")) {
                        //Episodes.Add(Regex.Match(PageContent, "data-video-id=\"([^\"]+)\"").Groups[1].Value);
                        BrandID = Regex.Match(PageContent, "data-page-brand=\"(\\d+)\"").Groups[1].Value;
                    }
                }
            }
            #endregion
            DownloadEpisodes(Episodes.ToArray());
        }
        private static bool DownloadEpisodes(Title[] Episodes) {
            int CurrEpisode = 0;
            int TotalEpisodes = Episodes.Length;
            foreach (Title Episode in Episodes) {
                CurrEpisode++;
                Logger.Info("Downloading Episode #" + CurrEpisode + "/" + TotalEpisodes + " - " + Episode.ID);
                JObject VideoData = (JObject)Fetch(
                    string.Join("/", new string[] {
                        "http://api.contents.watchabc.go.com/vp2/ws/s/contents/3000/videos",
                        BrandID,//brand_id
                        "001",
                        "-1",
                        "-1",//show_id?
                        "-1",
                        Episode.ID,//video_id
                        "-1",
                        "-1.json"
                    }),
                    string.Empty,
                    "json"
                );

                string M3U8Url = null;
                // No DRM, No Adobe Auth, 100% open
                if (Episode.AccessLevel == 0) {
                    Logger.Info("S" + Episode.TV_Season_Serial + "E" + Episode.TV_Episode_Serial + " is FREE! Getting it directly via authorize.json");
                    do {
                        try {
                            JToken AuthorizeRes = JObject.Parse(new WebClient {
                                Encoding = Encoding.UTF8,
                                Headers = {{
                                    HttpRequestHeader.ContentType,
                                    "application/x-www-form-urlencoded"
                                }, {
                                    HttpRequestHeader.Referer,
                                    "http://cdn1.edgedatg.com/aws/apps/datg/web-player-unity/1.0.6.13/swf/player_vod.swf"
                                }}
                            }.UploadString(
                                "https://api.entitlement.watchabc.go.com/vp2/ws-secure/entitlement/2020/authorize.json",
                                string.Join("&", new[] {
                                    "device=" + DeviceID,
                                    "video%5Ftype=lf",//?
                                    "video%5Fid=" + Episode.ID.Replace("_", "%5F"),
                                    "brand=" + BrandID
                                })
                            ));
                            bool error = AuthorizeRes.SelectToken("$.errors.count") != null;
                            M3U8Url = error ? null : VideoData.SelectToken("$.video[0].assets.asset[?(@.storagetype=='uplynk')].value").ToString() + "?" + AuthorizeRes.SelectToken("$.uplynkData.sessionKey").ToString() + "&ad.cping=1";
                            if(error) {
                                Logger.Error("authorize.json call failed :( Attempting with Web Player Device ID Instead...");
                                DeviceID = "001";
                            }
                        } catch {
                            Logger.Error("authorize.json call failed :( Aborting...");
                            return false;
                        }
                    } while (M3U8Url == null);
                } else {
                    Logger.Info("S" + Episode.TV_Season_Serial + "E" + Episode.TV_Episode_Serial + " is locked behind Adobe MSO/SP Auth, Getting it via Adobe shortAuthorize and authorize.json");
                    AdobeAuthSP aasp = new AdobeAuthSP {
                        REQUESTERID = "DisneyChannels",
                        REQUESTINGPAGE = "https://disneynow.go.com" + Episode.URL,
                        RESOURCEID = string.Concat(new[] {
                            "<rss version=\"2.0\" xmlns:media=\"http://search.yahoo.com/mrss/\"><channel><title>ABC</title><item><title>",
                            Episode.Name,
                            "</title><guid>",
                            Episode.ID,
                            "</guid><media:rating scheme=\"urn:v-chip\" /></item></channel></rss>"
                        })
                    };
                    M3U8Url = Regex.Match(
                        new WebClient {
                            Encoding = Encoding.UTF8,
                            Headers = {{
                                HttpRequestHeader.ContentType, "application/x-www-form-urlencoded"
                            }}
                        }.UploadString(
                            "https://api.entitlement.watchabc.go.com/vp2/ws-secure/entitlement/2020/playmanifest_secure.json",
                            string.Join("&", new[] {
                                "video_type=lf",//?
                                "video_id=" + Episode.ID,
                                "adobe_requestor_id=DisneyXD",//Should this be "DisneyChannels" like with aasp?
                                "brand=" + BrandID,
                                "token_type=ap",//ap = adobepass
                                "device=" + DeviceID,
                                "token=" + WebUtility.UrlEncode(aasp.ShortAuthorize())
                            })
                        ),
                        "\"([^\"]+m3u8[^\"]+)"
                    ).Groups[1].Value;
                }
                // Subtitles
                string ttml = (string)VideoData.SelectToken("$.video[0].closedcaption.src[?(@.type=='ttml')].value");
                if (ttml != string.Empty) {
                    DownloadSRT(ttml);
                }
                // Chapters
                if (VideoData.SelectTokens("$.video[0].cues.cue[*].value").Count() > 0) {
                    Logger.Debug("Generating Chapters (based on json cues)");
                    File.WriteAllText(
                        "temp/.chapters",
                        string.Join("\n",
                            VideoData.SelectTokens("$.video[0].cues.cue[*].value").Select((x, i) => {
                                int num = i + 1;
                                string numPad = num.ToString().PadLeft(2, '0');
                                return string.Join("\n", new[] {
                                    "CHAPTER" + numPad + "=" + TimeSpan.FromMilliseconds((double)x).ToString("hh\\:mm\\:ss\\.fff"),
                                    "CHAPTER" + numPad + "NAME=Chapter " + num
                                });
                            })
                        )
                    );
                }
                // Download M3U8 File Contents and Get Resolutions
                string m3u8res = (string)Fetch(M3U8Url);
                IOrderedEnumerable<Match> resolutions = Regex.Matches(m3u8res, "#EXT-X-STREAM.*?RESOLUTION=[^x]*x([^,]*),BANDWIDTH=([^,]*).*\\s(.*)").Cast<Match>().OrderByDescending(f => int.Parse(f.Groups[2].Value)).OrderByDescending(f => int.Parse(f.Groups[1].Value));
                string Choice = string.Empty;
                bool CLIChoiceInvalid = Arguments.Quality != null && Arguments.Quality != "best" && !resolutions.Cast<Match>().Any(x => x.Groups[1].Value == Arguments.Quality);
                if (Arguments.Quality == null || CLIChoiceInvalid) {
                    if (CLIChoiceInvalid) {
                        Logger.Error("--quality value \"" + Arguments.Quality + "\" is not valid for " + Episode.URL + "\n Please choose a new one below.");
                    }
                    VideoTracks(resolutions.Cast<Match>().Select((res, i) => res.Groups.Cast<Group>().Skip(1).Take(2).Select(x => x.Value).ToArray()).ToArray());
                    Choice = AskInput("Which resolution do you wish to download? (use # or 'best')").Trim('#');
                } else {
                    Choice = Arguments.Quality;
                }
                Match selected = Choice == "best" ? resolutions.First() : resolutions.ElementAt(int.Parse(Choice) - 1);
                Logger.Info("VIDEO: " + selected.Groups[1].Value + "p @ " + selected.Groups[2].Value + " bandwidth");
                string OutputFileName = Episode.Name_Serial + ".S" + Episode.TV_Season_Serial + "E" + Episode.TV_Episode_Serial + "." + Episode.TV_EpisodeTitle_Serial + "." + selected.Groups[1].Value + "p.WEB.[CODEC]";
                DownloadM3U8withMultiThreading(
                    selected.Groups[3].Value,
                    OutputFileName
                );
                string mediaInfo = string.Empty;
                using (MediaInfo.DotNetWrapper.MediaInfo mi = new MediaInfo.DotNetWrapper.MediaInfo()) {
                    mi.Open("output/" + OutputFileName + ".mkv");
                    mi.Option("Complete", "0");
                    mediaInfo = mi.Inform().Replace("\r", string.Empty).TrimEnd();
                }
                Dictionary<string, string>[] mediaInfoBlocksSplit = mediaInfo.Split(new string[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries).Select(
                    block => {
                        return block.Split('\n').Where(l => !l.StartsWith("Complete name ")).Select(
                            line => {
                                Match mc = Regex.Match(line, "^(((?!\\s{2}).)*)[^:]*: (.*)");
                                return new KeyValuePair<string, string>(mc.Groups[1].Value.Trim(), mc.Groups[3].Value.Trim());
                            }
                        ).ToDictionary(x => x.Key, x => x.Value);
                    }
                ).ToArray();
                Dictionary<string, string> mediaInfo_GENERAL = mediaInfoBlocksSplit[0];
                Dictionary<string, string> mediaInfo_VIDEO = mediaInfoBlocksSplit[1];
                Dictionary<string, string>[] mediaInfo_AUDIOS = mediaInfoBlocksSplit.Where(block => block.Any(x => x.Key == "Channel(s)")).ToArray();
                Dictionary<string, string>[] mediaInfo_OTHER = mediaInfoBlocksSplit.Skip(3).ToArray();
                File.Move("output/" + OutputFileName + ".mkv", "output/" + OutputFileName.Replace("[CODEC]", mediaInfo_VIDEO.ContainsKey("Writing library") && mediaInfo_VIDEO["Writing library"].StartsWith("x264") ? "x264" : mediaInfo_VIDEO["Format"].Replace("AVC", "H.264")) + "-PRAGMA.mkv");

                Console.WriteLine("Done!");
            }
            return true;
        }
    }
}
