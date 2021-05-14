﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static MoeLoaderDelta.Control.Toast.ToastBoxNotification;

namespace MoeLoaderDelta
{
    public enum DLWorkMode { Retry, Stop, Remove, Del, RetryAll, AutoRetryAll, StopAll, RemoveAll }
    public delegate void DownloadHandler(long size, double percent, string url, double speed);

    public struct MiniDownloadItem
    {
        public string url;
        public string fileName;
        public string host;
        public string author;
        public string localName;
        public string localfileName;
        public int id;
        public bool noVerify;
        public string searchWord;
        //当前下载对象的站点接口检索号
        public int siteIntfcIndex;
        public MiniDownloadItem(string file, string url, string host, string author, string localName, string localfileName, int id, bool noVerify, int siteIntfcIndex)
        {
            //原始后缀名
            string ext = string.Empty;

            if (file != null && file != string.Empty)
            {
                ext = url.Substring(url.LastIndexOf('.'), url.Length - url.LastIndexOf('.'));
                fileName = file.EndsWith(ext) ? file : file + ext;
            }
            else
            {
                fileName = file;
            }

            this.url = url;
            this.host = host;
            this.author = author;
            this.localName = localName.EndsWith(ext) ? localName
                : localName.IsNullOrEmptyOrWhiteSpace() ? fileName : localName + ext;
            this.localfileName = localfileName.EndsWith(ext) ? localfileName
                : localfileName.IsNullOrEmptyOrWhiteSpace() ? fileName : localfileName + ext;
            this.id = id;
            this.noVerify = noVerify;
            this.searchWord = MainWindow.SearchWordPu;
            this.siteIntfcIndex = siteIntfcIndex;
        }
    }

    /// <summary>
    /// Interaction logic for DownloadControl.xaml
    /// 下载面板用户控件
    /// </summary>
    public partial class DownloadControl : UserControl
    {
        public ScrollViewer Scrollviewer => (ScrollViewer)dlList.Template.FindName("dlListSView", dlList);

        public const string DLEXT = ".moe";
        private const string dlerrtxt = "下载失败下载未完成";

        private bool isMouseSelect = false;

        //一个下载任务
        private class DownloadTask
        {
            public string Url { get; set; }
            public string SaveLocation { set; get; }
            public bool IsStop { set; get; }
            public string NeedReferer { get; set; }
            public bool NoVerify { get; set; }
            public SessionHeadersCollection SiteHeaders { get; set; }

            /// <summary>
            /// 下载任务
            /// </summary>
            /// <param name="url">目标地址</param>
            /// <param name="saveLocation">保存位置</param>
            /// <param name="referer">是否需要伪造Referer</param>
            /// <param name="shc">指定请求头</param>
            public DownloadTask(string url, string saveLocation, string referer, bool noVerify, SessionHeadersCollection shc)
            {
                SaveLocation = saveLocation;
                Url = url;
                NeedReferer = referer;
                NoVerify = noVerify;
                IsStop = false;
                SiteHeaders = shc;
            }
        }
        public ObservableCollection<DownloadItem> DownloadItems { get; } = new ObservableCollection<DownloadItem>();

        //downloadItems的副本，用于快速查找
        private Dictionary<string, DownloadItem> downloadItemsDic = new Dictionary<string, DownloadItem>();
        /// <summary>
        /// 是否正在下载
        /// </summary>
        public bool IsWorking { get; private set; } = false;

        private int numOnce;
        /// <summary>
        /// 同时下载的任务数量
        /// </summary>
        public int NumOnce
        {
            set
            {
                if (value > 20) value = 20;
                else if (value < 1) value = 1;

                numOnce = value;
                //SetNum(value);
            }
            get { return numOnce; }
        }
        /// <summary>
        /// 重试次数
        /// </summary>
        private int retryCount;
        /// <summary>
        /// 重试计时器
        /// </summary>
        private Timer retryTimer;
        /// <summary>
        /// 分站点存放
        /// </summary>
        public bool IsSepSave { get; set; }
        /// <summary>
        /// 分上传者存放
        /// </summary>
        public bool IsSaSave { get; set; }
        /// <summary>
        /// 分搜索标签存放
        /// </summary>
        public bool IsSscSave { get; set; }
        /// <summary>
        /// 下载的保存位置
        /// </summary>
        public static string SaveLocation { get; set; } = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        /// <summary>
        /// 已保存数量、剩余保存数量
        /// </summary>
        private int NumSaved = 0, NumLeft = 0;
        /// <summary>
        /// 错误数量
        /// </summary>
        public int NumFail { get; private set; } = 0;

        //正在下载的链接
        private Dictionary<string, DownloadTask> webs = new Dictionary<string, DownloadTask>();

        public DownloadControl()
        {
            InitializeComponent();

            NumOnce = 2;
            IsSepSave = IsSscSave = IsSaSave = false;

            downloadStatus.Text = "当前无下载任务";

            dlList.DataContext = this;
            ResetRetryCount();
        }

        /// <summary>
        /// 重置重试次数
        /// </summary>
        public void ResetRetryCount()
        {
            retryCount = 1;
        }

