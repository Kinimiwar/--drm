using System;
using System.Net;

namespace __drm {
    public class CookieAwareWebClient : WebClient {
        public CookieContainer CookieContainer { get; set; }
        public Uri Uri { get; set; }

        public CookieAwareWebClient() : this(new CookieContainer()) {
        }

        public CookieAwareWebClient(CookieContainer cookies) {
            CookieContainer = cookies;
        }

        protected override WebRequest GetWebRequest(Uri address) {
            WebRequest wr = base.GetWebRequest(address);
            if (wr is HttpWebRequest) {
                (wr as HttpWebRequest).CookieContainer = CookieContainer;
            }
            HttpWebRequest hwr = (HttpWebRequest)wr;
            hwr.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return hwr;
        }

        protected override WebResponse GetWebResponse(WebRequest request) {
            WebResponse response = base.GetWebResponse(request);
            string setCookieHeader = response.Headers[HttpResponseHeader.SetCookie];

            //do something if needed to parse out the cookie.
            if (!string.IsNullOrEmpty(setCookieHeader)) {
                try {
                    Cookie cookie = new Cookie(); //create cookie
                    if(cookie != null) {
                        CookieContainer.Add(cookie);
                    }
                } catch {
                }
            }

            return response;
        }
    }
}
