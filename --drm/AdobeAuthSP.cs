using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace __drm {
    /// <summary>
    /// Unofficial Adobe Auth Interface for C#
    /// Created by twitter.com/PRAGMA
    /// </summary>
    class AdobeAuthSP {
        private static CookieAwareWebClient wc = new CookieAwareWebClient();

        // Public
        public string REQUESTERID = null;
        public string REQUESTINGPAGE = null;
        public string RESOURCEID = null;

        // Private
        private class Tokens {
            public static string N = null;
            public static string Z = null;
        }

        /// <summary>
        /// This is essentially the Login to Provider portion of Adobe Auth
        /// The Token retreived from this only needs to be done once unless its expired
        /// </summary>
        private bool AuthNToken() {
            /* Login to Adobe SAML Authentication or use Cached Data if available */
            if (File.Exists("cache/adobeauth/" + Arguments.APMSOID)) {
                Tokens.N = File.ReadAllText("cache/adobeauth/" + Arguments.APMSOID, Encoding.UTF8);
                //todo: check if expired
                DateTime.ParseExact(
                    Regex.Match(
                        Tokens.N,
                        "<simpleTokenExpires>([^<]+)</simpleTokenExpires>"
                    ).Groups[1].Value,
                    "yyyy/MM/dd HH:mm:ss G\\MT zz00",
                    CultureInfo.InvariantCulture
                );
            } else {
                #region Ask for SAML Authentication
                wc.Headers.Add(HttpRequestHeader.AcceptCharset, "ISO-8859-1,utf-8;q=0.7,*;q=0.7");
                wc.Headers.Add(HttpRequestHeader.AcceptLanguage, "en-us,en;q=0.5");
                wc.Headers.Add(HttpRequestHeader.Accept, "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                wc.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 Safari/537.36");
                string SAMLAuthRes = wc.DownloadString("https://sp.auth.adobe.com/adobe-services/authenticate/saml?noflash=true&mso_id=" + Arguments.APMSOID + "&requestor_id=" + REQUESTERID + "&no_iframe=false&domain_name=adobe.com&redirect_url=" + WebUtility.UrlEncode(REQUESTINGPAGE));
                string RelayState = Uri.EscapeDataString(WebUtility.HtmlDecode(Regex.Match(SAMLAuthRes, " <input type=\"hidden\" name=\"RelayState\" value=\"([^\"]+)\"").Groups[1].Value));
                #endregion
                #region Decode SAML Authentication Response (Fake Form POST Request)
                wc.Headers.Add(HttpRequestHeader.ContentType, "application/x-www-form-urlencoded");
                string loginUrl = Regex.Match(
                    Regex.Match(
                        wc.UploadString(
                            WebUtility.HtmlDecode(Regex.Match(SAMLAuthRes, "<form action=\"([^\"]+)\" method=\"post\">").Groups[1].Value), // https://idp.dtvce.com/dtv-idp-authn/authn/v2
                            string.Join("&", new[] {
                                "SAMLRequest=" + Uri.EscapeDataString(Regex.Match(SAMLAuthRes, " <input type=\"hidden\" name=\"SAMLRequest\" value=\"([^\"]+)\"").Groups[1].Value),
                                "RelayState=" + RelayState
                            })
                        ),
                        "<form.*?</form>", RegexOptions.Singleline
                    ).Value,
                    "<form[^>]+action=\"([^\"]+)\""
                ).Groups[1].Value;
                #endregion
                #region Login to SAML Authentication Response
                wc.Headers.Add(HttpRequestHeader.ContentType, "application/x-www-form-urlencoded");
                string loginRes = wc.UploadString(
                    loginUrl,
                    string.Join("&", new[] {
                        "submit=",
                        "username=" + WebUtility.UrlEncode(Arguments.APUsername),
                        "password=" + WebUtility.UrlEncode(Arguments.APPassword)
                    })
                );
                if (!loginRes.Contains("SAMLResponse")) {
                    if (loginRes.Contains("<div id=\"errorTag\" class=\"contLog loginbgerror\">You entered an incorrect email or password. Please try again.</div>")) {
                        Console.WriteLine("[WARNING]: Login information provided is invalid :(");
                    } else {
                        Console.WriteLine("[!ERROR]: Unexpected response when logging in to \"" + loginUrl + "\"");
                        Console.WriteLine(loginRes);
                    }
                    return false;
                }
                #endregion
                #region Decode Login Response
                wc.Headers.Add(HttpRequestHeader.ContentType, "application/x-www-form-urlencoded");
                wc.UploadString(
                    WebUtility.HtmlDecode(Regex.Match(loginRes, "<form[^>]+action=\"([^\"]+)\"").Groups[1].Value),
                    string.Join("&", new[] {
                        "SAMLResponse=" + Uri.EscapeDataString(Regex.Match(loginRes, "<input type=\"hidden\" name=\"SAMLResponse\" value=\"([^\"]+)\"").Groups[1].Value),
                        "RelayState=" + RelayState
                    })
                );
                #endregion
                #region Get Authn Token/Session Token
                wc.Headers.Clear();
                wc.Headers.Add(HttpRequestHeader.AcceptLanguage, "en-US,en;q=0.9");
                wc.Headers.Add(HttpRequestHeader.ContentType, "application/x-www-form-urlencoded; charset=UTF-8");
                wc.Headers.Add(HttpRequestHeader.Accept, "application/xml");
                wc.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 Safari/537.36");
                wc.Headers.Add("ap_11", "Linux i686");
                wc.Headers.Add("ap_42", "anonymous");
                wc.Headers.Add("ap_z", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 Safari/537.36");
                Tokens.N = WebUtility.HtmlDecode(
                    Regex.Match(
                        wc.UploadString(
                            "https://sp.auth.adobe.com/adobe-services/session",
                            "_method=GET&requestor_id=" + REQUESTERID
                        ),
                        "<authnToken>(.*?)</authnToken>"
                    ).Groups[1].Value
                );
                Directory.CreateDirectory("cache/adobeauth");
                using (StreamWriter streamWriter = new StreamWriter("cache/adobeauth/" + Arguments.APMSOID)) {
                    streamWriter.Write(Tokens.N);
                }
                #endregion
            }
            return true;
        }

        /// <summary>
        /// This is the Token that websites using Adobe Auth request and use for Short Authorizing
        /// This is always different and cannot be cached, it expires very fast
        /// </summary>
        private bool AuthZToken() {
            string postData = string.Join("&", new[] {
                "mso_id=" + Arguments.APMSOID,
                "requestor_id=" + REQUESTERID,
                "authentication_token=" + WebUtility.UrlEncode(Tokens.N),
                "resource_id=" + WebUtility.HtmlEncode(WebUtility.UrlEncode(RESOURCEID)),
                "userMeta=1"
            });
            string cookieHeader = wc.CookieContainer.ToString();
            int attempts = 0;
            do {
                try {
                    string authorizeRes = new CookieAwareWebClient() {
                        Encoding = Encoding.UTF8,
                        Headers = {{
                            HttpRequestHeader.ContentType, "application/x-www-form-urlencoded; charset=UTF-8"
                        },{
                            HttpRequestHeader.Cookie, cookieHeader
                        },{
                            "ap_42", "anonymous"
                        },{
                            "ap_11", "Linux i686"
                        },{
                            "ap_z", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 Safari/537.36"
                        }}
                    }.UploadString(
                        "https://sp.auth.adobe.com/adobe-services/authorize",
                        postData
                    );
                    if(!Regex.IsMatch(authorizeRes, "pendingLogout reason=\"\\d+\">true</pendingLogout>")) {
                        Tokens.Z = WebUtility.HtmlDecode(Regex.Match(authorizeRes, "<authzToken>(.*?)</authzToken>").Groups[1].Value);
                        return true;
                    }
                } catch {
                    Logger.Error("[AdobeAuthSP] Failed to send authorize request... Attempt " + attempts + "/4");
                }
            } while (Tokens.Z == null && ++attempts <= 4);
            //if it reaches max attempts it gets here
            return false;
        }
        /// <summary>
        /// The website using Adobe Auth uses this token against there OWN API
        /// Its essentially the Key/Password generation for their own API
        /// </summary>
        public string ShortAuthorize() {
            // Make sure an AuthN and AuthZ token are grabbed.
            if(!AuthNToken() || !AuthZToken()) {
                return null;
            }
            try {
                string shortToken = new CookieAwareWebClient() {
                    Encoding = Encoding.UTF8,
                    Headers = {{
                        HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 Safari/537.36"
                    },{
                        HttpRequestHeader.ContentType, "application/x-www-form-urlencoded; charset=UTF-8"
                    },{
                        "ap_42", "anonymous"
                    },{
                        "ap_11", "Linux i686"
                    },{
                        "ap_19", string.Empty
                    },{
                        "ap_23", string.Empty
                    },{
                        "ap_z", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/72.0.3626.109 Safari/537.36"
                    }}
                }.UploadString(
                    "https://sp.auth.adobe.com/adobe-services/shortAuthorize",
                    string.Join("&", new[] {
                    "session_guid=" + WebUtility.UrlEncode(Regex.Match(Tokens.N, "<simpleTokenAuthenticationGuid>(.*?)</simpleTokenAuthenticationGuid>").Groups[1].Value),
                    "hashed_guid=false",
                    "requestor_id=" + REQUESTERID,
                    "authz_token=" + WebUtility.UrlEncode(Tokens.Z),
                    "userMeta=1"
                    })
                );
                if (string.IsNullOrEmpty(shortToken)) {
                    Logger.Error("[AdobeAuthSP] shortAuthorize request responded no data??");
                    return null;
                }
                return shortToken;
            } catch {
                Logger.Error("[AdobeAuthSP] Failed to send shortAuthorize request...");
                return null;
            }
        }
    }
}