        /// <summary>
        /// 添加下载任务
        /// </summary>
        /// <param name="items">下载物</param>
        /// <param name="dLWork">模式</param>
        public void AddDownload(IEnumerable<MiniDownloadItem> items, DLWorkMode dLWork)
        {
            foreach (MiniDownloadItem item in items)
            {
                string fileName = item.fileName;
                if (fileName == null || fileName.Trim().Length == 0)
                    fileName = Uri.UnescapeDataString(item.url.Substring(item.url.LastIndexOf('/') + 1));

                try
                {
                    /*  if(downloadItemsDic.ContainsKey(item.url))
                      {
                          downloadItemsDic.Remove(item.url);
                      }*/
                    DownloadItem itm = new DownloadItem(
                        fileName, item.url, item.host, item.author, item.localName, 
                        item.localfileName, item.id, item.noVerify, item.searchWord, item.siteIntfcIndex
                        );
                    downloadItemsDic.Add(item.url, itm);
                    DownloadItems.Add(itm);
                    NumLeft++;
                }
                catch (ArgumentException) { }//duplicate entry
            }

            if (!IsWorking)
            {
                IsWorking = true;
            }

            RefreshList();
            if (dLWork != DLWorkMode.AutoRetryAll)
            {
                ResetRetryCount();
            }
        }
        /// <summary>
        /// 添加下载通用
        /// </summary>
        /// <param name="items">下载物</param>
        /// <param name="site">站点接口</param>
        public void AddDownload(IEnumerable<MiniDownloadItem> items)
        {
            AddDownload(items, DLWorkMode.Retry);
        }

        /// <summary>
        /// 取本地保存目录
        /// </summary>
        /// <param name="dlitem">下载项</param>
        /// <returns></returns>
        private string GetLocalPath(DownloadItem dlitem)
        {
            string sPath;
            if (!dlitem.LocalName.IsNullOrEmptyOrWhiteSpace() && dlitem.LocalName.Contains("\\"))
            {
                sPath = dlitem.LocalName.Substring(0, dlitem.LocalName.LastIndexOf("\\") + 1);
            }
            else
            {
                sPath = SaveLocation
                    + (IsSepSave ? "\\" + dlitem.Host : "")
                   + (IsSscSave && !dlitem.SearchWord.IsNullOrEmptyOrWhiteSpace() ? "\\" + dlitem.SearchWord : "")
                   + (IsSaSave ? "\\" + ReplaceInvalidPathChars(dlitem.Author) : "")
                   + "\\";

                //Pixiv站动图，每个动图投稿都用ID号建一个文件夹装好
                if (dlitem.Host == "pixiv" &&
                    !dlitem.Url.IsNullOrEmptyOrWhiteSpace() &&
                    dlitem.Url.Contains("_ugoira"))
                    sPath = SaveLocation + "\\" + dlitem.Id + "\\";

                if (!Directory.Exists(sPath))
                    Directory.CreateDirectory(sPath);
            }
            return sPath;
        }

        /// <summary>
        /// 刷新下载状态
        /// </summary>
        private void RefreshList()
        {
            TotalProgressChanged();

            //根据numOnce及正在下载的情况生成下载
            int downloadingCount = NumOnce - webs.Count;
            for (int j = 0; j < downloadingCount; j++)
            {
                if (NumLeft > 0)
                {
                    DownloadItem dlitem = DownloadItems[DownloadItems.Count - NumLeft];

                    bool fileExists = false;
                    string url = dlitem.Url,
                                file = dlitem.FileName.Replace("\r\n", string.Empty),
                                path = GetLocalPath(dlitem),
                                errTip = "路径太长";


                    //检查目录长度
                    if (path.Length > 246)
                    {
                        DownloadItems[DownloadItems.Count - NumLeft].StatusE = DLStatus.Failed;
                        DownloadItems[DownloadItems.Count - NumLeft].Size = errTip;
                        WriteErrText($"{url}: {errTip}");
                        j--;
                    }
                    else
                    {
                        dlitem.LocalFileName = ReplaceInvalidPathChars(file, path, 0);
                        if (dlitem.LocalFileName.IsNullOrEmptyOrWhiteSpace())
                        {
                            DownloadItems[DownloadItems.Count - NumLeft].StatusE = DLStatus.Failed;
                            DownloadItems[DownloadItems.Count - NumLeft].Size = $"{errTip}或文件名格式错误";
                            WriteErrText($"{url}: {errTip}或文件名格式错误");
                            j--;
                        }
                        else
                        {
                            file = dlitem.LocalName = path + dlitem.LocalFileName;

                            //检查全路径长度
                            if (file.Length > 256)
                            {
                                DownloadItems[DownloadItems.Count - NumLeft].StatusE = DLStatus.Failed;
                                DownloadItems[DownloadItems.Count - NumLeft].Size = errTip;
                                WriteErrText($"{url}: {errTip}");
                                j--;
                            }
                        }
                    }

                    #region --- deleted标签的图片文件是否已存在 ---
                    if (dlitem.Url.Contains("#ext") && dlitem.LocalFileName.Contains("deleted"))
                    {
                        int lastind;
                        string oFileName = file;
                        string filename = string.Empty;
                        string[] exts = { "png", "gif", "webm" };

                        file = file.Replace(".#ext", ".jpg");
                        foreach (string ext in exts)
                        {

                            if (File.Exists(file))
                            {
                                DownloadItems[DownloadItems.Count - NumLeft].StatusE = DLStatus.IsHave;
                                DownloadItems[DownloadItems.Count - NumLeft].Size = "已存在跳过";
                                j--;
                                fileExists = true;
                                break;
                            }
                            else
                            {
                                lastind = file.LastIndexOf('.');
                                filename = file.Substring(0, lastind < 0 ? file.Length : lastind);
                                file = $"{filename}.{ext}";
                            }
                        }
                        file = oFileName;

                    }
                    else if (File.Exists(file))
                    {
                        DownloadItems[DownloadItems.Count - NumLeft].StatusE = DLStatus.IsHave;
                        DownloadItems[DownloadItems.Count - NumLeft].Size = "已存在跳过";
                        j--;
                        fileExists = true;
                    }
                    #endregion --- deleted标签的图片文件是否已存在 ---

                    if (!fileExists)
                    {
                        if (!Directory.Exists(path))
                            Directory.CreateDirectory(path);

                        DownloadItems[DownloadItems.Count - NumLeft].StatusE = DLStatus.DLing;

                        SessionHeadersCollection shc = new SessionHeadersCollection();
                        shc = SiteManager.Instance.Sites[dlitem.SiteIntfcIndex].SiteHeaders;
                        DownloadTask task = new DownloadTask(url, file, MainWindow.IsNeedReferer(url), dlitem.NoVerify, shc);
                        webs.Add(url, task);

                        //异步下载开始
                        Thread thread = new Thread(new ParameterizedThreadStart(Download));
                        thread.Start(task);
                    }


                    NumLeft = NumLeft > 0 ? --NumLeft : 0;
                }
                else break;
            }
            RefreshStatus();
        }

