using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using static __drm.Common;

namespace __drm.Modules {
    public class adultswim {
        public override string ToString() {
            Help(new[] {
                "URL can be the TV Show Page, Season Page or Episode Page.",
                "Providing a TV Show Page (with --season) or a Season Page URL will download all episodes of that season.",
                "--quality ('best'[str] or height[int])",
                "--season ([int], only used if you provide the TV Show Page e.x. disneynow.go.com/shows/phineas-and-ferb)",
                "--episode ([int], when used on a TV Show or Season page, it will skip to the set episode and download from there on)",
                "--ap-msoid ([str], required for most episodes, required even if there is cached data)",
                "--ap-username ([str], required for most episodes, and only required to be used if theres no cached ap data)",
                "--ap-password ([str], required for most episodes, and only required to be used if theres no cached ap data)"
            });
            return null;
        }
        public void Start() {
            SetTitle(Module.AdultSwim);
            DownloadEpisode(Arguments.URL);
        }
        public void DownloadEpisode(string URL) {
            string PageContent = new WebClient().DownloadString(URL);
            string MediaID = null;
            bool MediaProtected = false;
            foreach (string video in Regex.Matches(PageContent, "\"Video:[^\"]*\":{([^}]*}[^}]*)}").Cast<Match>().Select(m => m.Groups[1].Value)) {
                if (!video.Contains("\"slug\":\"" + URL.Substring(URL.LastIndexOf('/') + 1) + "\"")) {
                    continue;
                }
                MediaProtected = !video.Contains("\"auth\":false,");
                if (!MediaProtected) {
                    MediaID = Regex.Match(video, "\"_id\":\"([^\"]*)").Groups[1].Value;
                } else {
                    MediaID = Regex.Match(video, "\"mediaID\":\"([^\"]*)").Groups[1].Value;
                }
                break;
            }
            if (MediaID == null) {
                Logger.Error("No MediaID obtained, aborting");
                return;
            }
            if (MediaProtected) {

                Logger.Info(URL + " is locked behind Adobe MSO/SP Auth, Getting it via Adobe shortAuthorize and authorize.json");
                
                // Get M3U8 URL
                AdobeAuthSP aasp = new AdobeAuthSP {
                    REQUESTERID = "AdultSwim",
                    REQUESTINGPAGE = URL,
                    RESOURCEID = "AdultSwim" //shouldnt this have some content data like disney and such? works but ehh
                };
                string NGTVRes = new WebClient().DownloadString("http://medium.ngtv.io/media/" + MediaID + "/tv");
                string bulkAesURL = Regex.Match(NGTVRes, "bulkaes\":{.*?\"url\":\"([^\"]*)").Groups[1].Value;
                WebClient wc = new WebClient();
                wc.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 Safari/537.36");
                wc.Headers.Add(HttpRequestHeader.ContentType, "application/x-www-form-urlencoded");
                ServicePointManager.Expect100Continue = false;
                string M3U8Url = bulkAesURL + "?hdnea=" + Regex.Match(
                    wc.UploadString(
                        "http://token.vgtf.net/token/token_spe",
                        string.Join("&", new[] {
                            "path=" + WebUtility.UrlEncode(Regex.Match(bulkAesURL, "https?://[^/]+(.+)$").Groups[1].Value),
                            "videoId=" + MediaID,
                            "profile=tve", //?
                            "accessTokenType=Adobe", //Adobe Auth
                            "accessToken=" + WebUtility.UrlEncode(aasp.ShortAuthorize())
                        })
                    ),
                    "<token>(.*)?<\\/token>"
                ).Groups[1].Value;

                // Chapters
                string bulkAesData = JToken.Parse(NGTVRes)["media"]["tv"]["bulkaes"].ToString();
                if (bulkAesData.Contains("\"start\"")) {
                    Logger.Debug("Generating Chapters (based on json bulkaes data)");
                    PrepareDirectory(new[] { "temp" });
                    File.WriteAllText(
                        "temp/.chapters",
                        string.Join("\n",
                            Regex.Matches(bulkAesData, "\"start\":([^,]*)").Cast<Match>().Select((x, i) => {
                                int num = i + 1;
                                string numPad = num.ToString().PadLeft(2, '0');
                                return string.Join("\n", new[] {
                                    "CHAPTER" + numPad + "=" + TimeSpan.FromSeconds(double.Parse(x.Groups[1].Value)).ToString("hh\\:mm\\:ss\\.fff"),
                                    "CHAPTER" + numPad + "NAME=Chapter " + num
                                });
                            })
                        )
                    );
                }
                
                // Download M3U8 File Contents and Get Resolutions
                string M3U8Content = new WebClient().DownloadString(M3U8Url);
                IOrderedEnumerable<Match> resolutions = Regex.Matches(M3U8Content, "#EXT-X-STREAM.*?BANDWIDTH=([^,]*),RESOLUTION=[^x]*x([^,]*).*\\s(.*)").Cast<Match>().OrderByDescending(f => int.Parse(f.Groups[1].Value)).OrderByDescending(f => int.Parse(f.Groups[2].Value));
                string Choice = string.Empty;
                bool CLIChoiceInvalid = Arguments.Quality != null && Arguments.Quality != "best" && !resolutions.Cast<Match>().Any(x => x.Groups[2].Value == Arguments.Quality);
                if (Arguments.Quality == null || CLIChoiceInvalid) {
                    if (CLIChoiceInvalid) {
                        Logger.Error("--quality value \"" + Arguments.Quality + "\" is not valid for " + URL + "\n Please choose a new one below.");
                    }
                    VideoTracks(resolutions.Cast<Match>().Select((res, i) => res.Groups.Cast<Group>().Skip(1).Take(2).Select(x => x.Value).ToArray()).ToArray());
                    Choice = AskInput("Which resolution do you wish to download? (use # or 'best')").Trim('#');
                } else {
                    Choice = Arguments.Quality;
                }
                Match selected = Choice == "best" ? resolutions.First() : resolutions.ElementAt(int.Parse(Choice) - 1);
                Logger.Info("VIDEO: " + selected.Groups[2].Value + "p @ " + selected.Groups[1].Value + " bandwidth");

                string OutputFileName = "out";// Episode.Name_Serial + ".S" + Episode.TV_Season_Serial + "E" + Episode.TV_Episode_Serial + "." + Episode.TV_EpisodeTitle_Serial + "." + selected.Groups[1].Value + "p.WEB.[CODEC]";
                DownloadM3U8withMultiThreading(
                    bulkAesURL.Substring(0, bulkAesURL.LastIndexOf("/") + 1) + selected.Groups[3].Value,
                    OutputFileName
                );

                // SHOULDNT NEED:
                
                //string text7 = new WebClient().DownloadString(chosenResM3u8Url);
                //File.WriteAllText("thing.m3u8", text7);
                //string text8 = Regex.Match(chosenResM3u8Url, "(http.*/)").Groups[1].Value + "seg.key";
                //string text9 = Regex.Match(text7, "(#EXT-X-KEY:METHOD=AES-128.*)").Groups[1].Value.Replace("seg.key", text8);
                //List<string> list3 = new List<string>();
                //int num2 = 0;
                //foreach (Match match3 in Regex.Matches(text7, "(#EXTINF[^,]+,.*?)#EXT-X-(DISCONTINUITY|ENDLIST)", RegexOptions.Singleline)) {
                //    string text11 = "output_seg" + num2.ToString();
                //    list3.Add(text11);
                //    using (StreamWriter streamWriter2 = new StreamWriter(text11 + ".m3u8")) {
                //        streamWriter2.Write("#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXT-X-VERSION:4\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n" + text9 + "\n" + Regex.Replace(match3.Groups[1].Value, "^([^#].*)$", Regex.Match(chosenResM3u8Url, "(http.*/)").Groups[1].Value + "$1", RegexOptions.Multiline) + Environment.NewLine + "#EXT-X-ENDLIST");
                //        streamWriter2.Flush();
                //    }
                //    num2++;
                //}
                //foreach (string text12 in list3) {
                //    if (RunEXE("ffmpeg.exe", "-protocol_whitelist file,http,https,tcp,tls,crypto -y -hide_banner -i \"" + text12 + ".m3u8\" -c copy \"output_seg" + list3.IndexOf(text12).ToString() + ".ts\"") != 0) {
                //        Console.WriteLine("[!ERROR]: ffmpeg closed with an error code :(");
                //    }
                //    RunEXE("ccextractor/ccextractorwin.exe", "--no_progress_bar \"output_seg" + list3.IndexOf(text12).ToString() + ".ts\"");
                //}

                //string text13 = "--output \"output.mkv\" ";
                //foreach (string str2 in list3) {
                //    text13 += "\"" + str2 + ".ts\" + ";
                //}
                //text13 = text13.Trim(new[] { ' ', '+' });
                //foreach (string text14 in list3) {
                //    if (File.Exists(text14 + ".srt")) {
                //        text13 += " --default-track 0:false --sub-charset 0:UTF-8 \"" + text14 + ".srt\" + ";
                //    } else {
                //        Console.WriteLine("[!ERROR]: Subtitle segment " + list3.IndexOf(text14).ToString() + " missing. Subtitles might be out of sync.");
                //    }
                //}
                //text13 = text13.Trim(new[] { ' ', '+' });
                //if (File.Exists("output_chapters.txt")) {
                //    text13 = text13 + " --chapters \"output_chapters.txt\"";
                //}
                //Console.WriteLine("Muxing...");
                //if (RunEXE("mkvmerge/mkvmerge.exe", text13) != 0) {
                //    Console.WriteLine("MKVMerge exited with error.");
                //    return false;
                //}
                //foreach (string str3 in list3) {
                //    File.Delete(str3 + ".ts");
                //    File.Delete(str3 + ".srt");
                //    File.Delete(str3 + ".m3u8");
                //}
                //File.Delete("output_chapters.txt");
            } else {
                Logger.Info(URL + " is FREE! Downloading directly via apiv1");

                JToken APIRes = JToken.Parse(new WebClient().DownloadString("https://www.adultswim.com/api/shows/v1/videos/" + MediaID + "?fields=title%2Ccollection_title%2Cstream%2Csegments"))["data"]["video"];
                string PageRes = new WebClient().DownloadString(APIRes["url"].ToString());

                string chaptersdata = new WebClient().DownloadString(APIRes["stream"]["assets"].Where(asset => asset["url"].ToString().Contains("cue_points")).First()["url"].ToString());

                // Subtitles
                string vtt = APIRes["stream"]["assets"].Where(asset => asset["mime_type"].ToString() == "text/vtt").First()["url"].ToString();
                if (vtt != string.Empty) {
                    DownloadSRT(vtt);
                }

                // Chapters
                if (chaptersdata.Contains("<start time=\"")) {
                    Logger.Debug("Generating Chapters (based on xml adcue data)");
                    PrepareDirectory(new[] { "temp" });
                    File.WriteAllText(
                        "temp/.chapters",
                        string.Join("\n",
                            Regex.Matches(chaptersdata, "start time=\"([^:]*):([^:]*):([^:]*):([^\"]*)").Cast<Match>().Select((x, i) => {
                                int num = i + 1;
                                string numPad = num.ToString().PadLeft(2, '0');
                                return string.Join("\n", new[] {
                                    "CHAPTER" + numPad + "=" + x.Groups[1].Value + ":" + x.Groups[2].Value + ":" + x.Groups[2].Value + "." + x.Groups[2].Value + "0",
                                    "CHAPTER" + numPad + "NAME=Chapter " + num
                                });
                            })
                        )
                    );
                }

                string Title = Regex.Replace(APIRes["collection_title"].ToString(), "[/\\*!?,.\'\"()<>:|]", string.Empty).Replace(" ", ".");
                string EpisodeName = Regex.Replace(APIRes["title"].ToString(), "[/\\*!?,.\'\"()<>:|]", string.Empty).Replace(" ", ".");
                string SeasonNumber = "0";
                Match EpisodeFind = Regex.Match(PageRes, "EP[^<]*?<!-- -->([^<]*)<!-- -->[^<]*?<\\/span><span>" + APIRes["title"].ToString() + "<\\/span>");
                string EpisodeNumber = EpisodeFind.Groups[1].Value;

                int[] Seasons = Regex.Matches(PageRes, "Season ([^<]*)<\\/h3>").Cast<Match>().Select(x => int.Parse(x.Groups[1].Value)).ToArray();
                for (int i = 0; i < Seasons.Length; i++) {
                    int Season = Seasons[i];
                    int EpisodeTextIndex = PageRes.IndexOf(EpisodeFind.Value);
                    // If the Episode text is after the current season text
                    if (EpisodeTextIndex > PageRes.IndexOf("Season " + Season + "<\\/h3>")) {
                        // and If its the last season number found, then it must be the one.
                        // or
                        // and if the episode is before the next season number it must be the one.
                        if(i == Seasons.Length - 1 || EpisodeTextIndex < PageRes.IndexOf("Season " + Seasons[i + 1] + "<\\/h3>")) {
                            SeasonNumber = Season.ToString();
                        }
                    }
                }

                string OutputFilename = Title + ".S" + SeasonNumber.ToString().PadLeft(2, '0') + "E" + EpisodeNumber.PadLeft(2, '0') + "." + EpisodeName + ".[QUALITY]p.WEB.[CODEC]";
                Logger.Debug(OutputFilename);

                DownloadM3U8withMultiThreading(
                    ChooseResolutionFromM3U8(
                        Regex.Match(new WebClient().DownloadString("https://www.adultswim.com/api/shows/v1/media/" + MediaID + "/desktop"), "url\":\"([^\"]*)").Groups[1].Value
                    ),
                    OutputFilename
                );
            }
        }
    }
}
