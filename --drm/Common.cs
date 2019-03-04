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
        public enum Module {
            SimpsonsWorld,
            DisneyNow
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
                using (CookieAwareWebClient wc = new CookieAwareWebClient() {
                    Encoding = Encoding.UTF8,
                    Headers = {{
                        HttpRequestHeader.UserAgent,
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.119 Safari/537.36"
                    },{
                        HttpRequestHeader.Referer,
                        Referer ?? new Uri(URL).Host
                    }}
                })
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
                    new WebClient().DownloadFile(keyurl, fn);
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
                    new WebClient().DownloadFile(ts, fn);
                    segMap.TryAdd(ts, fn.Replace("temp/", string.Empty));
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
