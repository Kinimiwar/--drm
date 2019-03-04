namespace __drm {
    class Arguments {
        public static string URL = null;
        public static int Season = -1;
        public static int Episode = -1;
        public static string Quality = "best";
        public static string TrackType = null;
        public static string APUsername = null;
        public static string APPassword = null;
        public static string APMSOID = null;
        public static void Parse(string[] args) {
            for (int i = 0; i < args.Length; i++) {
                if (args[i].StartsWith("--")) {
                    string v = args[i + 1];
                    switch (args[i].Substring(2)) {
                        case "url":
                        URL = v;
                        break;
                        case "season":
                        Season = int.Parse(v.ToLower().Replace("s", string.Empty));
                        break;
                        case "episode":
                        Episode = int.Parse(v.ToLower().Replace("e", string.Empty));
                        break;
                        case "quality":
                        Quality = v;
                        break;
                        case "trackType":
                        TrackType = v;
                        break;
                        case "ap-username":
                        APUsername = v;
                        break;
                        case "ap-password":
                        APPassword = v;
                        break;
                        case "ap-msoid":
                        APMSOID = v;
                        break;
                    }
                }
            }
        }
    }
}
