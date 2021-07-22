﻿using System.Collections.Generic;
using System.Net;

namespace MoeLoaderDelta
{
    /// <summary>
    /// 抽象图片站点
    /// </summary>
    public abstract class AbstractImageSite : IMageSite
    {
        /// <summary>
        /// 站点URL，用于打开该站点主页。eg. http://yande.re
        /// </summary>
        public abstract string SiteUrl { get; }

        /// <summary>
        /// 站点名称，用于站点列表中的显示。eg. yande.re
        /// 提示：当站点名称含有空格时，第一个空格前的字符串相同的站点将在站点列表中自动合并为一项，
        /// 例如“www.pixiv.net [User]”和“www.pixiv.net [Day]”将合并为“www.pixiv.net”
        /// </summary>
        public abstract string SiteName { get; }

        /// <summary>
        /// 站点的短名称，将作为站点的唯一标识，eg. yandere
        /// 提示：可以在程序集中加入以短名称作为文件名的ico图标（eg. yandere.ico），该图标会自动作为该站点的图标显示在站点列表中。
        /// 注意：需要选择该图标文件的 Build Action 为 Embedded Resource
        /// </summary>
        public abstract string ShortName { get; }

        /// <summary>
        /// 站点的搜索方式短名称，用于显示在下拉表标题上
        /// </summary>
        public virtual string ShortType => string.Empty;

        /// <summary>
        /// 向该站点发起请求时需要伪造的Referer，若不需要则保持null
        /// </summary>
        public virtual string Referer => null;

        /// <summary>
        /// 子站映射关键名，用于下载时判断不同于主站短域名的子站，以此返回主站的Referer,用半角逗号分隔
        /// </summary>
        public virtual string SubReferer => null;

        /// <summary>
        /// 是否支持设置单页数量，若为false则单页数量不可修改
        /// </summary>
        public virtual bool IsSupportCount => true;

        /// <summary>
        /// 是否支持评分，若为false则不可按分数过滤图片
        /// </summary>
        public virtual bool IsSupportScore => true;

        /// <summary>
        /// 是否支持分辨率，若为false则不可按分辨率过滤图片
        /// </summary>
        public virtual bool IsSupportRes => true;

        /// <summary>
        /// 是否显示分辨率，若为false则在缩略图分辨率处显示标签
        /// </summary>
        public virtual bool IsShowRes => true;

        /// <summary>
        /// 是否支持预览图，若为false则缩略图上无查看预览图的按钮
        /// </summary>
        public virtual bool IsSupportPreview => true;

        /// <summary>
        /// 是否支持搜索框自动提示，若为false则输入关键词时无自动提示
        /// </summary>
        public virtual bool IsSupportTag => true;

        /// <summary>
        /// 鼠标悬停在站点列表项上时显示的工具提示信息
        /// </summary>
        public virtual string ToolTip => null;

        /// <summary>
        /// 站点登录地址，如果有登录地址则可在主页右键菜单中登录
        /// </summary>
        public virtual string LoginURL => null;

        /// <summary>
        /// 当前站点是否已登录
        /// </summary>
        public virtual bool LoginSiteIsLogged => false;

        /// <summary>
        /// 当前登录站点的用户
        /// </summary>
        public virtual string LoginUser
        {
            get => string.IsNullOrWhiteSpace(loginUser) ? "登录站点" : loginUser;
            set => loginUser = string.IsNullOrWhiteSpace(loginUser) ? "登录站点" : value;
        }
        private static string loginUser = null;

        /// <summary>
        /// 当前登录站点的密码，Get仅用于判断是否有密码，无法取得原始密码
        /// </summary>
        public virtual string LoginPwd
        {
            get => string.IsNullOrWhiteSpace(loginPwd) ? string.Empty : "6";
            set => loginPwd = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
        }
        private static string loginPwd = null;

        /// <summary>
        /// 登录帮助链接
        /// </summary>
        public virtual string LoginHelpUrl => string.Empty;

        /// <summary>
        /// 调用登录站点方法
        /// </summary>
        /// <param name="loginArgs">登录信息</param>
        public virtual void LoginCall(LoginSiteArgs loginArgs) { throw new System.NotImplementedException(); }

