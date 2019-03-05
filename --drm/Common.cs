using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace __drm {
    class Common {
        public static string EXEDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        public static CookieAwareWebClient wc = new CookieAwareWebClient() {
            Encoding = Encoding.UTF8,
            Headers = {{
                HttpRequestHeader.UserAgent,
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.119 Safari/537.36"
            }}
        };
        public enum Module {
            AdultSwim,
            DisneyNow,
            SimpsonsWorld
        }
        public static void SetTitle(Module m) => Console.Title = "--drm | Module: " + m.ToString() + " " + Arguments.URL;
        public static string AskInput(string query) {
            Console.WriteLine(" " + query);
            return Console.ReadLine().Trim();
        }
        public static void DeleteDirectory(string target_dir) {
            foreach (string file in Directory.GetFiles(target_dir)) {
                File.Delete(file);
            }
            foreach (string dir in Directory.GetDirectories(target_dir)) {
                DeleteDirectory(dir);
            }
            Directory.Delete(target_dir, false);
        }
        public static void Help(string[] Entries) {
            Logger.Info("CLI Switches:\n " + string.Join("\n ", Entries));
        }

        public static dynamic Fetch(string URL, string Referer = null, string ReturnAs = "string") {
            if (!URL.StartsWith("http")) {
                URL = "http://" + URL.TrimStart('/');
            }
            int Failures = 0;
            do {
                wc.Headers.Add(HttpRequestHeader.Referer, Referer ?? new Uri(URL).Host);
                using (Stream s = wc.OpenRead(URL))
                using (StreamReader sr = new StreamReader(wc.ResponseHeaders["Content-Encoding"] == "gzip" ? new GZipStream(s, CompressionMode.Decompress) : s)) {
                    string text = sr.ReadToEnd();
                    if (string.IsNullOrEmpty(text)) {
                        continue;
                    }
                    switch (ReturnAs) {
                        case "string":
                        return text;
                        case "json":
                        return JObject.Parse(text);
                        case "xml": {
                            XmlDocument xmlDocument = new XmlDocument();
                            xmlDocument.LoadXml(text);
                            return xmlDocument;
                        }
                    }
                }
            }
            while (++Failures <= 4);
            return null;
        }
        public static void VideoTracks(string[][] mc) {
            Logger.Info(
                mc.Length + " Video Tracks:\n" +
                " " + string.Join("\n ", mc.Select((t, i) => "#" + (i + 1) + " | " + string.Join(" / ", t.Select((d, m) => m==0?d+"p":d))))
            );
        }
        public static void PrepareDirectory(string[] dirs) {
            foreach (string dir in dirs) {
                Directory.CreateDirectory(dir);
            }
        }

        public static string ChooseResolutionFromM3U8(string M3U8Url) {
            string M3U8Content = new WebClient().DownloadString(M3U8Url);
            MatchCollection Streams = Regex.Matches(M3U8Content, "(#EXT-X-STREAM.*)\\s(.*)");
            List<(int resolution, int bandwidth, string url)> Resolutions = new List<(int, int, string)>();
            foreach (Match Stream in Streams) {
                string INFO = Stream.Groups[1].Value;
                Resolutions.Add((int.Parse(Regex.Match(INFO, "RESOLUTION=[^x]*x([^,]*)").Groups[1].Value), int.Parse(Regex.Match(INFO, "BANDWIDTH=([^,]*)").Groups[1].Value), Stream.Groups[2].Value));
            }
            IOrderedEnumerable<(int resolution, int bandwidth, string url)> OrderedResolutions = Resolutions.OrderByDescending(x => x.bandwidth).OrderByDescending(x => x.resolution);
            string Choice = string.Empty;
            bool CLIChoiceInvalid = Arguments.Quality != null && Arguments.Quality != "best" && !OrderedResolutions.Cast<Match>().Any(x => x.Groups[2].Value == Arguments.Quality);
            if (Arguments.Quality == null || CLIChoiceInvalid) {
                if (CLIChoiceInvalid) {
                    Logger.Error("--quality value \"" + Arguments.Quality + "\" is not valid\n Please choose a new one below.");
                }
                VideoTracks(OrderedResolutions.Select(res => new[] { res.resolution.ToString(), res.bandwidth.ToString() }).ToArray());
                Choice = AskInput("Which resolution do you wish to download? (use # or 'best')").Trim('#');
            } else {
                Choice = Arguments.Quality;
            }
            var Selected = Choice == "best" ? OrderedResolutions.First() : OrderedResolutions.ElementAt(int.Parse(Choice) - 1);
            if(!Selected.url.StartsWith("http")) {
                Selected.url = M3U8Url.Substring(0, M3U8Url.LastIndexOf('/') + 1) + Selected.url;
            }
            Logger.Info("VIDEO: " + Selected.resolution + "p @ " + Selected.bandwidth + " bandwidth");
            Logger.Debug("M3U8: " + Selected.url);
            return Selected.url;
        }

        public static void DownloadSRT(string srturl) {
            Logger.Debug("Downloading Subtitle: " + Path.GetFileName(srturl));
            PrepareDirectory(new[] { "temp" });
            new WebClient().DownloadFile(srturl, "temp/.srt");
            if(Path.GetExtension(srturl).ToLower() != "srt") {
                if(RunEXE("subtitleedit/SubtitleEdit.exe", "/convert \"temp/.srt\" srt /overwrite") != 0) {
                    return;
                }
            }
        }
        public static void DownloadM3U8withFFMPEG(string URL, string OutputFilename) {
            Logger.Info("Downloading M3U8 TS Segments to Single TS file with FFMPEG (Slow!)");
            PrepareDirectory(new[] { "temp" });
            if (RunEXE("ffmpeg.exe", "-protocol_whitelist file,http,https,tcp,tls,crypto -allowed_extensions ALL -y -hide_banner -i \"" + URL + "\" -c copy \"temp/.ts\"") != 0) {
                return;
            }
            MuxM3U8(OutputFilename);
        }
        public static void DownloadM3U8withMultiThreading(string URL, string OutputFilename) {
            Logger.Info("Downloading M3U8 TS Segments Multi-Threaded to Segements Folder and fixing with FFMPEG (FAST!)");
            PrepareDirectory(new[] { "temp", "temp/segments", "temp/keys" });
            // Get M3U8 Data
            string m3u8res = Fetch(URL);
            // Download Keys and replace m3u8's key' paths' to local relative locations
            ConcurrentDictionary<string, string> keyMap = new ConcurrentDictionary<string, string>();
            Parallel.ForEach(
                Regex.Matches(m3u8res, "#EXT-X-KEY:METHOD=AES-128,URI=\"([^\"]*)").Cast<Match>().Select(x => x.Groups[1].Value).Distinct(),
                keyurl => {
                    string fn = "temp/keys/" + keyurl.GetHashCode().ToString().Replace("-", "m") + ".key";
                    new WebClient().DownloadFile((!keyurl.StartsWith("http") ? URL.Substring(0, URL.LastIndexOf('/') + 1) : string.Empty) + keyurl, fn);
                    keyMap.TryAdd(keyurl, fn.Replace("temp/", string.Empty));
                }
            );
            foreach (KeyValuePair<string, string> key in keyMap) {
                m3u8res = m3u8res.Replace(key.Key, key.Value);
            }
            // Download TS Segments and replace m3u8's ts' paths' to local relative locations
            ConcurrentDictionary<string, string> segMap = new ConcurrentDictionary<string, string>();
            Parallel.ForEach(
                Regex.Matches(m3u8res, "#EXTINF:.*\\s(.*)").Cast<Match>().Select(x => x.Groups[1].Value).Distinct(),
                ts => {
                    string fn = "temp/segments/" + ts.GetHashCode().ToString().Replace("-", "m") + ".ts";
                    bool downloaded = false;
                    while (!downloaded) {
                        try {
                            new WebClient().DownloadFile((!ts.StartsWith("http") ? URL.Substring(0, URL.LastIndexOf('/') + 1) : string.Empty) + ts, fn);
                            segMap.TryAdd(ts, fn.Replace("temp/", string.Empty));
                            downloaded = true;
                        } catch (Exception ex) {
                            Logger.Error("Failed while downloading \"" + ts + "\", Retrying, Error Message: " + ex.Message);
                        }
                    }
                }
            );
            foreach (KeyValuePair<string, string> ts in segMap) {
                m3u8res = m3u8res.Replace(ts.Key, ts.Value);
            }
            // Write new M3U8 content to a file so FFMPEG can read it
            File.WriteAllText("temp/.m3u8", m3u8res);
            if (RunEXE("ffmpeg.exe", "-protocol_whitelist file,http,https,tcp,tls,crypto -allowed_extensions ALL -y -hide_banner -i \"temp/.m3u8\" -c copy \"temp/.ts\"") != 0) {
                return;
            }
            // Delete now un-needed data
            DeleteDirectory("temp/keys");
            DeleteDirectory("temp/segments");
            File.Delete("temp/.m3u8");
            MuxM3U8(OutputFilename);
        }
        private static void MuxM3U8(string OutFileName) {
            PrepareDirectory(new[] { "output" });
            Logger.Info("Using MKVMERGE to mux single TS file, chapters and subtitles to final MKV file");
            if (RunEXE("mkvmerge/mkvmerge.exe", "--output \"" + Path.Combine(EXEDirectory, "output", OutFileName.Replace("/", "-") + ".mkv") + "\" \"" + Path.Combine(EXEDirectory, "temp", ".ts") + "\" --default-track 0:false " + (File.Exists("temp/.chapters") ? "--chapters \"" + Path.Combine(EXEDirectory, "temp", ".chapters") + "\"" : string.Empty) + " " + (File.Exists("temp/.srt") ? "--sub-charset 0:UTF-8 \"" + Path.Combine(EXEDirectory, "temp", ".srt") + "\"" : string.Empty)) != 0) {
                return;
            }
            string mediaInfo = string.Empty;
            using (MediaInfo.DotNetWrapper.MediaInfo mi = new MediaInfo.DotNetWrapper.MediaInfo()) {
                mi.Open("output/" + OutFileName + ".mkv");
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
            File.Move("output/" + OutFileName + ".mkv", "output/" + OutFileName.Replace("[QUALITY]", string.Join(string.Empty, mediaInfo_VIDEO["Height"].Select(x => char.IsDigit(x) ? x.ToString() : string.Empty))).Replace("[CODEC]", mediaInfo_VIDEO.ContainsKey("Writing library") && mediaInfo_VIDEO["Writing library"].StartsWith("x264") ? "x264" : mediaInfo_VIDEO["Format"].Replace("AVC", "H.264")) + "-PRAGMA.mkv");

            // Cleanup files no longer needed
            // todo: setup files in such a way to be multi-threaded supported and not conflict with other downloads at same time
            File.Delete("temp/.ts");
            File.Delete("temp/.srt");
            File.Delete("temp/.chapters");
        }
        public static int RunEXE(string exePath, string args) {
            //todo: find a more convenient better looking way to ignore all output and windows then using redirect like this
            Process p = new Process {
                StartInfo = new ProcessStartInfo(Path.Combine(EXEDirectory, "tools", exePath)) {
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true
                }
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            if (p.ExitCode != 0) {
                Logger.Error(Path.GetFileName(exePath) + " closed with an error code :( (Something, unsure what, went wrong)");
            }
            return p.ExitCode;
        }
    }
}