        /// <summary>
        /// 下载，另一线程
        /// </summary>
        /// <param name="o"></param>
        private void Download(object o)
        {
            DownloadTask task = (DownloadTask)o;
            FileStream fs = null;
            Stream str = null;
            SessionHeadersCollection shc = new SessionHeadersCollection();
            shc = task.SiteHeaders;
            SessionClient sc = new SessionClient(MainWindow.SecurityType);
            System.Net.WebResponse res = null;
            double downed = 0;
            DownloadItem item = downloadItemsDic[task.Url];

            try
            {
                string oTaskUrl = task.Url;

                #region --- 验证deleted标签文件是否可获取 ---
                if (task.Url.Contains("#ext") && task.SaveLocation.Contains("deleted"))
                {
                    int lastind;
                    string filename = string.Empty;
                    shc.Referer = task.NeedReferer;
                    string[] exts = { "png", "gif", "webm" };
                    task.Url = task.Url.Replace(".#ext", ".jpg");
                    task.SaveLocation = task.SaveLocation.Replace(".#ext", ".jpg");
                    item.LocalFileName = item.LocalFileName.Replace(".#ext", ".jpg");

                    foreach (string ext in exts)
                    {
                        if (!sc.IsExist(task.Url, MainWindow.WebProxy, shc))
                        {
                            lastind = task.Url.LastIndexOf('.');
                            filename = task.Url.Substring(0, lastind < 0 ? task.Url.Length : lastind);
                            task.Url = $"{filename}.{ext}";

                            lastind = task.SaveLocation.LastIndexOf('.');
                            filename = task.SaveLocation.Substring(0, lastind < 0 ? task.SaveLocation.Length : lastind);
                            task.SaveLocation = $"{filename}.{ext}";
                            item.LocalName = task.SaveLocation;

                            lastind = item.LocalFileName.LastIndexOf('.');
                            filename = item.LocalFileName.Substring(0, lastind < 0 ? task.Url.Length : lastind);
                            item.LocalFileName = $"{filename}.{ext}";
                        }
                        else { break; }
                    }
                }
                #endregion --- 验证deleted标签文件是否可获取 ---

                res = sc.GetWebResponse(
                    task.Url,
                    MainWindow.WebProxy,
                    task.NeedReferer
                    );

                //- 恢复Key
                task.Url = oTaskUrl;

                /////////开始写入文件
                str = res.GetResponseStream();
                byte[] bytes = new byte[5120];
                fs = new FileStream(task.SaveLocation + DLEXT, FileMode.Create, FileAccess.Write, FileShare.Delete);

                int bytesReceived = 0;
                DateTime last = DateTime.Now;
                int osize = str.Read(bytes, 0, bytes.Length);
                downed = osize;
                while (!task.IsStop && osize > 0)
                {
                    fs.Write(bytes, 0, osize);
                    bytesReceived += osize;
                    DateTime now = DateTime.Now;
                    double speed = -1;
                    if ((now - last).TotalSeconds > 0.6)
                    {
                        speed = downed / (now - last).TotalSeconds / 1024.0;
                        downed = 0;
                        last = now;
                    }
                    Dispatcher.Invoke(new DownloadHandler(web_DownloadProgressChanged),
                        res.ContentLength, bytesReceived / (double)res.ContentLength * 100.0, task.Url, speed);
                    osize = str.Read(bytes, 0, bytes.Length);
                    downed += osize;
                }
            }
            catch (Exception ex)
            {
                //Dispatcher.Invoke(new UIdelegate(delegate(object sender) { StopLoadImg(re.Key, re.Value); }), "");
                task.IsStop = true;
                Dispatcher.Invoke(new VoidDel(delegate ()
                {
                    //下载失败
                    if (downloadItemsDic.ContainsKey(task.Url))
                    {
                        NumFail++;
                        item.StatusE = DLStatus.Failed;
                        item.Size = "下载失败";
                        WriteErrText(task.Url);
                        WriteErrText(task.SaveLocation);
                        WriteErrText(ex.Message + "\r\n");

                        try
                        {
                            if (fs != null)
                                fs.Close();
                            if (str != null)
                                str.Close();
                            if (res != null)
                                res.Close();

                            File.Delete(task.SaveLocation + DLEXT);
                            DelDLItemNullDirector(item);
                        }
                        catch { }
                    }
                }));
            }
            finally
            {
                if (fs != null)
                    fs.Close();
                if (str != null)
                    str.Close();
                if (res != null)
                    res.Close();
            }

            if (task.IsStop)
            {
                //任务被取消
                Dispatcher.Invoke(new VoidDel(delegate ()
                {
                    if (downloadItemsDic.ContainsKey(task.Url))
                    {
                        if (!dlerrtxt.Contains(item.Size))
                        {
                            item.StatusE = DLStatus.Cancel;
                        }
                    }
                }));

                try
                {
                    if (fs != null) { fs.Close(); }
                    File.Delete(task.SaveLocation + DLEXT);
                    DelDLItemNullDirector(item);
                }
                catch { }
            }
            else
            {
                //下载成功完成
                Dispatcher.Invoke(new VoidDel(delegate ()
                {
                    try
                    {
                        //DownloadTask task1 = obj as DownloadTask;

                        //判断完整性
                        if (!item.NoVerify && 100 - item.Progress > 0.001)
                        {
                            task.IsStop = true;
                            item.StatusE = DLStatus.Failed;
                            item.Size = "下载未完成";
                            NumFail++;
                            try
                            {
                                if (fs != null) { fs.Close(); }
                                File.Delete(task.SaveLocation + DLEXT);
                                DelDLItemNullDirector(item);
                            }
                            catch { }
                        }
                        else
                        {
                            //修改后缀名
                            if (fs != null) { fs.Close(); }
                            File.Move(task.SaveLocation + DLEXT, task.SaveLocation);

                            item.Progress = 100.0;
                            item.StatusE = DLStatus.Success;
                            //downloadItemsDic[task.Url].Size = (downed > 1048576
                            //? (downed / 1048576.0).ToString("0.00MB")
                            //: (downed / 1024.0).ToString("0.00KB"));
                            NumSaved++;
                        }
                    }
                    catch { }
                }));
            }

            //下载结束
            Dispatcher.Invoke(new VoidDel(delegate ()
            {
                webs.Remove(task.Url);
                RefreshList();
            }));
        }

