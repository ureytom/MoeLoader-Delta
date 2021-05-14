﻿using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MoeLoaderDelta.Control
{
    /// <summary>
    /// PreviewImg.xaml 的交互逻辑
    /// by YIU
    /// Fixed 20190209
    /// </summary>
    public partial class PreviewImg : UserControl
    {

        //======== 私有变量 =========
        //预览窗口
        private PreviewWnd prew;
        //图片信息结构
        private Img img;
        //网络请求组
        private readonly SessionClient Sweb = new SessionClient(MainWindow.SecurityType);
        private readonly SessionHeadersCollection shc = new SessionHeadersCollection();
        //容错加载次数
        private int reTryCount = 0;

        //用于静态图片格式转化
        Dictionary<string, ImageFormat> imgf = new Dictionary<string, ImageFormat>();

        #region ==== 封装 =======
        public Dictionary<int, HttpWebRequest> Reqs { get; set; } = new Dictionary<int, HttpWebRequest>();

        public bool ImgLoaded { get; private set; }

        public bool isZoom { get; set; }

        /// <summary>
        /// 预览图类型
        /// bmp, jpg, png, gif, webm, mpeg, zip, rar, 7z
        /// </summary>
        public string ImgType { get; private set; }

        public Stream Strs { get; set; }
        #endregion

        //======= 委托 =======
        //进度条数值
        private delegate void ProgressBarSetter(double value);

        public PreviewImg(PreviewWnd prew, Img img)
        {
            InitializeComponent();
            this.prew = prew;
            this.img = img;
            //格式初始化
            imgf.Add("bmp", ImageFormat.Bmp);
            imgf.Add("jpg", ImageFormat.Jpeg);
            imgf.Add("png", ImageFormat.Png);
        }


        /// <summary>
        /// 下载图片
        /// </summary>
        /// <param name="reTry">重试:1~9</param>
        public void DownloadImg(string needReferer, int reTry = 0)
        {
            try
            {
                #region 创建请求数据
                reTry = (reTry < 0 ? 0 : reTry) > 9 ? 9 : reTry;
                shc.Add("Accept-Ranges", "bytes");
                shc.Referer = needReferer;
                shc.ContentType = SessionHeadersValue.ContentTypeAuto;
                shc.AcceptEncoding = SessionHeadersValue.AcceptEncodingGzip;
                shc.AutomaticDecompression = DecompressionMethods.GZip;
                string[] requrls = {
                    img.PreviewUrl,
                    img.JpegUrl,
                    img.OriginalUrl.Replace(".#ext",".jpg"),
                    img.OriginalUrl.Replace(".#ext", ".png"),
                    img.OriginalUrl.Replace(".#ext", ".gif"),
                    WebUrlEncode(img.PreviewUrl),
                    WebUrlEncode(img.JpegUrl),
                    WebUrlEncode(img.OriginalUrl.Replace(".#ext",".jpg")),
                    WebUrlEncode(img.OriginalUrl.Replace(".#ext", ".png")),
                    WebUrlEncode(img.OriginalUrl.Replace(".#ext", ".gif"))
                };

                string requrl = requrls[reTry];
                HttpWebRequest req = Sweb.CreateWebRequest(requrl, MainWindow.WebProxy, shc);

                //将请求加入请求组
                Reqs.Add(img.Id, req);
                #endregion

                //异步下载开始
                req.BeginGetResponse(new AsyncCallback(RespCallback), new KeyValuePair<int, HttpWebRequest>(img.Id, req));
            }
            catch (Exception ex)
            {
                Program.Log(ex, "Download sample failed");
                StopLoadImg(img.Id, true, "创建下载失败");
            }
        }

        /// <summary>
        /// 异步下载
        /// </summary>
        /// <param name="req"></param>
        private void RespCallback(IAsyncResult req)
        {
            KeyValuePair<int, HttpWebRequest> re = (KeyValuePair<int, HttpWebRequest>)(req.AsyncState);
            try
            {
                Dispatcher.Invoke(new UIdelegate(delegate (object sender)
                {
                    try
                    {
                        //取响应数据
                        HttpWebResponse res = (HttpWebResponse)re.Value.EndGetResponse(req);
                        string resae = res.Headers[HttpResponseHeader.ContentEncoding];
                        Stream str = res.GetResponseStream();

                        //响应长度
                        double reslength = res.ContentLength, restmplength = 0;


                        //获取数据更新进度条
                        ThreadPool.QueueUserWorkItem((obj) =>
                        {
                            //缓冲块长度
                            byte[] buffer = new byte[1024];
                            //读到的字节长度
                            int realReadLen = str.Read(buffer, 0, 1024);
                            //进度条字节进度
                            long progressBarValue = 0;
                            double progressSetValue = 0;
                            //内存流字节组
                            byte[] data = null;
                            MemoryStream ms = new MemoryStream();

                            //写流数据并更新进度条
                            while (realReadLen > 0)
                            {
                                ms.Write(buffer, 0, realReadLen);
                                progressBarValue += realReadLen;
                                if (reslength < 1)
                                {
                                    if (restmplength < progressBarValue)
                                        restmplength = progressBarValue * 2;
                                    progressSetValue = progressBarValue / restmplength;
                                }
                                else
                                { progressSetValue = progressBarValue / reslength; }

                                pdload.Dispatcher.BeginInvoke(new ProgressBarSetter(SetProgressBar), progressSetValue);
                                try
                                {
                                    realReadLen = str.Read(buffer, 0, 1024);
                                }
                                catch
                                {
                                    Dispatcher.Invoke(new UIdelegate(delegate (object sende) { StopLoadImg(re.Key, "数据中止"); }), string.Empty);
                                    return;
                                }
                            }

                            data = ms.ToArray();

                            //解压gzip
                            if (resae != null && resae.Contains("gzip", StringComparison.OrdinalIgnoreCase))
                            {
                                ungzip(ref data);
                            }

                            //将字节组转为流
                            ms = new MemoryStream(data);

                            //读完数据传递图片流并显示
                            Dispatcher.Invoke(new UIdelegate(delegate (object sende) { AssignImg(ms, re.Key); }), string.Empty);

                            str.Dispose();
                            str.Close();
                        }, null);
                    }
                    catch (WebException e)
                    {
                        reTryCount++;
                        if (reTryCount > 0 && reTryCount < 10)
                        {
                            if (req != null)
                            {
                                req.AsyncWaitHandle.Close();
                            }

                            StopLoadImg(re.Key, false, $"容错加载{reTryCount}次");
                            DownloadImg(re.Value.Referer, reTryCount);
                        }
                        else
                            Dispatcher.Invoke(new UIdelegate(delegate (object sende) { StopLoadImg(re.Key, true, "缓冲失败"); }), e);
                    }
                    finally
                    {
                        if (req != null)
                            req.AsyncWaitHandle.Close();
                    }
                }), this);
            }
            catch (Exception ex2)
            {
                Program.Log(ex2, "Download sample failed");
                Dispatcher.Invoke(new UIdelegate(delegate (object sender) { StopLoadImg(re.Key, true, "下载失败"); }), ex2);
            }
        }

        /// <summary>
        /// 解压gzip
        /// </summary>
        /// <param name="data">字节组</param>
        /// <returns></returns>
        private static byte[] ungzip(ref byte[] data)
        {
            try
            {
                MemoryStream js = new MemoryStream();                       // 解压后的流   
                MemoryStream ms = new MemoryStream(data);                   // 用于解压的流   
                GZipStream g = new GZipStream(ms, CompressionMode.Decompress);
                byte[] buffer = new byte[5120];                                // 5K缓冲区      
                int l = g.Read(buffer, 0, 5120);
                while (l > 0)
                {
                    js.Write(buffer, 0, l);
                    l = g.Read(buffer, 0, 5120);
                }
                data = js.ToArray();
                g.Dispose();
                ms.Dispose();
                js.Dispose();
                g.Close();
                ms.Close();
                js.Close();
                return data;
            }
            catch
            {
                return data;
            }
        }

        /// <summary>
        /// 设进度条进度值
        /// </summary>
        /// <param name="value"></param>
        private void SetProgressBar(double value)
        {
            pdload.Value = value;
        }

        #region 停止加载预览图
        /// <summary>
        /// 停止加载
        /// </summary>
        /// <param name="id"></param>
        /// <param name="Failed">是否失败</param>
        /// <param name="FMsg">失败提示</param>
        public void StopLoadImg(int id, bool Failed, string FMsg)
        {
            try
            {
                //清理请求数据
                if (Reqs.ContainsKey(id))
                {
                    if (Reqs[id] != null)
                    {
                        Reqs[id].Abort();
                        Reqs.Remove(id);
                    }
                }

                if (Strs != null)
                {
                    Strs.Flush();
                    Strs.Dispose();
                    Strs.Close();
                }
            }
            catch { }
            finally
            {
                pdtext.Text = FMsg;
                pdload.BorderBrush = Failed
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(238, 255, 55, 90))
                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(238, 255, 210, 0));
                pdload.Background = Failed
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(238, 255, 197, 197))
                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(238, 255, 237, 192));
            }
        }
        /// <summary>
        /// 停止加载2
        /// </summary>
        /// <param name="id"></param>
        /// <param name="FMsg">失败提示</param>
        public void StopLoadImg(int id, string FMsg)
        {
            StopLoadImg(id, false, FMsg);
        }
        #endregion

        /// <summary>
        /// 显示预览
        /// </summary>
        /// <param name="str">各种Stream</param>
        /// <param name="key">img.ID</param>
        private void AssignImg(Stream str, int key)
        {
            //流下载完毕
            try
            {
                //提取图片规格类型
                string type = GetImgType(str);
                ImgType = type;

                //记录Stream
                Strs = str;

                //分显示容器
                switch (type)
                {
                    case "bmp":
                    case "jpg":
                    case "png":
                        //静态图片格式转化
                        System.Drawing.Image dimg = System.Drawing.Image.FromStream(str);
                        MemoryStream ms = new MemoryStream();
                        dimg.Save(ms, imgf[type]);
                        BitmapImage bi = new BitmapImage();
                        bi.BeginInit();
                        bi.StreamSource = new MemoryStream(ms.ToArray()); //ms不能直接用
                        bi.EndInit();
                        ms.Close();

                        //创建静态图片控件
                        Image img = new Image()
                        {
                            Source = bi,
                            Stretch = Stretch.Uniform,
                            SnapsToDevicePixels = true,
                            StretchDirection = StretchDirection.Both,
                            Margin = new Thickness() { Top = 0, Right = 0, Bottom = 0, Left = 0 }
                        };

                        //将预览控件加入布局
                        prewimg.Children.Add(img);
                        break;

                    case "gif":
                        //创建GIF动图控件
                        AnimatedGIF gif = new AnimatedGIF()
                        {
                            GIFSource = str,
                            Stretch = Stretch.Uniform,
                            SnapsToDevicePixels = true,
                            StretchDirection = StretchDirection.Both
                        };

                        //将预览控件加入布局
                        prewimg.Children.Add(gif);
                        break;

                    default:
                        //未支持预览的格式
                        Dispatcher.Invoke(new UIdelegate(delegate (object ss) { StopLoadImg(key, "不支持" + type); }), string.Empty);
                        return;
                }

                //选中的预览图显示出来
                if (key == prew.SelectedId)
                {
                    Visibility = Visibility.Visible;
                }

                //隐藏进度条
                ProgressPlate.Visibility = Visibility.Hidden;

                ImgZoom(true);
                ImgLoaded = true;
            }
            catch (Exception ex1)
            {
                Program.Log(ex1, "Read sample img failed");
                Dispatcher.Invoke(new UIdelegate(delegate (object ss) { StopLoadImg(key, true, "读取数据失败"); }), ex1);
            }
        }

        #region 设置图片缩放
        /// <summary>
        /// 设置图片缩放
        /// </summary>
        /// <param name="zoom">true自适应</param>
        /// <param name="begin">首次显示</param>
        public void ImgZoom(bool zoom, bool begin)
        {
            if (ImgType == null) return;

            double imgw = 0, imgh = 0;
            AnimatedGIF gifo = null;
            bool isani = false;

            UIElement imgui = prewimg.Children[0];
            //分类型取值
            switch (ImgType)
            {
                case "bmp":
                case "jpg":
                case "png":
                    Image img = (Image)imgui;
                    BitmapImage bi = (BitmapImage)img.Source;
                    imgw = bi.PixelWidth;
                    imgh = bi.PixelHeight;
                    break;
                case "gif":
                    AnimatedGIF gif = (AnimatedGIF)imgui;
                    gifo = gif;
                    isani = true;
                    break;
            }

            if (begin)
            {
                if (isani)
                {
                    imgw = prew.Descs[prew.SelectedId].Width;
                    imgh = prew.Descs[prew.SelectedId].Height;
                    if (imgw < 1 || imgh < 1)
                        imgw = imgh = double.NaN;
                }
                if (zoom && imgw > prew.imgGrid.ActualWidth || zoom && imgh > prew.imgGrid.ActualHeight)
                {
                    Width = Height = double.NaN;
                }
                else
                {
                    Width = imgw;
                    Height = imgh;
                    zoom = false;
                }
            }
            else
            {
                if (zoom)
                {
                    Width = Height = double.NaN;
                }
                else if (isani)
                {
                    AnimatedGIF.GetWidthHeight(gifo, ref imgw, ref imgh);
                    Width = imgw;
                    Height = imgh;
                }
                else
                {
                    Width = imgw;
                    Height = imgh;
                }
            }

            isZoom = zoom;
        }

        /// <summary>
        /// 设置图片缩放首次模式自适应
        /// </summary>
        public void ImgZoom(bool begin) { ImgZoom(true, begin); }

        /// <summary>
        /// 设置图片缩放到自适应
        /// </summary>
        public void ImgZoom() { ImgZoom(true, false); }
        #endregion

        /// <summary>
        /// 简单的获取图片类型，失败返回空
        /// </summary>
        /// <param name="str">Stream</param>
        /// <returns>bmp,jpg,png,gif,webm,mpeg,zip,rar,7z</returns>
        private string GetImgType(Stream str)
        {
            if (str == null) return string.Empty;

            //由自带对象判断类型
            ImageFormat dwimgformat = System.Drawing.Image.FromStream(str).RawFormat;
            if (dwimgformat.Equals(ImageFormat.Bmp))
            {
                return "bmp";
            }
            else if (dwimgformat.Equals(ImageFormat.Jpeg)
               || dwimgformat.Equals(ImageFormat.Exif))
            {
                return "jpg";
            }
            else if (dwimgformat.Equals(ImageFormat.Png))
            {
                return "png";
            }
            else if (dwimgformat.Equals(ImageFormat.Gif))
            {
                return "gif";
            }

            //如果对象无法判断就取文件头字节判断
            //图片类型特征字节
            Dictionary<string, string> itype = new Dictionary<string, string>();
            itype.Add("bmp", "424D");
            itype.Add("jpg", "FFD8");
            itype.Add("png", "89504E470D0A");
            itype.Add("gif", "47494638");
            itype.Add("webm", "1A45DFA3");
            itype.Add("mpeg", "66747970");
            itype.Add("zip", "504B0304");
            itype.Add("rar", "52617221");
            itype.Add("7z", "377ABCAF271C");

            //取数据头一部分
            byte[] head = DataConverter.LocalStreamToByte(str, 32);
            //找出符合的格式
            foreach (string type in itype.Keys)
            {
                if (DataHelpers.SearchBytes(head, DataConverter.strHexToByte(itype[type])) >= 0)
                {
                    return type;
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// 常规化的URL编码
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string WebUrlEncode(string str)
        {
            return !string.IsNullOrEmpty(str) ?
                WebUtility.UrlEncode(str).Replace("+", "%20")
                .Replace("*", "%2A")
                .Replace("%7E", "~")
                .Replace("'", "%27")
                .Replace("(", "%28")
                .Replace(")", "%29")
                .Replace("%3A", ":")
                .Replace("%2F", "/")
                .Replace("%23", "#")
                : str;
        }

    }
}
