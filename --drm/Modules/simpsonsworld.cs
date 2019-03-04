using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using static __drm.Common;

namespace __drm.Modules {
    public class simpsonsworld {
        public override string ToString() {
            Help(new[] {
                "URL can be the Episode Guide Page (Has All Seasons) or Episode Page.",
                "Providing the Episode Guide Page (with --season) URL will download all episodes of that season.",
                "--quality ('best'[str] or height[int])",
                "--season ([int], only used if you provide the Episode Guide Page: simpsonsworld.com/browse/episodes)",
                "--episode ([int], when used on the Episode Guide Page, it will skip to the set episode and download from there on)",
                "--ap-msoid ([str], required)",
                "--ap-username ([str], only required if theres no cached ap data)",
                "--ap-password ([str], only required if theres no cached ap data)"
            });
            return null;
        }
        public void Start() {
            SetTitle(Module.SimpsonsWorld);

            if (Arguments.Season != -1) {
                DownloadSeason(Arguments.URL, Arguments.Season);
            } else {
                string EpisodeID = Arguments.URL.Substring(Arguments.URL.IndexOf("/video/") + 7);
                Logger.Info("Downloading \"" + EpisodeID + "\"");
                DownloadEpisode(EpisodeID);
            }

        }
        public static void DownloadSeason(string url, int season) {
            string pageRes = new WebClient().DownloadString(url);
            string[] EpisodeIDs = Regex.Matches(pageRes, "\\/video\\/([^\"]*)\" data-season-number=\"" + season.ToString() + "\"").Cast<Match>().Select(x => x.Groups[1].Value).ToArray();
            Logger.Info(EpisodeIDs.Length + " Episodes in S" + season + " found\n");
            int position = 0;
            foreach (string EpisodeID in EpisodeIDs) {
                Logger.Info("Downloading #" + ++position + "/" + EpisodeIDs.Length + " \"" + EpisodeID + "\"");
                DownloadEpisode(EpisodeID);
            }
        }
        public static void DownloadEpisode(string EpisodeID) {
            string VideoPage = "http://www.simpsonsworld.com/video/" + EpisodeID.ToString();
            string UrlContent = new WebClient().DownloadString(VideoPage);

            string[] TrackTypes = Regex.Matches(UrlContent, "var url_([^ ]*)").Cast<Match>().Select(t => t.Groups[1].Value).ToArray();
            string SelectedTrackType = string.Empty;
            bool CLIChoiceExists = Arguments.TrackType == null || TrackTypes.Contains(Arguments.TrackType);
            if (Arguments.TrackType == null || !CLIChoiceExists) {
                if(!CLIChoiceExists) {
                    Logger.Error("--trackType value \"" + Arguments.TrackType + "\" is not valid for " + VideoPage + "\n Please choose a new one below.");
                }
                Logger.Info(
                    TrackTypes.Length + " Video Track Types:\n" +
                    " " + string.Join("\n ", TrackTypes.Select((t, i) => "#" + (i + 1) + " | " + t + " (" + t.Replace("x", ":").Replace("_commentary", " (+Commentary)") + " Version)"))
                );
                SelectedTrackType = TrackTypes[int.Parse(AskInput("Which Track Type do you wish to download? (use #)").Trim('#')) - 1];
            } else {
                SelectedTrackType = Arguments.TrackType;
            }
            Logger.Info("VIDEOTYPE: " + SelectedTrackType.Replace("x", ":").Replace("_commentary", " (+Commentary)") + " Version (" + SelectedTrackType + ")");

            Match ResourceData = Regex.Match(UrlContent, "content_id:\"([^\"]*)\",content_title:\"([^\"]*)");
            string Resource_GUID = ResourceData.Groups[1].Value;

            AdobeAuthSP aasp = new AdobeAuthSP {
                REQUESTERID = "fx",
                REQUESTINGPAGE = VideoPage,
                RESOURCEID = "<rss version=\"2.0\" xmlns:media=\"http://search.yahoo.com/mrss/\"> <channel>     <title>fxx</title>     <item>         <title>" + ResourceData.Groups[2].Value + "</title>         <guid>" + Resource_GUID + "</guid>         <media:rating scheme=\"urn:v-chip\">" + Regex.Match(UrlContent, "data-guid=\"" + Resource_GUID + "\" data-rating=\"([^\"]*)").Groups[1].Value + "</media:rating>     </item> </channel></rss>"
            };

            string ShortToken = aasp.ShortAuthorize();
            if (ShortToken == null) {
                Console.WriteLine("[!ERROR]: Adobe Authorization FAILED. ABORTING!");
                return;
            }

            WebClient wc = new WebClient();
            wc.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 Safari/537.36");
            wc.Headers.Add(HttpRequestHeader.Accept, "*/*");
            string postRes = wc.DownloadString(Regex.Match(UrlContent, SelectedTrackType + " = '([^']*)").Groups[1].Value + "&auto=true&sdk=PDK%205.8.6&auth=" + WebUtility.UrlEncode(ShortToken) + "&formats=m3u,mpeg4&format=SMIL&embedded=true&tracking=true");

            string videoUrl = Regex.Match(postRes, "video src=\"([^\"]*)").Groups[1].Value;
            string SRTURL = Regex.Match(postRes, "textstream src=\"([^\"]*.srt)").Groups[1].Value;
            string m3u8Content = new WebClient().DownloadString(videoUrl);

            MatchCollection resolutions = Regex.Matches(m3u8Content, "#EXT-X-STREAM.*?BANDWIDTH=([^,]*),RESOLUTION=[^x]*x([^,]*).*\\s(.*)");
            string Choice = string.Empty;
            bool CLIChoiceInvalid = Arguments.Quality != null && Arguments.Quality != "best" && !resolutions.Cast<Match>().Any(x => x.Groups[2].Value == Arguments.Quality);
            if (Arguments.Quality == null || CLIChoiceInvalid) {
                if(CLIChoiceInvalid) {
                    Logger.Error("--quality value \"" + Arguments.Quality + "\" is not valid for " + VideoPage + "\n Please choose a new one below.");
                }
                VideoTracks(resolutions.Cast<Match>().Select((res, i) => res.Groups.Cast<Group>().Reverse().Skip(1).Take(2).Select(x => x.Value).ToArray()).ToArray());
                Choice = AskInput("Which resolution do you wish to download? (use # or 'best')").Trim('#');
            } else {
                Choice = Arguments.Quality;
            }
            string chosenResM3u8Url = string.Empty;
            int bestHeight = 0;
            int bestBandwidth = 0;
            if (Choice == "best") {
                foreach (Match res in resolutions) {
                    int height = int.Parse(res.Groups[2].Value);
                    int bandwidth = int.Parse(res.Groups[1].Value);
                    if (height > bestHeight && bandwidth > bestBandwidth) {
                        bestHeight = height;
                        bestBandwidth = bandwidth;
                        chosenResM3u8Url = res.Groups[3].Value;
                        //QualityP = res.Groups[2].Value;
                    }
                }
            } else {
                Match res = resolutions[int.Parse(Choice) - 1];
                bestHeight = int.Parse(res.Groups[2].Value);
                bestBandwidth = int.Parse(res.Groups[1].Value);
                chosenResM3u8Url = res.Groups[3].Value;
                //QualityP = res.Groups[2].Value;
            }
            Logger.Info("VIDEO: " + bestHeight + "p @ " + bestBandwidth + " bandwidth");

            DownloadSRT(SRTURL);
            JToken TitleInfo = JToken.Parse(
                Regex.Match(
                    UrlContent.Replace("\"name\" : \"Matt \"Groening\" Nastuk\"", "\"name\" : \"Matt Nastuk\""),
                    "<script type='application\\/ld\\+json'>([\\s\\S]*?)<\\/script>"
                ).Groups[1].Value
            );
            DownloadM3U8withMultiThreading(
                chosenResM3u8Url,
                TitleInfo["partOfSeries"]["name"].ToString().Replace(' ', '.') + ".S" + TitleInfo["partOfSeason"]["seasonNumber"].ToString().PadLeft(2, '0') + "E" + TitleInfo["episodeNumber"].ToString().PadLeft(2, '0') + "." + TitleInfo["name"].ToString().Replace(' ', '.') + "." + bestHeight + "p.WEB"
            );
            Console.WriteLine("Downloaded!\n");
        }
    }
}