        private void WriteErrText(string content)
        {
            try
            {
                File.AppendAllText(SaveLocation + "\\moedl_error.log",
                    $"{DateTime.Now.ToSafeString()}{Environment.NewLine}{content}{Environment.NewLine}");
            }
            catch { }
        }


        /// <summary>
        /// 更新状态显示
        /// </summary>
        private void RefreshStatus()
        {
            if (webs.Count > 0)
            {
                downloadStatus.Text = "已保存 " + NumSaved + " 剩余 " + NumLeft + " 正在下载 " + webs.Count;
            }
            else
            {
                IsWorking = false;
                downloadStatus.Text = "已保存 " + NumSaved + " 剩余 " + NumLeft + " 下载完毕 ";

                // 9秒后执行重试、9秒内则重置重试时间
                if (NumFail > 0 && retryCount > 0)
                {
                    if (retryTimer == null)
                    {
                        retryTimer = new Timer(new TimerCallback(RetryRuntime), null, 9000, Timeout.Infinite);
                    }
                    else
                    {
                        retryTimer.Change(9000, Timeout.Infinite);
                    }
                }
            }

            blkTip.Visibility = DownloadItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 重试失败项计时处理
        /// </summary>
        private void RetryRuntime(object state)
        {
            retryCount--;
            if (DownloadItems.Count > 0)
            {
                Dispatcher.Invoke(new VoidDel(delegate () { ExecuteDownloadListTask(DLWorkMode.AutoRetryAll); }));
            }
            GC.Collect(2, GCCollectionMode.Optimized);
        }

        /// <summary>
        /// 下载进度发生改变
        /// </summary>
        /// <param name="total"></param>
        /// <param name="percent"></param>
        /// <param name="url"></param>
        void web_DownloadProgressChanged(long total, double percent, string url, double speed)
        {
            try
            {
                string size = total > 1048576 ? (total / 1048576.0).ToString("0.00MB") : (total / 1024.0).ToString("0.00KB");
                downloadItemsDic[url].Size = size;
                downloadItemsDic[url].Progress = percent > 100 ? 100 : percent;
                if (speed > 0)
                    downloadItemsDic[url].SetSpeed(speed);
            }
            catch { }
        }

        /// <summary>
        /// 总下载进度，根据下载完成的图片数量计算
        /// </summary>
        private void TotalProgressChanged()
        {
            if (DownloadItems.Count > 0)
            {
                double percent = (DownloadItems.Count - NumLeft - webs.Count) / (double)DownloadItems.Count * 100.0;

                Win7TaskBar.ChangeProcessValue(MainWindow.Hwnd, (uint)percent);

                if (Math.Abs(percent - 100.0) < 0.001)
                {
                    Win7TaskBar.StopProcess(MainWindow.Hwnd);
                    if (GlassHelper.GetForegroundWindow() != MainWindow.Hwnd)
                    {
                        //System.Media.SystemSounds.Beep.Play();
                        GlassHelper.FlashWindow(MainWindow.Hwnd, true);
                    }

                    #region 关机
                    if (itmAutoClose.IsChecked)
                    {
                        //关机
                        System.Timers.Timer timer = new System.Timers.Timer()
                        {
                            //20秒后关闭
                            Interval = 20000,
                            Enabled = false,
                            AutoReset = false
                        };
                        timer.Elapsed += delegate { GlassHelper.ExitWindows(GlassHelper.ShutdownType.PowerOff); };
                        timer.Start();

                        if (MessageBox.Show("系统将于20秒后自动关闭，若要取消请点击确定", MainWindow.ProgramName, MessageBoxButton.OK, MessageBoxImage.Information) == MessageBoxResult.OK)
                        {
                            timer.Stop();
                        }
                    }
                    #endregion
                }
            }
            else
            {
                Win7TaskBar.ChangeProcessValue(MainWindow.Hwnd, 0);
                Win7TaskBar.StopProcess(MainWindow.Hwnd);
            }
        }

        /// <summary>
        /// 去掉文件名中的无效字符,如 \ / : * ? " < > | 
        /// </summary>
        /// <param name="file">待处理的文件名</param>
        /// <param name="replace">替换字符</param>
        /// <returns>处理后的文件名</returns>
        public static string ReplaceInvalidPathChars(string file, string replace)
        {
            if (file.IndexOf('?', (file.LastIndexOf('.') < 1 ? file.Length : file.LastIndexOf('.'))) > 0)
            {
                //adfadsf.jpg?adfsdf   remove trailing ?param
                file = file.Substring(0, file.IndexOf('?'));
            }

            foreach (char rInvalidChar in Path.GetInvalidFileNameChars())
                file = file.Replace(rInvalidChar.ToSafeString(), replace);
            return file;
        }
        /// <summary>
        /// 去掉文件名中的无效字符,如 \ / : * ? " < > | 
        /// </summary>
        public static string ReplaceInvalidPathChars(string file)
        {
            return ReplaceInvalidPathChars(file, "_");
        }
        /// <summary>
        /// 去掉文件名中无效字符的同时裁剪过长文件名
        /// </summary>
        /// <param name="file">文件名</param>
        /// <param name="path">所在路径</param>
        /// <param name="any">任何数</param>
        /// <returns></returns>
        public static string ReplaceInvalidPathChars(string file, string path, int any)
        {
            if (path.Length + file.Length > 256 && file.Contains("<!<"))
            {
                string last = file.Substring(file.LastIndexOf("<!<"));
                int endl = 256 - path.Length - last.Length;
                file = file.Substring(0, endl > 0 ? endl : 0);

                if (file.Length > 0)
                {
                    endl = file.LastIndexOf(' ');
                    file = file.Substring(0, endl > 0 ? endl : file.Length);
                    file += last;
                }
                else if (last.Length + endl > 0)
                {
                    file += last.Substring(0, last.Length + endl);
                }
                else
                {
                    file = string.Empty;
                }
            }
            file = file.Replace("<!<", string.Empty);
            return ReplaceInvalidPathChars(file);
        }

        /// <summary>
        /// 导出lst
        /// </summary>
        private void itmLst_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Forms.SaveFileDialog saveFileDialog1 = new System.Windows.Forms.SaveFileDialog()
                {
                    DefaultExt = "lst",
                    FileName = "MoeLoaderList.lst",
                    Filter = "lst文件|*.lst",
                    OverwritePrompt = false
                };
                if (saveFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string text = "";
                    int success = 0, repeat = 0;
                    //读存在的lst内容
                    string[] flst = null;
                    bool havelst = File.Exists(saveFileDialog1.FileName);
                    bool isexists = false;

                    if (havelst)
                    {
                        flst = File.ReadAllLines(saveFileDialog1.FileName);
                    }

                    foreach (DownloadItem i in dlList.SelectedItems)
                    {
                        //查找重复项
                        try
                        {
                            isexists = havelst && flst.Any(x => x.Split('|')[2] == i.Host && x.Split('|')[4] == i.Id.ToSafeString());
                        }
                        catch { }

                        if (!isexists)
                        {
                            //url|文件名|域名|上传者|ID(用于判断重复)|免文件校验|下载对象的站点接口检索号
                            text += i.Url
                                + "|" + i.LocalFileName
                                + "|" + i.Host
                                + "|" + i.Author
                                + "|" + i.Id
                                + "|" + (i.NoVerify ? 'v' : 'x')
                                + "|" + i.SearchWord
                                + "|" + i.SiteIntfcIndex
                                + "\r\n";
                            success++;
                        }
                        else
                            repeat++;
                    }
                    File.AppendAllText(saveFileDialog1.FileName, text);
                    MainWindow.MainW.Toast.Show("成功保存 " + success + " 个地址\r\n" + repeat + " 个地址已在列表中", MsgType.Success);
                }
            }
            catch (Exception ex)
            {
                MainWindow.MainW.Toast.Show("保存失败:\r\n" + ex.Message, MsgType.Error);
            }
        }
        /// <summary>
        /// 导出图片lst，itmLstPic_Click
        /// </summary>
        private void itmLstPic_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Forms.SaveFileDialog saveFileDialog1 = new System.Windows.Forms.SaveFileDialog()
                {
                    DefaultExt = "lst",
                    FileName = "MoeLoaderPicList.lst",
                    Filter = "lst文件|*.lst",
                    OverwritePrompt = false
                };
                if (saveFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string text = "";
                    int success = 0, repeat = 0;
                    //读存在的lst内容
                    string[] flst = null;
                    bool havelst = File.Exists(saveFileDialog1.FileName);
                    bool isexists = false;

                    if (havelst)
                    {
                        flst = File.ReadAllLines(saveFileDialog1.FileName);
                    }

                    foreach (DownloadItem i in dlList.SelectedItems)
                    {
                        //查找重复项
                        try
                        {
                            isexists = havelst && flst.Any(x => x == i.Url);
                        }
                        catch { }

                        if (!isexists)
                        {
                            //url
                            text += i.Url + "\r\n";
                            success++;
                        }
                        else
                            repeat++;
                    }
                    File.AppendAllText(saveFileDialog1.FileName, text);
                    MainWindow.MainW.Toast.Show("成功保存 " + success + " 个地址\r\n" + repeat + " 个地址已在列表中", MsgType.Success);
                }
            }
            catch (Exception ex)
            {
                MainWindow.MainW.Toast.Show("保存失败:\r\n" + ex.Message, MsgType.Error);
            }
        }
        /// <summary>
        /// 复制地址
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void itmCopy_Click(object sender, RoutedEventArgs e)
        {
            DownloadItem i = (DownloadItem)dlList.SelectedItem;
            if (i == null) return;
            string text = i.Url;
            try
            {
                Clipboard.SetText(text);
            }
            catch { }
        }

        //============================== Menu Function ===================================
        private void ExecuteDownloadListTask(DLWorkMode dlworkmode)
        {
            int selectcs, delitemfile = 0;
            List<DownloadItem> selected = new List<DownloadItem>();
            if (dlworkmode == DLWorkMode.RetryAll || dlworkmode == DLWorkMode.AutoRetryAll
                || dlworkmode == DLWorkMode.StopAll || dlworkmode == DLWorkMode.RemoveAll)
            {
                foreach (object o in dlList.Items)
                {
                    //转存集合，防止selected改变
                    DownloadItem item = (DownloadItem)o;
                    selected.Add(item);
                }
            }
            else
            {
                foreach (object o in dlList.SelectedItems)
                {
                    DownloadItem item = (DownloadItem)o;
                    selected.Add(item);
                }
            }
            selectcs = selected.Count;

            foreach (DownloadItem item in selected)
            {
                switch (dlworkmode)
                {
                    case DLWorkMode.Retry:
                    case DLWorkMode.RetryAll:
                    case DLWorkMode.AutoRetryAll:
                        if (item.StatusE == DLStatus.Failed || item.StatusE == DLStatus.Cancel || item.StatusE == DLStatus.IsHave)
                        {
                            if (dlworkmode == DLWorkMode.AutoRetryAll && item.StatusE == DLStatus.Cancel) break;
                            if (retryTimer != null) retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
                            NumLeft = NumLeft > selectcs ? selectcs : NumLeft;
                            NumFail = NumFail > 0 ? --NumFail : 0;
                            DownloadItems.Remove(item);
                            downloadItemsDic.Remove(item.Url);
                            AddDownload(new MiniDownloadItem[] {
                                new MiniDownloadItem(item.FileName, item.Url, item.Host, item.Author, item.LocalName, item.LocalFileName,
                                item.Id, item.NoVerify, item.SiteIntfcIndex)
                            }, dlworkmode);
                        }
                        break;

                    case DLWorkMode.Stop:
                    case DLWorkMode.StopAll:
                        if (item.StatusE == DLStatus.DLing || item.StatusE == DLStatus.Wait)
                        {
                            if (webs.ContainsKey(item.Url))
                            {
                                webs[item.Url].IsStop = true;
                                webs.Remove(item.Url);
                            }

                            NumLeft = NumLeft > 0 ? --NumLeft : 0;

                            if (dlworkmode == DLWorkMode.StopAll)
                            {
                                NumLeft = 0;
                            }
                            item.StatusE = DLStatus.Cancel;
                            item.Size = "已取消";

                            try
                            {
                                File.Delete(item.LocalFileName + DLEXT);
                                DelDLItemNullDirector(item);
                            }
                            catch { }
                        }
                        break;

                    case DLWorkMode.Del:
                    case DLWorkMode.Remove:
                    case DLWorkMode.RemoveAll:
                        if (dlworkmode == DLWorkMode.Del && delitemfile < 1)
                        {
                            if (MessageBox.Show("QwQ 真的要把任务和文件一起删除么？", MainWindow.ProgramName,
                                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                            { delitemfile = 2; }
                            else
                            { delitemfile = 1; }
                        }
                        if (delitemfile > 1) { break; }

                        if (item.StatusE == DLStatus.DLing)
                        {
                            if (webs.ContainsKey(item.Url))
                            {
                                webs[item.Url].IsStop = true;
                                webs.Remove(item.Url);
                            }
                        }
                        else if (item.StatusE == DLStatus.Success || item.StatusE == DLStatus.IsHave)
                            NumSaved = NumSaved > 0 ? --NumSaved : 0;
                        else if (item.StatusE == DLStatus.Wait || item.StatusE == DLStatus.Cancel)
                        {
                            NumLeft = NumLeft > 0 ? --NumLeft : 0;
                        }
                        else if (item.StatusE == DLStatus.Failed)
                            NumFail = NumFail > 0 ? --NumFail : 0;


                        DownloadItems.Remove(item);
                        downloadItemsDic.Remove(item.Url);

                        //删除文件
                        string fname = item.LocalName;
                        if (dlworkmode == DLWorkMode.Del)
                        {
                            if (File.Exists(fname))
                            {
                                File.Delete(fname);
                                DelDLItemNullDirector(item);
                            }
                        }
                        break;
                }
            }
            if (dlworkmode == DLWorkMode.Stop || dlworkmode == DLWorkMode.Remove)
            {
                RefreshList();
            }
            if (dlworkmode == DLWorkMode.Remove)
            {
                RefreshStatus();
            }
        }

        /// <summary>
        /// 删除空目录
        /// </summary>
        /// <param name="item">下载项</param>
        private void DelDLItemNullDirector(DownloadItem item)
        {
            try
            {
                new Thread(new ThreadStart(delegate
                {
                    string lpath = GetLocalPath(item);
                    DirectoryInfo di = new DirectoryInfo(lpath);

                    while (Directory.Exists(lpath) && di.GetFiles().Length + di.GetDirectories().Length < 1 && lpath.Contains(SaveLocation))
                    {
                        Directory.Delete(lpath);
                        Thread.Sleep(666);
                        int last = lpath.LastIndexOf("\\", lpath.Length - 2);
                        lpath = lpath.Substring(0, last > 0 ? last : lpath.Length);
                        di = new DirectoryInfo(lpath);
                    }
                })).Start();
            }
            catch { }
        }
        //================================================================================
        /// <summary>
        /// 重试
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void itmRetry_Click(object sender, RoutedEventArgs e)
        {
            ResetRetryCount();
            ExecuteDownloadListTask(DLWorkMode.Retry);
        }

        /// <summary>
        /// 停止某个任务
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void itmStop_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDownloadListTask(DLWorkMode.Stop);
        }

        /// <summary>
        /// 移除某个任务
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void itmDelete_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDownloadListTask(DLWorkMode.Remove);
        }

        /// <summary>
        /// 停止所有下载
        /// </summary>
        public void StopAll()
        {
            DownloadItems.Clear();
            downloadItemsDic.Clear();
            foreach (DownloadTask item in webs.Values)
            {
                item.IsStop = true;
            }
        }

        /// <summary>
        /// 清理下载列表任务
        /// </summary>
        /// <param name="cm">清除类型 Success完成的 Failed失败的 Cancel已取消和已存在</param>
        private void ClearDlItems(DLStatus cm)
        {
            try
            {
                int s = DownloadItems.Count,
                      i = 0;
                while (i < s)
                {
                    DownloadItem item = DownloadItems[i];

                    switch (cm)
                    {
                        case DLStatus.Success:
                            if (item.StatusE == DLStatus.Success)
                            {
                                s--;
                                DownloadItems.RemoveAt(i);
                                downloadItemsDic.Remove(item.Url);
                            }
                            else
                            {
                                i++;
                            }
                            break;
                        case DLStatus.Failed:
                            if (item.StatusE == DLStatus.Failed)
                            {
                                s--;
                                DownloadItems.RemoveAt(i);
                                downloadItemsDic.Remove(item.Url);
                                NumFail = NumFail > 0 ? --NumFail : 0;
                            }
                            else
                            {
                                i++;
                            }
                            break;
                        case DLStatus.Cancel:
                            if (item.StatusE == DLStatus.Cancel || item.StatusE == DLStatus.IsHave)
                            {
                                s--;
                                DownloadItems.RemoveAt(i);
                                downloadItemsDic.Remove(item.Url);
                            }
                            else
                            {
                                i++;
                            }
                            break;
                    }
                }

                if (cm == DLStatus.Success)
                {
                    NumSaved = 0;
                }
                RefreshStatus();
            }
            catch { }
        }

        /// <summary>
        /// 清空已成功任务
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void itmClearDled_Click(object sender, RoutedEventArgs e)
        {
            ClearDlItems(DLStatus.Success);
        }

        /// <summary>
        /// 右键菜单即将打开
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void menu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (dlList.SelectedItems == null || dlList.SelectedItems.Count == 0)
            {
                itmLst.IsEnabled =
                    itmCopy.IsEnabled =
                    itmRetry.IsEnabled =
                    itmStop.IsEnabled =
                    itmDelete.IsEnabled =
                    itmLstPic.IsEnabled =
                    itmDeleteFile.IsEnabled = false;
            }
            else
            {
                itmCopy.IsEnabled = dlList.SelectedItems.Count == 1;
                itmLst.IsEnabled =
                    itmRetry.IsEnabled =
                    itmStop.IsEnabled =
                    itmDelete.IsEnabled =
                    itmLstPic.IsEnabled =
                    itmDeleteFile.IsEnabled = true;
            }

            itmRetryAll.IsEnabled =
                itmStopAll.IsEnabled =
                itmDeleteAll.IsEnabled = dlList.Items.Count > 0;
        }

        /// <summary>
        /// 文件拖拽事件
        /// </summary>
        public void UserControl_DragEnter(object sender, DragEventArgs e)
        {
            try
            {
                string fileName = ((string[])e.Data.GetData(System.Windows.Forms.DataFormats.FileDrop))[0];
                if (fileName != null && Path.GetExtension(fileName).ToLower() == ".lst")
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else e.Effects = DragDropEffects.None;
            }
            catch (Exception) { e.Effects = DragDropEffects.None; }
        }

        /// <summary>
        /// 从lst文件添加下载
        /// </summary>
        /// <param name="fileName"></param>
        public void DownLoadFromFile(string fileName)
        {
            if (fileName != null && Path.GetExtension(fileName).ToLower() == ".lst")
            {
                List<string> lines = new List<string>(File.ReadAllLines(fileName));
                List<MiniDownloadItem> items = new List<MiniDownloadItem>();
                MiniDownloadItem di = new MiniDownloadItem();
                //提取地址
                foreach (string line in lines)
                {
                    //移除空行
                    if (line.Trim().Length == 0) continue;
                    string[] parts = line.Split('|');

                    //url
                    if (parts.Length > 0 && parts[0].Trim().Length < 1)
                        continue;
                    else
                        di.url = parts[0];

                    //文件名
                    if (parts.Length > 1 && parts[1].Trim().Length > 0)
                    {
                        string ext = di.url.Substring(di.url.LastIndexOf('.'), di.url.Length - di.url.LastIndexOf('.'));
                        di.fileName = parts[1].EndsWith(ext) ? parts[1] : parts[1] + ext;
                    }

                    //域名
                    if (parts.Length > 2 && parts[2].Trim().Length > 0)
                        di.host = parts[2];

                    //上传者
                    if (parts.Length > 3 && parts[3].Trim().Length > 0)
                        di.author = parts[3];

                    //ID
                    if (parts.Length > 4 && parts[4].Trim().Length > 0)
                    {
                        try
                        {
                            di.id = int.Parse(parts[4]);
                        }
                        catch { }
                    }

                    //免文件校验
                    if (parts.Length > 5 && parts[5].Trim().Length > 0)
                        di.noVerify = parts[5].Contains('v');

                    //搜索时关键词
                    if (parts.Length > 6 && parts[6].Trim().Length > 0)
                        di.searchWord = parts[6];

                    //下载对象的站点接口检索号
                    if (parts.Length > 7 && parts[7].Trim().Length > 0)
                    {
                        try
                        {
                            di.siteIntfcIndex = int.Parse(parts[7]);
                        }
                        catch
                        {
                            //设为默认站点接口检索号
                            di.siteIntfcIndex = 10;
                        }
                    }
                    else
                    {
                        //设为默认站点接口检索号
                        di.siteIntfcIndex = 10;
                    }

                    items.Add(di);
                }

                //添加至下载列表
                AddDownload(items);
            }

            ResetRetryCount();
        }

        /// <summary>
        /// 文件被拖入
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void UserControl_Drop(object sender, DragEventArgs e)
        {
            try
            {
                string fileName = ((string[])(e.Data.GetData(System.Windows.Forms.DataFormats.FileDrop)))[0];
                DownLoadFromFile(fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("从文件添加下载失败\r\n" + ex.Message, MainWindow.ProgramName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 打开保存目录
        /// </summary>
        private void itmOpenSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DownloadItem dlItem = (DownloadItem)dlList.SelectedItem;

                if (File.Exists(dlItem.LocalName))
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("explorer.exe");
                    psi.Arguments = "/e,/select," + dlItem.LocalName;
                    System.Diagnostics.Process.Start(psi);
                }
                else
                {
                    System.Diagnostics.Process.Start(GetLocalPath(dlItem));
                }

            }
            catch
            {
                System.Diagnostics.Process.Start(SaveLocation);
            }
        }

        /// <summary>
        /// 双击一个表项执行的操作
        /// </summary>
        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DownloadItem dcitem = (DownloadItem)dlList.SelectedItem;

            if (e.ClickCount == 1)
            {
                isMouseSelect = true;
            }
            else if (e.ClickCount == 2)
            {

                switch (dcitem.StatusE)
                {
                    case DLStatus.Success:
                    case DLStatus.IsHave:
                        if (File.Exists(dcitem.LocalName))
                            System.Diagnostics.Process.Start(dcitem.LocalName);
                        else
                            MainWindow.MainW.Toast.Show("无法打开文件！可能已被更名、删除或移动", MsgType.Error);
                        break;
                    case DLStatus.Cancel:
                    case DLStatus.Failed:
                        ExecuteDownloadListTask(DLWorkMode.Retry);
                        break;
                    default:
                        ExecuteDownloadListTask(DLWorkMode.Stop);
                        break;
                }
            }
        }

        /// <summary>
        /// 鼠标左键放开
        /// </summary>
        private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isMouseSelect = false;
            dlList.SelectionMode = SelectionMode.Extended;
        }

        /// <summary>
        /// 鼠标移动事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Grid_MouseMove(object sender, MouseEventArgs e)
        {
            DownloadItem dlItem = (DownloadItem)((Grid)sender).DataContext;
            if (isMouseSelect && dlItem != null && e.LeftButton == MouseButtonState.Pressed)
            {
                if (dlList.SelectionMode == SelectionMode.Extended) { dlList.SelectionMode = SelectionMode.Multiple; }
                dlItem.IsSelected = true;
            }
        }

        /// <summary>
        /// 清除所有失败任务
        /// </summary>
        private void itmClearDled_Click_1(object sender, RoutedEventArgs e)
        {
            ClearDlItems(DLStatus.Failed);
        }

        /// <summary>
        /// 仅清除已取消和已存在的任务
        /// </summary>
        private void itmClearDled_Click_2(object sender, RoutedEventArgs e)
        {
            ClearDlItems(DLStatus.Cancel);
        }

        /// <summary>
        /// 全选
        /// </summary>
        private void itmSelAll_Click(object sender, RoutedEventArgs e)
        {
            dlList.SelectAll();
        }

        /// <summary>
        /// 重试所有任务
        /// </summary>
        private void itmRetryAll_Click(object sender, RoutedEventArgs e)
        {
            ResetRetryCount();
            ExecuteDownloadListTask(DLWorkMode.RetryAll);
        }

        /// <summary>
        /// 停止所有任务
        /// </summary>
        private void itmStopAll_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDownloadListTask(DLWorkMode.StopAll);
        }

        /// <summary>
        /// 移除所有任务
        /// </summary>
        private void itmDeleteAll_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDownloadListTask(DLWorkMode.RemoveAll);
        }

        /// <summary>
        /// 任务和文件一起删除
        /// </summary>
        private void itmDeleteFile_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDownloadListTask(DLWorkMode.Del);
        }

        /// <summary>
        /// 反选.......我能怎么办?我也很复杂啊...
        /// </summary>
        private void itmSelInvert_Click(object sender, RoutedEventArgs e)
        {
            //表项总数
            int listcount = DownloadItems.Count;
            //选中项的url
            List<string> selurl = new List<string>();

            if (listcount < 1)
            {
                dlList.UnselectAll();
                return;
            }

            if (dlList.SelectedItems.Count < 1)
            {
                itmSelAll_Click(null, null);
                return;
            }

            foreach (DownloadItem sitem in dlList.SelectedItems)
            {
                selurl.Add(sitem.Url);
            }

            //设置选中
            dlList.UnselectAll();
            DownloadItem item = null;
            int selcount = selurl.Count;

            for (int i = 0; i < listcount; i++)
            {
                int ii = 0;
                item = DownloadItems[i];

                //遍历是否之前选中
                foreach (string surl in selurl)
                {
                    ii++;
                    if (item.Url.Contains(surl))
                    {
                        break;
                    }
                    else if (ii == selcount)
                    {
                        dlList.SelectedItems.Add(item);
                    }
                }
            }
        }

        /// <summary>
        /// 当做下载列表快捷键
        /// </summary>
        private void dlList_KeyDown(object sender, KeyEventArgs e)
        {
            if (MainWindow.IsCtrlDown())
            {
                int dlselect = dlList.SelectedItems.Count;

                if (e.Key == Key.U)
                {   //反选
                    itmSelInvert_Click(null, null);
                }
                else if (dlselect > 0)
                {
                    if (e.Key == Key.L)
                    {//导出下载列表
                        itmLst_Click(null, null);
                    }
                    else if (e.Key == Key.C && dlselect == 1)
                    {   //复制地址
                        itmCopy_Click(null, null);
                    }
                    else if (e.Key == Key.Z)
                    {
                        //导出图片下载链接列表
                        itmLstPic_Click(null, null);
                    }
                    else if (e.Key == Key.R)
                    {    //重试
                        itmRetry_Click(null, null);
                    }
                    else if (e.Key == Key.S)
                    {    //停止
                        itmStop_Click(null, null);
                    }
                    else if (e.Key == Key.D)
                    {    //移除
                        itmDelete_Click(null, null);
                    }
                    else if (e.Key == Key.X)
                    {    //和文件一起删除
                        itmDeleteFile_Click(null, null);
                    }
                }
                if (e.Key == Key.G)
                {   //停止所有任务
                    itmStopAll_Click(null, null);
                }
                else if (e.Key == Key.V)
                {   //清空所有任务
                    itmDeleteAll_Click(null, null);
                }
                else if (e.Key == Key.T)
                {   //重试所有任务
                    itmRetryAll_Click(null, null);
                }
            }
        }

    }
}