        /// <summary>
        /// 该站点在站点列表中是否可见
        /// 提示：若该站点默认不希望被看到可以设为false，当满足一定条件时（例如存在某个文件）再显示
        /// </summary>
        public virtual bool IsVisible => true;

        /// <summary>
        /// 该站点的请求头
        /// </summary>
        public virtual SessionHeadersCollection SiteHeaders
        {
            get
            {
                SessionHeadersCollection shc = new SessionHeadersCollection();
                return shc;
            }

        }

        /// <summary>
        /// 站点扩展设置，用于在站点子菜单加入扩展设置选项
        /// </summary>
        public virtual List<SiteExtendedSetting> ExtendedSettings { get; set; }

        /// <summary>
        /// 获取页面的源代码，例如HTML
        /// </summary>
        /// <param name="page">页码</param>
        /// <param name="count">单页数量（可能不支持）</param>
        /// <param name="keyWord">关键词</param>
        /// <param name="proxy">全局的代理设置，进行网络操作时请使用该代理</param>
        /// <returns>页面源代码</returns>
        public abstract string GetPageString(int page, int count, string keyWord, IWebProxy proxy);

        /// <summary>
        /// 从页面源代码获取图片列表
        /// </summary>
        /// <param name="pageString">页面源代码</param>
        /// <param name="proxy">全局的代理设置，进行网络操作时请使用该代理</param>
        /// <returns>图片信息列表</returns>
        public abstract List<Img> GetImages(string pageString, IWebProxy proxy);

        /// <summary>
        /// 获取关键词自动提示列表
        /// </summary>
        /// <param name="word">关键词</param>
        /// <param name="proxy">全局的代理设置，进行网络操作时请使用该代理</param>
        /// <returns>提示列表项集合</returns>
        public virtual List<TagItem> GetTags(string word, IWebProxy proxy)
        {
            return new List<TagItem>();
        }

        #region 实现者无需关注此处代码
        /// <summary>
        /// 站点列表中显示的图标
        /// </summary>
        public virtual System.IO.Stream IconStream => GetType().Assembly.GetManifestResourceStream($"SitePack.image.{ShortName}.ico");

        /// <summary>
        /// 获取图片列表
        /// </summary>
        /// <param name="page">页码</param>
        /// <param name="count">单页数量（可能不支持）</param>
        /// <param name="keyWord">关键词</param>
        /// <param name="proxy">全局的代理设置，进行网络操作时请使用该代理</param>
        /// <returns>图片信息列表</returns>
        public virtual List<Img> GetImages(int page, int count, string keyWord, IWebProxy proxy)
        {
            return GetImages(GetPageString(page, count, keyWord, proxy), proxy);
        }

        /// <summary>
        /// 图片过滤
        /// </summary>
        /// <param name="imgs">图片集合</param>
        /// <param name="maskScore">屏蔽分数</param>
        /// <param name="maskRes">屏蔽分辨率</param>
        /// <param name="lastViewed">已浏览的图片id</param>
        /// <param name="maskViewed">屏蔽已浏览</param>
        /// <param name="showExplicit">屏蔽Explicit评级</param>
        /// <param name="updateViewed">更新已浏览列表</param>
        /// <returns></returns>
        public virtual List<Img> FilterImg(List<Img> imgs, int maskScore, int maskRes, ViewedID lastViewed, bool maskViewed, bool showExplicit, bool updateViewed)
        {
            List<Img> re = new List<Img>();
            if (imgs == null) { return re; }
            foreach (Img img in imgs)
            {
                //标记已阅
                img.IsViewed = true;
                if (lastViewed != null && !lastViewed.IsViewed(img.Id))
                {
                    img.IsViewed = false;
                    if (updateViewed)
                        lastViewed.AddViewingId(img.Id);
                }
                else if (maskViewed) continue;

                int res = img.Width * img.Height;
                //score filter & resolution filter & explicit filter
                if ((IsSupportScore && img.Score <= maskScore) || (IsSupportRes && res < maskRes) || (!showExplicit && img.IsExplicit))
                {
                    continue;
                }
                else
                {
                    re.Add(img);
                }
            }
            return re;
        }
        #endregion
    }
}
