﻿using MoeLoaderDelta;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SitePack
{
    public class SiteSankaku : AbstractImageSite
    {
        private SiteBooru booru;
        private readonly SessionClient Sweb = new SessionClient(SiteManager.SecurityType);
        private readonly SessionHeadersCollection shc = new SessionHeadersCollection();
        public override SessionHeadersCollection SiteHeaders => shc;
        private static bool IsLoginSite = false, IsRunLogin = IsLoginSite;
        private string sitePrefix, temppass, tempappkey, ua, pageurl;
        private static string cookie = string.Empty, authorization = cookie, nowUser = cookie, nowPwd = cookie, prevSitePrefix = cookie;

        public override string SiteUrl => $"https://{sitePrefix}.sankakucomplex.com";
        public override string SiteName => $"{sitePrefix}.sankakucomplex.com";
        public override string ShortName => sitePrefix.Contains("chan") ? "chan.sku" : "idol.sku";
        public override bool IsSupportScore => false;
        public override bool IsSupportCount => true;
        //public override string Referer => sitePrefix.Contains("chan") ? "https://beta.sankakucomplex.com/" : null;
        public override string SubReferer => "*";
        public override string LoginURL => SiteManager.SiteLoginType.FillIn.ToSafeString();
        public override string LoginUser { get => nowUser; set => nowUser = value; }
        public override string LoginPwd { get => nowPwd; set => nowPwd = value; }
        public override bool LoginSiteIsLogged => IsLoginSite;

        /// <summary>
        /// sankakucomplex site
        /// </summary>
        public SiteSankaku(string prefix)
        {
            shc.Timeout = 18000;
            sitePrefix = prefix;

            if (sitePrefix == "idol")
            {
                ua = "SCChannelApp/3.2 (Android; idol)";
            }

            CookieRestore();
        }

        /// <summary>
        /// 取页面源码 来自官方APP处理方式
        /// </summary>
        public override string GetPageString(int page, int count, string keyWord, IWebProxy proxy)
        {
            if (prevSitePrefix != sitePrefix)
            {
                IsLoginSite = false;
                prevSitePrefix = sitePrefix;
            }

            //Fix date:
            try
            {
                Regex reg = new Regex(@"date%3A\d{4}(?:-(?:0?[1-9]|1[0-2])+)?(?:-(?:0?[1-9]|[12][0-9]|3[01])+)?", RegexOptions.IgnoreCase);
                MatchCollection mc = reg.Matches(keyWord);
                int mcs = mc.Count;

                for (int i = 0; i < mcs; i++)
                {
                    string newdate = mc[i].Value;
                    newdate = Regex.Replace(newdate, "-", ".");
                    keyWord = Regex.Replace(keyWord, mc[i].Value, newdate);
                }
            }
            catch { }

            //对获取数添加限制
            if (count >= 40)
                count = 40;

            if (!IsLoginSite)
            {
                LoadUser();
                LoginCall(new LoginSiteArgs() { User = nowUser, Pwd = nowPwd });
            }
            return booru?.GetPageString(page, count, keyWord, proxy);
        }

        public override List<Img> GetImages(string pageString, IWebProxy proxy)
        {
            return booru?.GetImages(pageString, proxy);
        }

        public override List<TagItem> GetTags(string word, IWebProxy proxy)
        {
            List<TagItem> re = new List<TagItem>();

            //https://chan.sankakucomplex.com/tag/autosuggest?tag=*****&locale=en
            string url = string.Format(SiteUrl + "/tag/autosuggest?tag={0}", word);
            shc.ContentType = SessionHeadersValue.AcceptAppJson;
            string json = Sweb.Get(url, proxy, shc);
            object[] array = (new JavaScriptSerializer()).DeserializeObject(json) as object[];

            if (array.Count() > 1)
            {
                if (array[1].GetType().FullName.Contains("Object[]"))
                {
                    int i = 2;
                    foreach (object names in array[1] as object[])
                    {
                        string name = names.ToString();
                        string count = array[i].ToString();
                        i++;
                        re.Add(new TagItem() { Name = name, Count = count });
                    }
                }
            }

            return re;
        }

        /// <summary>
        /// 登录调用
        /// </summary>
        public override void LoginCall(LoginSiteArgs loginArgs)
        {
            if (IsRunLogin || string.IsNullOrWhiteSpace(loginArgs.User) || string.IsNullOrWhiteSpace(loginArgs.Pwd))
            {
                SiteManager.ShowToastMsg("当前还未登录，需要登录后再获取", SiteManager.MsgType.Warning);
                return;
            }
            nowUser = loginArgs.User;
            nowPwd = loginArgs.Pwd;
            Login();
        }

        /// <summary>
        /// 还原Cookie
        /// </summary>
        private void CookieRestore()
        {
            if (!string.IsNullOrWhiteSpace(cookie) || sitePrefix.Contains("chan")) { return; }

            string ck = Sweb.GetURLCookies(SiteUrl);
            cookie = string.IsNullOrWhiteSpace(ck) ? string.Empty : $"iapi.sankaku;{ck}";
        }

        /// <summary>
        /// 两个子站登录方式不同
        /// chan 使用 Authorization
        /// idol 使用 Cookie
        /// </summary>
        private void Login()
        {
            IsRunLogin = true;
            IsLoginSite = false;
            string subdomain = sitePrefix.Substring(0, 1), loginhost = "https://";

            if (subdomain.Contains("c"))
            {
                //chan
                subdomain += "api-v2";
                loginhost += $"{subdomain}.sankakucomplex.com";

                try
                {
                    JObject user = new JObject
                    {
                        ["login"] = nowUser,
                        ["password"] = nowPwd
                    };
                    string post = JsonConvert.SerializeObject(user);

                    //Post登录取Authorization
                    shc.Accept = "application/vnd.sankaku.api+json;v=2";
                    shc.ContentType = SessionHeadersValue.AcceptAppJson;
                    Sweb.CookieContainer = null;

                    post = Sweb.Post(loginhost + "/auth/token", post, SiteManager.MainProxy, shc);
                    if (string.IsNullOrWhiteSpace(post) || !post.Contains("{"))
                    {
                        IsRunLogin = false;
                        nowUser = nowPwd = null;
                        string msg = $"登录失败{Environment.NewLine}{post}";
                        SiteManager.EchoErrLog(ShortName, msg, true);
                        SiteManager.ShowToastMsg(msg, SiteManager.MsgType.Warning);
                        return;
                    }

                    JObject jobj = JObject.Parse(post);
                    if (jobj.Property("token_type") != null)
                    {
                        authorization = $"{jobj["token_type"]} {jobj["access_token"]} ";
                        //在请求头中添加登录关键信息Authorization
                        shc.Add(HttpRequestHeader.Authorization, authorization);
                    }

                    if (string.IsNullOrWhiteSpace(authorization))
                    {
                        IsRunLogin = false;
                        nowUser = nowPwd = null;
                        string msg = "登录失败 - 验证账号错误";
                        SiteManager.EchoErrLog(ShortName, msg, true);
                        SiteManager.ShowToastMsg(msg, SiteManager.MsgType.Warning);
                        return;
                    }

                    pageurl = $"{loginhost}/posts?page={{0}}&limit={{1}}&tags=hide_posts_in_books:never+{{2}}";

                    //登录成功 初始化Booru类型站点
                    booru = new SiteBooru(SiteUrl, pageurl, null, SiteName, ShortName, false, BooruProcessor.SourceType.JSONcSku, shc);
                    IsLoginSite = true;
                    SaveUser();

                }
                catch (Exception e)
                {
                    nowUser = nowPwd = null;
                    string msg = $"登录失败{Environment.NewLine}{e.Message}";
                    SiteManager.EchoErrLog(ShortName, e, null, true);
                    SiteManager.ShowToastMsg(msg, SiteManager.MsgType.Error);
                }
            }
            else
            {
                //idol
                subdomain += "api";
                loginhost += $"{subdomain}.sankakucomplex.com";

                if (string.IsNullOrWhiteSpace(cookie) || !cookie.Contains($"{subdomain}.sankaku"))
                {
                    try
                    {
                        cookie = string.Empty;
                        temppass = GetSankakuPwHash(nowPwd);
                        tempappkey = GetSankakuAppkey(nowUser);

                        string post = $"login={nowUser}&password_hash={temppass}&appkey={tempappkey}";

                        //Post登录取Cookie
                        shc.UserAgent = ua;
                        shc.Accept = SessionHeadersValue.AcceptAppJson;
                        shc.ContentType = SessionHeadersValue.ContentTypeFormUrlencoded;
                        post = Sweb.Post($"{loginhost}/user/authenticate.json", post, SiteManager.MainProxy, shc);
                        cookie = Sweb.GetURLCookies(loginhost);

                        if (!cookie.Contains("sankakucomplex_session") || string.IsNullOrWhiteSpace(cookie))
                        {
                            IsRunLogin = false;
                            nowUser = nowPwd = null;
                            string msg = $"登录失败{Environment.NewLine}{post}";
                            SiteManager.EchoErrLog(ShortName, msg, true);
                            SiteManager.ShowToastMsg(msg, SiteManager.MsgType.Warning);
                            return;
                        }
                        else
                        {
                            cookie = $"{subdomain }.sankaku;{cookie}";
                            //在请求头中添加cookie
                            shc.Add(HttpRequestHeader.Cookie, cookie);
                        }

                        pageurl = $"{loginhost}/post/index.json?login={nowUser}&password_hash={temppass}" +
                            $"&appkey={tempappkey}&page={{0}}&limit={{1}}&tags={{2}}";

                        //登录成功 初始化Booru类型站点
                        booru = new SiteBooru(SiteUrl, pageurl, null, SiteName, ShortName, false, BooruProcessor.SourceType.JSONiSku, shc);
                        IsLoginSite = true;
                        SaveUser();
                    }
                    catch (Exception e)
                    {
                        nowUser = nowPwd = null;
                        string msg = $"登录失败{Environment.NewLine}{e.Message}";
                        SiteManager.EchoErrLog(ShortName, e, null, true);
                        SiteManager.ShowToastMsg(msg, SiteManager.MsgType.Error);
                    }
                }
            }
            IsRunLogin = false;
        }

        /// <summary>
        /// 保存账号
        /// </summary>
        private void SaveUser()
        {
            SiteManager.SiteConfig(ShortName, new SiteConfigArgs() { Section = "Login", Key = "User", Value = nowUser }, SiteManager.SiteConfigType.Change);
            SiteManager.SiteConfig(ShortName, new SiteConfigArgs() { Section = "Login", Key = "Pwd", Value = nowPwd }, SiteManager.SiteConfigType.Change);
        }

        /// <summary>
        /// 载入账号
        /// </summary>
        private void LoadUser()
        {
            nowUser = SiteManager.SiteConfig(ShortName, new SiteConfigArgs() { Section = "Login", Key = "User" });
            nowPwd = SiteManager.SiteConfig(ShortName, new SiteConfigArgs() { Section = "Login", Key = "Pwd" });
        }

        /// <summary>
        /// 计算用于登录等账号操作的AppKey
        /// </summary>
        /// <param name="user">用户名</param>
        /// <returns></returns>
        private static string GetSankakuAppkey(string user)
        {
            return SHA1("sankakuapp_" + user.ToLower() + "_Z5NE9YASej", Encoding.Default).ToLower();
        }

        /// <summary>
        /// 计算密码sha1
        /// </summary>
        /// <param name="password">密码</param>
        /// <returns></returns>
        private static string GetSankakuPwHash(string password)
        {
            return SHA1("choujin-steiner--" + password + "--", Encoding.Default).ToLower();
        }

        /// <summary>
        /// SHA1加密
        /// </summary>
        /// <param name="content">字符串</param>
        /// <param name="encode">编码</param>
        /// <returns></returns>
        private static string SHA1(string content, Encoding encode)
        {
            try
            {
                System.Security.Cryptography.SHA1 sha1 = new System.Security.Cryptography.SHA1CryptoServiceProvider();
                byte[] bytes_in = encode.GetBytes(content);
                byte[] bytes_out = sha1.ComputeHash(bytes_in);
                string result = BitConverter.ToString(bytes_out);
                result = result.Replace("-", string.Empty);
                return result;
            }
            catch (Exception ex)
            {
                throw new Exception("SHA1Error:" + ex.Message);
            }
        }
    }
}
