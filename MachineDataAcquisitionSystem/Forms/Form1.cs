using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Models;
using System.Threading.Tasks;
using MachineDataAcquisitionSystem.Core.Parser.Core;
using System.Linq;
using MachineDataAcquisitionSystem.Forms;
using MachineDataAcquisitionSystem.Helpers;
using SqlSugar;
using System.Data.SQLite;
using Yitter.IdGenerator;
using System.Collections.Concurrent;
using CancellationToken = System.Threading.CancellationToken;
using SemaphoreSlim = System.Threading.SemaphoreSlim;

namespace MachineDataAcquisitionSystem
{
    public partial class Form1 : Form
    {
        //配置类
        private List<MachineConfig> _machineConfigs;

        // 队列统计
        private int _queueLength = 0;
        private int _processSpeed = 0;
        private DateTime _lastSpeedUpdate = DateTime.Now;
        private int _processedCountSinceLastUpdate = 0;


        // 正在处理的文件集合（防止重复处理）内存锁
        private HashSet<string> _processingFiles = new HashSet<string>();
        private object _processingLock = new object();
        // 队列数据结构
        private class QueueItem
        {
            public int MachineId { get; set; }
            public string FileName { get; set; }
            public string Status { get; set; }  // 等待中/处理中/成功/失败
            public DateTime AddTime { get; set; }
            public int Progress { get; set; }   // 进度百分比
        }

        // 队列列表
        private List<QueueItem> _queueItems = new List<QueueItem>();
        private object _queueLock = new object();  // 线程安全锁

        // 文件监控器
        private Dictionary<int, FileWatcher> _fileWatchers = new Dictionary<int, FileWatcher>();
        private HashSet<int> _stoppingMachines = new HashSet<int>();
        private bool _shutdownInProgress;
        private bool _shutdownCompleted;
        private FileParser _fileParser = new FileParser();

        // 机台监控路径
        private Dictionary<int, string> _monitorPaths = new Dictionary<int, string>();
        private Dictionary<int, string> _successPaths = new Dictionary<int, string>();
        private Dictionary<int, string> _errorPaths = new Dictionary<int, string>();


        // 机台运行状态
        private bool _machine1Running = false;
        private bool _machine2Running = false;
        private bool _machine3Running = false;
        private bool _machine4Running = false;
        private bool _machine5Running = false;
        private bool _machine6Running = false;

        // 机台今日数据
        private int _machine1Today = 0;
        private int _machine1Success = 0;
        private int _machine1Fail = 0;

        private int _machine2Today = 0;
        private int _machine2Success = 0;
        private int _machine2Fail = 0;

        private int _machine3Today = 0;
        private int _machine3Success = 0;
        private int _machine3Fail = 0;

        private int _machine4Today = 0;
        private int _machine4Success = 0;
        private int _machine4Fail = 0;

        private int _machine5Today = 0;
        private int _machine5Success = 0;
        private int _machine5Fail = 0;

        private int _machine6Today = 0;
        private int _machine6Success = 0;
        private int _machine6Fail = 0;
        // ===============================
        public Form1()
        {
            InitializeComponent();
            // 设置窗体启动位置为屏幕中央
            this.StartPosition = FormStartPosition.CenterScreen;


            // 加载配置
            LoadConfiguration();

            // 初始化日志写入线程
            InitLogDbWriter();

            // 初始化批量插入
            InitBatchInsert();

            InitLogFlusher(); 

            InitQueueRefreshTimer(); 
            InitLogFlusher();     


            // 初始化队列统计
            _lastSpeedUpdate = DateTime.Now;
            _processedCountSinceLastUpdate = 0;
            _processSpeed = 0;

            // 设置初始显示
            UpdateQueueStats();


            // 绑定按钮事件
            BindButtonEvents();

            // 初始化所有机台显示
            UpdateAllMachinesUI();

            //InitMachinePaths();

            InitQueueListView();
            // 初始化状态栏
            UpdateStatusBar();

            // 添加启动日志
            AddLog("系统启动完成", LogLevel.Success);

        }


        /// <summary>
        /// 加载机台配置
        /// </summary>
        private void LoadConfiguration()
        {
            // 加载配置
            _machineConfigs = MachineConfig.Load();

            // 如果没有配置或配置为空，创建默认配置
            if (_machineConfigs == null || _machineConfigs.Count == 0)
            {
                _machineConfigs = CreateDefaultConfig();
                MachineConfig.Save(_machineConfigs);
            }

            // 初始化路径字典
            for (int i = 1; i <= 6; i++)
            {
                var machine = _machineConfigs.Find(m => m.Id == i);
                if (machine != null)
                {
                    _monitorPaths[i] = machine.MonitorPath;
                    _successPaths[i] = machine.SuccessPath;
                    _errorPaths[i] = machine.ErrorPath;

                    // 创建目录
                    Directory.CreateDirectory(machine.MonitorPath);
                    Directory.CreateDirectory(machine.SuccessPath);
                    Directory.CreateDirectory(machine.ErrorPath);
                }
            }

            AddLog($"配置加载完成，共 {_machineConfigs.Count} 个机台", LogLevel.Success);
        }

        /// <summary>
        /// 创建默认配置
        /// </summary>
        private List<MachineConfig> CreateDefaultConfig()
        {
            var configs = new List<MachineConfig>();
            string basePath = @"D:\Test";

            for (int i = 1; i <= 6; i++)
            {
                configs.Add(new MachineConfig
                {
                    Id = i,
                    Name = $"机台{i}",
                    MonitorPath = Path.Combine(basePath, $"Machine{i}", "Incoming"),
                    SuccessPath = Path.Combine(basePath, $"Machine{i}", "Success"),
                    ErrorPath = Path.Combine(basePath, $"Machine{i}", "Error")
                });
            }

            return configs;
        }



        /// <summary>
        /// 更新状态栏显示
        /// </summary>
        private void UpdateStatusBar()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateStatusBar));
                return;
            }

            // 1. 机台状态
            int running = 0;
            if (_machine1Running) running++;
            if (_machine2Running) running++;
            if (_machine3Running) running++;
            if (_machine4Running) running++;
            if (_machine5Running) running++;
            if (_machine6Running) running++;

            if (toolStripStatusLabel3 != null)
            {
                toolStripStatusLabel3.Text = $"(机台: 6 | 运行中: {running})";
            }

            // 2. 总处理数、成功数、失败数（从统计面板获取）
            int total = _machine1Today + _machine2Today + _machine3Today +
                        _machine4Today + _machine5Today + _machine6Today;
            int success = _machine1Success + _machine2Success + _machine3Success +
                          _machine4Success + _machine5Success + _machine6Success;
            int fail = _machine1Fail + _machine2Fail + _machine3Fail +
                       _machine4Fail + _machine5Fail + _machine6Fail;

            if (toolStripStatusLabel4 != null)
            {
                toolStripStatusLabel4.Text = $"{total}";
            }

            if (toolStripStatusLabel6 != null)
            {
                toolStripStatusLabel6.Text = $"{success}";
            }

            if (toolStripStatusLabel19 != null)
            {
                toolStripStatusLabel19.Text = $"{fail}";
            }
        }



        // 在构造函数或初始化方法中设置 ListView 列
        private void InitQueueListView()
        {
            listView1.View = View.Details;
            listView1.FullRowSelect = true;
            listView1.Columns.Add("机台", 50);
            listView1.Columns.Add("文件名", 500);
            listView1.Columns.Add("状态", 80);
            listView1.Columns.Add("进度", 100);
        }


        /// <summary>
        /// 添加文件到队列
        /// </summary>
        private void AddToQueue(int machineId, string fileName)
        {
            lock (_queueLock)
            {
                var item = new QueueItem
                {
                    MachineId = machineId,
                    FileName = fileName,
                    Status = "等待中",
                    AddTime = DateTime.Now,
                    Progress = 0
                };
                _queueItems.Add(item);
                _queueLength = _queueItems.Count;
            }
            MarkQueueDirty();  // 只标记
            UpdateQueueStats();
        }


        /// <summary>
        /// 更新队列统计显示（队列长度 + 处理速度）
        /// </summary>
        private void UpdateQueueStats()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateQueueStats));
                return;
            }

            // 更新队列长度数字
            if (toolStripStatusLabel17 != null)
            {
                toolStripStatusLabel17.Text = _queueLength.ToString();
            }

            // 更新处理速度数字
            if (toolStripStatusLabel14 != null)
            {
                toolStripStatusLabel14.Text = _processSpeed.ToString();
            }
        }

        /// <summary>
        /// 更新队列项状态
        /// </summary>
        private void UpdateQueueStatus(int machineId, string fileName, string status, int progress = -1)
        {
            lock (_queueLock)
            {
                var item = _queueItems.Find(x => x.MachineId == machineId && x.FileName == fileName);
                if (item != null)
                {
                    item.Status = status;
                    if (progress >= 0)
                        item.Progress = progress;
                }
            }
            MarkQueueDirty();  // 只标记，不立即刷新
        }

        /// <summary>
        /// 从队列中移除项
        /// </summary>
        private void RemoveFromQueue(int machineId, string fileName)
        {
            lock (_queueLock)
            {
                _queueItems.RemoveAll(x => x.MachineId == machineId && x.FileName == fileName);
                _queueLength = _queueItems.Count;
            }
            MarkQueueDirty();  // 只标记
            UpdateQueueStats();
        }

        // 添加队列刷新控制
        private Timer _queueRefreshTimer;
        private bool _queueDirty = false;
        private object _queueDirtyLock = new object();

        // 在构造函数中初始化
        private void InitQueueRefreshTimer()
        {
            _queueRefreshTimer = new Timer();
            _queueRefreshTimer.Interval = 200; // 每200ms刷新一次
            _queueRefreshTimer.Tick += (s, e) =>
            {
                lock (_queueDirtyLock)
                {
                    if (_queueDirty)
                    {
                        _queueDirty = false;
                        RefreshQueueDisplayInternal();
                    }
                }
            };
            _queueRefreshTimer.Start();
        }

        private void MarkQueueDirty()
        {
            lock (_queueDirtyLock)
            {
                _queueDirty = true;
            }
        }

        private void RefreshQueueDisplay()
        {
            MarkQueueDirty();  // 不立即刷新，标记为脏
        }

        private void RefreshQueueDisplayInternal()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(RefreshQueueDisplayInternal));
                return;
            }

            listView1.BeginUpdate();  // 暂停重绘

            try
            {
                listView1.Items.Clear();

                lock (_queueLock)
                {
                    // 只显示最近50条，避免过多数据
                    var items = _queueItems.OrderByDescending(x => x.AddTime).Take(50).ToList();

                    foreach (var item in items)
                    {
                        var listItem = new ListViewItem(item.MachineId.ToString());
                        listItem.SubItems.Add(item.FileName);
                        listItem.SubItems.Add(item.Status);
                        listItem.SubItems.Add(item.Progress > 0 ? $"{item.Progress}%" : "");

                        // 根据状态设置颜色
                        switch (item.Status)
                        {
                            case "处理中":
                                listItem.ForeColor = Color.Blue;
                                break;
                            case "成功":
                                listItem.ForeColor = Color.Green;
                                break;
                            case "失败":
                                listItem.ForeColor = Color.Red;
                                break;
                            default:
                                listItem.ForeColor = Color.Black;
                                break;
                        }

                        listView1.Items.Add(listItem);
                    }
                }
            }
            finally
            {
                listView1.EndUpdate();  // 恢复重绘
            }
        }

        //private void InitMachinePaths()
        //{
        //    string basePath = @"D:\Test";

        //    for (int i = 1; i <= 6; i++)
        //    {
        //        _monitorPaths[i] = Path.Combine(basePath, $"Machine{i}", "Incoming");
        //        _successPaths[i] = Path.Combine(basePath, $"Machine{i}", "Success");
        //        _errorPaths[i] = Path.Combine(basePath, $"Machine{i}", "Error");

        //        // 创建目录
        //        Directory.CreateDirectory(_monitorPaths[i]);
        //        Directory.CreateDirectory(_successPaths[i]);
        //        Directory.CreateDirectory(_errorPaths[i]);
        //    }

        //    AddLog($"监控目录已初始化: {basePath}", LogLevel.Info);
        //}

        // ========== 绑定按钮事件 ==========
        private void BindButtonEvents()
        {
            // 机台按钮
            btn1.Click += async (s, e) => await ToggleMachineAsync(1);
            btn2.Click += async (s, e) => await ToggleMachineAsync(2);
            btn3.Click += async (s, e) => await ToggleMachineAsync(3);
            btn4.Click += async (s, e) => await ToggleMachineAsync(4);
            btn5.Click += async (s, e) => await ToggleMachineAsync(5);
            btn6.Click += async (s, e) => await ToggleMachineAsync(6);

            // 工具栏按钮（假设你叫这些名字，如果不是请告诉我）
            // btnStartAll.Click += BtnStartAll_Click;
            // btnStopAll.Click += BtnStopAll_Click;
            // btnConfig.Click += BtnConfig_Click;
            // btnLog.Click += BtnLog_Click;
            // btnHelp.Click += BtnHelp_Click;
        }

        // ========== 切换机台启动/停止 ==========
        private async Task ToggleMachineAsync(int machineId)
        {
            switch (machineId)
            {
                case 1:
                    if (_machine1Running) await StopMachineAsync(1); else StartMachine(1);
                    break;
                case 2:
                    if (_machine2Running) await StopMachineAsync(2); else StartMachine(2);
                    break;
                case 3:
                    if (_machine3Running) await StopMachineAsync(3); else StartMachine(3);
                    break;
                case 4:
                    if (_machine4Running) await StopMachineAsync(4); else StartMachine(4);
                    break;
                case 5:
                    if (_machine5Running) await StopMachineAsync(5); else StartMachine(5);
                    break;
                case 6:
                    if (_machine6Running) await StopMachineAsync(6); else StartMachine(6);
                    break;
            }
        }

        // ========== 启动机台 ==========
        private void StartMachine(int machineId)
        {
            if (_stoppingMachines.Contains(machineId))
            {
                AddLog($"机台{machineId} 正在停止，请稍后再启动", LogLevel.Warning);
                return;
            }

            switch (machineId)
            {
                case 1:
                    if (_machine1Running) return;

                    // 创建并启动监控器
                    var watcher1 = new FileWatcher(_monitorPaths[1], _successPaths[1], _errorPaths[1]);
                    watcher1.OnFileCreated += async (filePath, cancellationToken) =>
                        await ProcessFile(machineId, filePath, cancellationToken);
                    watcher1.OnError += ex => AddLog($"[机台{machineId}] 文件监听异常: {ex.Message}", LogLevel.Error);
                    _fileWatchers[1] = watcher1;
                    watcher1.Start();

                    _machine1Running = true;
                    UpdateMachine1UI();
                    AddLog($"机台1 已启动，监控目录: {_monitorPaths[1]}", LogLevel.Success);
                    break;

                case 2:
                    if (_machine2Running) return;

                    var watcher2 = new FileWatcher(_monitorPaths[2], _successPaths[2], _errorPaths[2]);
                    watcher2.OnFileCreated += async (filePath, cancellationToken) =>
                        await ProcessFile(machineId, filePath, cancellationToken);
                    watcher2.OnError += ex => AddLog($"[机台{machineId}] 文件监听异常: {ex.Message}", LogLevel.Error);
                    _fileWatchers[2] = watcher2;
                    watcher2.Start();

                    _machine2Running = true;
                    UpdateMachine2UI();
                    AddLog($"机台2 已启动，监控目录: {_monitorPaths[2]}", LogLevel.Success);
                    break;

                case 3:
                    if (_machine3Running) return;

                    var watcher3 = new FileWatcher(_monitorPaths[3], _successPaths[3], _errorPaths[3]);
                    watcher3.OnFileCreated += async (filePath, cancellationToken) =>
                        await ProcessFile(machineId, filePath, cancellationToken);
                    watcher3.OnError += ex => AddLog($"[机台{machineId}] 文件监听异常: {ex.Message}", LogLevel.Error);
                    _fileWatchers[3] = watcher3;
                    watcher3.Start();

                    _machine3Running = true;
                    UpdateMachine3UI();
                    AddLog($"机台3 已启动，监控目录: {_monitorPaths[3]}", LogLevel.Success);
                    break;

                case 4:
                    if (_machine4Running) return;

                    var watcher4 = new FileWatcher(_monitorPaths[4], _successPaths[4], _errorPaths[4]);
                    watcher4.OnFileCreated += async (filePath, cancellationToken) =>
                        await ProcessFile(machineId, filePath, cancellationToken);
                    watcher4.OnError += ex => AddLog($"[机台{machineId}] 文件监听异常: {ex.Message}", LogLevel.Error);
                    _fileWatchers[4] = watcher4;
                    watcher4.Start();

                    _machine4Running = true;
                    UpdateMachine4UI();
                    AddLog($"机台4 已启动，监控目录: {_monitorPaths[4]}", LogLevel.Success);
                    break;

                case 5:
                    if (_machine5Running) return;

                    var watcher5 = new FileWatcher(_monitorPaths[5], _successPaths[5], _errorPaths[5]);
                    watcher5.OnFileCreated += async (filePath, cancellationToken) =>
                        await ProcessFile(machineId, filePath, cancellationToken);
                    watcher5.OnError += ex => AddLog($"[机台{machineId}] 文件监听异常: {ex.Message}", LogLevel.Error);
                    _fileWatchers[5] = watcher5;
                    watcher5.Start();

                    _machine5Running = true;
                    UpdateMachine5UI();
                    AddLog($"机台5 已启动，监控目录: {_monitorPaths[5]}", LogLevel.Success);
                    break;

                case 6:
                    if (_machine6Running) return;

                    var watcher6 = new FileWatcher(_monitorPaths[6], _successPaths[6], _errorPaths[6]);
                    watcher6.OnFileCreated += async (filePath, cancellationToken) =>
                        await ProcessFile(machineId, filePath, cancellationToken);
                    watcher6.OnError += ex => AddLog($"[机台{machineId}] 文件监听异常: {ex.Message}", LogLevel.Error);
                    _fileWatchers[6] = watcher6;
                    watcher6.Start();

                    _machine6Running = true;
                    UpdateMachine6UI();
                    AddLog($"机台6 已启动，监控目录: {_monitorPaths[6]}", LogLevel.Success);
                    break;
            }

            UpdateStatusBar();
            // 刷新统计面板
            RefreshStatisticsPanel();
        }

        // ========== 停止机台 ==========
        private async Task StopMachineAsync(int machineId)
        {
            if (_stoppingMachines.Contains(machineId))
            {
                if (_fileWatchers.TryGetValue(machineId, out FileWatcher stoppingWatcher))
                {
                    await stoppingWatcher.StopAsync();
                }
                return;
            }
            if (!IsMachineRunning(machineId)) return;

            _stoppingMachines.Add(machineId);
            SetMachineRunning(machineId, false);
            UpdateStatusBar();
            AddLog($"机台{machineId} 正在立即停止并取消在途采集", LogLevel.Info);

            try
            {
                if (_fileWatchers.TryGetValue(machineId, out FileWatcher watcher))
                {
                    await watcher.StopAsync();
                    watcher.Dispose();
                    _fileWatchers.Remove(machineId);
                }

                lock (_queueLock)
                {
                    _queueItems.RemoveAll(x => x.MachineId == machineId);
                    _queueLength = _queueItems.Count;
                }
                DiscardCancelledBatchItems(machineId);
                AddLog($"机台{machineId} 已停止，在途采集已取消", LogLevel.Info);
            }
            finally
            {
                _stoppingMachines.Remove(machineId);
                RefreshQueueDisplay();
                UpdateQueueStats();
                UpdateStatusBar();
                RefreshStatisticsPanel();
            }
        }

        private bool IsMachineRunning(int machineId)
        {
            switch (machineId)
            {
                case 1: return _machine1Running;
                case 2: return _machine2Running;
                case 3: return _machine3Running;
                case 4: return _machine4Running;
                case 5: return _machine5Running;
                case 6: return _machine6Running;
                default: return false;
            }
        }

        private void SetMachineRunning(int machineId, bool isRunning)
        {
            switch (machineId)
            {
                case 1:
                    _machine1Running = isRunning;
                    UpdateMachine1UI();
                    break;
                case 2:
                    _machine2Running = isRunning;
                    UpdateMachine2UI();
                    break;
                case 3:
                    _machine3Running = isRunning;
                    UpdateMachine3UI();
                    break;
                case 4:
                    _machine4Running = isRunning;
                    UpdateMachine4UI();
                    break;
                case 5:
                    _machine5Running = isRunning;
                    UpdateMachine5UI();
                    break;
                case 6:
                    _machine6Running = isRunning;
                    UpdateMachine6UI();
                    break;
            }
        }

        /// <summary>
        /// 处理文件（解析并入库）
        /// </summary>
        private async Task ProcessFile(int machineId, string filePath, CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileName(filePath);
            string fileKey = $"{machineId}_{fileName}";
            DateTime startTime = DateTime.Now;
            bool isSuccess = false;
            bool isCancelled = false;
            bool acquisitionCommitted = false;
            int recordCount = 0;
            string errorMsg = null;
            BatchItem batchItem = null;

            cancellationToken.ThrowIfCancellationRequested();

            // ========== 防重复1：检查是否正在处理中 ==========
            lock (_processingLock)
            {
                if (_processingFiles.Contains(fileKey))
                {
                    AddLog($"[机台{machineId}] 文件 {fileName} 正在处理中，跳过重复触发", LogLevel.Warning);
                    return;
                }
                _processingFiles.Add(fileKey);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 添加到队列
                AddToQueue(machineId, fileName);

                UpdateQueueStatus(machineId, fileName, "处理中", 30);
                AddLog($"[机台{machineId}] 开始处理: {fileName}", LogLevel.Info);

                // ========== 2. 查找匹配脚本 ==========
                var script = ScriptMatcher.Match(machineId, filePath);
                cancellationToken.ThrowIfCancellationRequested();

                if (script == null)
                {
                    AddLog($"[机台{machineId}] 未找到匹配的解析脚本: {fileName}", LogLevel.Warning);
                    UpdateQueueStatus(machineId, fileName, "失败", 100);

                    cancellationToken.ThrowIfCancellationRequested();
                    if (_fileWatchers.ContainsKey(machineId))
                    {
                        _fileWatchers[machineId].MoveToError(filePath);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    UpdateMachineStats(machineId, false);

                    isSuccess = false;
                    errorMsg = "未找到匹配的解析脚本";
                    recordCount = 0;

                    await Task.Delay(2000, cancellationToken);
                    RemoveFromQueue(machineId, fileName);
                    return;
                }

                AddLog($"[机台{machineId}] 找到脚本: {script.Name} (模型ID: {script.ModelId})", LogLevel.Success);
                UpdateQueueStatus(machineId, fileName, "处理中", 50);

                // ========== 3. 执行脚本 ==========
                object model = null;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    model = ScriptEngine.Execute(script.ScriptCode, filePath, machineId, script.ModelId);
                    cancellationToken.ThrowIfCancellationRequested();
                    recordCount = 1; // 脚本返回一个模型对象
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errorMsg = $"脚本执行失败: {ex.Message}";
                    AddLog($"[机台{machineId}] {errorMsg}", LogLevel.Error);
                    UpdateQueueStatus(machineId, fileName, "失败", 100);

                    cancellationToken.ThrowIfCancellationRequested();
                    if (_fileWatchers.ContainsKey(machineId))
                    {
                        _fileWatchers[machineId].MoveToError(filePath);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    UpdateMachineStats(machineId, false);

                    isSuccess = false;
                    await Task.Delay(2000, cancellationToken);
                    RemoveFromQueue(machineId, fileName);
                    return;
                }

                UpdateQueueStatus(machineId, fileName, "处理中", 70);

                // ========== 4. 保存到服务器数据库 ==========
                if (model != null)
                {
                    try
                    {
                        //bool saveSuccess = await SaveToServerDatabase(model);

                        // ========== 补全基类默认值 ==========
                        cancellationToken.ThrowIfCancellationRequested();
                        await FillDefaultValues(model);
                        cancellationToken.ThrowIfCancellationRequested();
                        batchItem = AddToBatch(machineId, model, cancellationToken);
                        AddLog($"[机台{machineId}] 数据已加入批量队列", LogLevel.Success);
                        //if (!saveSuccess)
                        //{
                        //    AddLog($"[机台{machineId}] 保存到服务器数据库失败", LogLevel.Error);
                        //}
                        //else
                        //{
                        //    AddLog($"[机台{machineId}] 数据已保存到服务器数据库", LogLevel.Success);
                        //}
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"[机台{machineId}] 保存数据库异常: {ex.Message}", LogLevel.Error);
                        throw;
                    }
                }

                UpdateQueueStatus(machineId, fileName, "处理中", 90);

                AddLog($"[机台{machineId}] 处理成功", LogLevel.Success);

                UpdateQueueStatus(machineId, fileName, "处理中", 95);

                cancellationToken.ThrowIfCancellationRequested();
                MarkBatchItemReady(batchItem, cancellationToken);
                try
                {
                    if (_fileWatchers.ContainsKey(machineId))
                    {
                        _fileWatchers[machineId].MoveToSuccess(filePath);
                    }
                }
                catch
                {
                    RemoveBatchItem(batchItem);
                    throw;
                }

                acquisitionCommitted = true;
                UpdateMachineStats(machineId, true);

                isSuccess = true;
                errorMsg = null;

                UpdateQueueStatus(machineId, fileName, "成功", 100);
                RemoveFromQueue(machineId, fileName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                isCancelled = true;
                RemoveBatchItem(batchItem);
                AddLog($"[机台{machineId}] 采集已取消，文件保留在原目录: {fileName}", LogLevel.Warning);
                UpdateQueueStatus(machineId, fileName, "已取消", 100);
            }
            catch (Exception ex)
            {
                if (!acquisitionCommitted)
                {
                    RemoveBatchItem(batchItem);
                }
                AddLog($"[机台{machineId}] 处理文件异常: {ex.Message}", LogLevel.Error);
                UpdateQueueStatus(machineId, fileName, "失败", 100);

                cancellationToken.ThrowIfCancellationRequested();
                if (_fileWatchers.ContainsKey(machineId))
                {
                    _fileWatchers[machineId].MoveToError(filePath);
                }

                UpdateMachineStats(machineId, false);

                isSuccess = false;
                errorMsg = ex.Message;
                recordCount = 0;

                await Task.Delay(2000, cancellationToken);
                RemoveFromQueue(machineId, fileName);
            }
            finally
            {
                // 移除处理锁
                lock (_processingLock)
                {
                    _processingFiles.Remove(fileKey);
                }

                if ((isCancelled || cancellationToken.IsCancellationRequested) && !acquisitionCommitted)
                {
                    RemoveFromQueue(machineId, fileName);
                }
                else
                {
                    // ========== 保存处理记录到 SQLite 本地数据库 ==========
                    SaveProcessRecord(machineId, fileName, isSuccess, recordCount, errorMsg, startTime);
                    AddLog($"[机台{machineId}] 文件 {fileName} 处理完成", LogLevel.Info);
                }
            }
        }

        /// <summary>
        /// 补全基类字段默认值
        /// </summary>
        private async Task FillDefaultValues(object model)
        {
            try
            {
                var type = model.GetType();
                var defaultValues = GetBaseFieldDefaultValues();

                foreach (var fieldConfig in defaultValues)
                {
                    // ========== 跳过 CID，让数据库自动生成 ==========
                    if (fieldConfig.FieldName == "CID") continue;
                    var prop = type.GetProperty(fieldConfig.FieldName);
                    if (prop == null) continue;

                    object currentValue = prop.GetValue(model);
                    bool needSet = false;

                    switch (fieldConfig.FieldType.ToLower())
                    {
                        case "long":
                            if (currentValue == null || (long)currentValue == 0)
                                needSet = true;
                            break;
                        case "string":
                            if (currentValue == null || string.IsNullOrEmpty(currentValue.ToString()))
                                needSet = true;
                            break;
                        case "datetime":
                            if (currentValue == null || (DateTime)currentValue == DateTime.MinValue)
                                needSet = true;
                            break;
                        default:
                            if (currentValue == null)
                                needSet = true;
                            break;
                    }

                    if (needSet)
                    {
                        object defaultValue = EvaluateDefaultValue(fieldConfig.DefaultValue);
                        if (defaultValue != null)
                        {
                            prop.SetValue(model, defaultValue);
                            AddLog($"自动补全字段 {fieldConfig.FieldName} = {defaultValue}", LogLevel.Info);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"补全默认值失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 保存数据到服务器数据库
        /// </summary>
        private async Task<bool> SaveToServerDatabase(object model)
        {
            try
            {
                // ========== 1. 动态补全基类字段默认值 ==========
                var type = model.GetType();
                var defaultValues = GetBaseFieldDefaultValues();

                foreach (var fieldConfig in defaultValues)
                {
                    var prop = type.GetProperty(fieldConfig.FieldName);
                    if (prop == null) continue;

                    object currentValue = prop.GetValue(model);
                    bool needSet = false;

                    // 判断是否需要设置默认值
                    switch (fieldConfig.FieldType.ToLower())
                    {
                        case "long":
                            if (currentValue == null || (long)currentValue == 0)
                                needSet = true;
                            break;
                        case "int":
                            if (currentValue == null || (int)currentValue == 0)
                                needSet = true;
                            break;
                        case "string":
                            if (currentValue == null || string.IsNullOrEmpty(currentValue.ToString()))
                                needSet = true;
                            break;
                        case "datetime":
                            if (currentValue == null || (DateTime)currentValue == DateTime.MinValue)
                                needSet = true;
                            break;
                        default:
                            if (currentValue == null)
                                needSet = true;
                            break;
                    }

                    if (needSet)
                    {
                        object defaultValue = EvaluateDefaultValue(fieldConfig.DefaultValue);
                        if (defaultValue != null)
                        {
                            prop.SetValue(model, defaultValue);
                            //AddLog($"自动补全字段 {fieldConfig.FieldName} = {defaultValue}", LogLevel.Info);
                        }
                    }
                }
                // ===============================================

                var settings = SettingsHelper.LoadSettings();
                var primaryDb = settings.Databases?.FirstOrDefault(d => d.IsPrimary);

                if (primaryDb == null)
                {
                    AddLog("未配置主数据库", LogLevel.Warning);
                    return false;
                }

                // 获取连接字符串（如果 ConnectionString 为空，则根据其他字段生成）
                string connectionString = primaryDb.GetConnectionString();

                AddLog($"使用主数据库: {primaryDb.Name}", LogLevel.Info);
                AddLog($"连接字符串: {connectionString}", LogLevel.Info);

                if (string.IsNullOrEmpty(connectionString))
                {
                    AddLog("主数据库连接字符串为空", LogLevel.Error);
                    return false;
                }

                var dbType = GetDbType(primaryDb.DbType);

                using (var db = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = connectionString,
                    DbType = dbType,
                    IsAutoCloseConnection = true
                }))
                {
                    int result = await db.InsertableByObject(model).ExecuteCommandAsync();
                    return result > 0;
                }
            }
            catch (Exception ex)
            {
                AddLog($"保存到服务器数据库失败: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private static List<BaseFieldDefault> _cachedBaseFields;
        private static DateTime _cacheTime;
        private static object _cacheLock = new object();

        /// <summary>
        /// 获取基类字段默认值配置（带缓存）
        /// </summary>
        private List<BaseFieldDefault> GetBaseFieldDefaultValues()
        {
            // 缓存5分钟
            if (_cachedBaseFields != null && (DateTime.Now - _cacheTime).TotalMinutes < 5)
            {
                return _cachedBaseFields;
            }

            lock (_cacheLock)
            {
                // 双重检查
                if (_cachedBaseFields != null && (DateTime.Now - _cacheTime).TotalMinutes < 5)
                {
                    return _cachedBaseFields;
                }

                var result = new List<BaseFieldDefault>();

                try
                {
                    using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                    {
                        conn.Open();
                        string sql = "SELECT FieldName, FieldType, DefaultValue FROM BaseFields WHERE DefaultValue IS NOT NULL AND DefaultValue != '' AND FieldName != 'CID'";

                        using (var cmd = new SQLiteCommand(sql, conn))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Add(new BaseFieldDefault
                                {
                                    FieldName = reader.GetString(0),
                                    FieldType = reader.GetString(1),
                                    DefaultValue = reader.GetString(2)
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"读取基类字段失败: {ex.Message}");
                    // 返回缓存或空列表
                    return _cachedBaseFields ?? new List<BaseFieldDefault>();
                }

                _cachedBaseFields = result;
                _cacheTime = DateTime.Now;
                return result;
            }
        }

        /// <summary>
        /// 计算默认值表达式
        /// </summary>
        private object EvaluateDefaultValue(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return null;

            // YitIdHelper.NextId()
            if (expression.Contains("YitIdHelper.NextId()"))
            {
                return YitIdHelper.NextId();
            }

            // DateTime.Now
            if (expression == "DateTime.Now")
            {
                return DateTime.Now;
            }

            // 数字
            if (long.TryParse(expression, out long longVal))
            {
                return longVal;
            }

            if (int.TryParse(expression, out int intVal))
            {
                return intVal;
            }

            // 字符串（去掉引号）
            if (expression.StartsWith("\"") && expression.EndsWith("\""))
            {
                return expression.Trim('"');
            }

            return expression;
        }

        /// <summary>
        /// 获取 SqlSugar 数据库类型
        /// </summary>
        private DbType GetDbType(string dbTypeName)
        {
            switch (dbTypeName?.ToLower())
            {
                case "sql server":
                    return DbType.SqlServer;
                case "mysql":
                    return DbType.MySql;
                case "postgresql":
                    return DbType.PostgreSQL;
                case "oracle":
                    return DbType.Oracle;
                default:
                    return DbType.SqlServer;
            }
        }

        // 添加日志写入队列
        private static BlockingCollection<string> _logDbQueue = new BlockingCollection<string>();
        private static bool _isLogDbRunning = true;

        // 在构造函数中启动后台写入线程
        private void InitLogDbWriter()
        {
            Task.Run(() =>
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    while (_isLogDbRunning)
                    {
                        try
                        {
                            string sql = _logDbQueue.Take();
                            using (var cmd = new SQLiteCommand(sql, conn))
                            {
                                cmd.ExecuteNonQuery();
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"日志写入失败: {ex.Message}");
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 保存文件处理记录到 SQLite 数据库
        /// </summary>
        private void SaveProcessRecord(int machineId, string fileName, bool success, int recordCount, string errorMsg, DateTime startTime)
        {
            try
            {
                int duration = (int)(DateTime.Now - startTime).TotalMilliseconds;
                string status = success ? "成功" : "失败";

                // 构建 SQL（不使用参数，因为队列中无法共享连接）
                string sql = $@"
            INSERT INTO FileProcessRecord (MachineId, FileName, Status, RecordCount, ErrorMsg, ProcessTime, Duration)
            VALUES ({machineId}, '{fileName.Replace("'", "''")}', '{status}', {recordCount}, '{errorMsg?.Replace("'", "''") ?? ""}', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}', {duration})";

                // 加入队列，不直接写入
                _logDbQueue.Add(sql);

                AddLog($"[机台{machineId}] 处理记录已加入队列 (状态:{status}, 记录数:{recordCount}, 耗时:{duration}ms)", LogLevel.Info);
            }
            catch (Exception ex)
            {
                AddLog($"保存处理记录失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 更新机台统计
        /// </summary>
        private void UpdateMachineStats(int machineId, bool success)
        {
            switch (machineId)
            {
                case 1:
                    _machine1Today++;
                    if (success) _machine1Success++;
                    else _machine1Fail++;
                    UpdateMachine1UI();
                    break;
                case 2:
                    _machine2Today++;
                    if (success) _machine2Success++;
                    else _machine2Fail++;
                    UpdateMachine2UI();
                    break;
                case 3:
                    _machine3Today++;
                    if (success) _machine3Success++;
                    else _machine3Fail++;
                    UpdateMachine3UI();
                    break;
                case 4:
                    _machine4Today++;
                    if (success) _machine4Success++;
                    else _machine4Fail++;
                    UpdateMachine4UI();
                    break;
                case 5:
                    _machine5Today++;
                    if (success) _machine5Success++;
                    else _machine5Fail++;
                    UpdateMachine5UI();
                    break;
                case 6:
                    _machine6Today++;
                    if (success) _machine6Success++;
                    else _machine6Fail++;
                    UpdateMachine6UI();
                    break;
            }

            // 记录处理数量
            _processedCountSinceLastUpdate++;

            // 每分钟计算一次处理速度
            TimeSpan elapsed = DateTime.Now - _lastSpeedUpdate;
            if (elapsed.TotalSeconds >= 60)
            {
                _processSpeed = (int)(_processedCountSinceLastUpdate / elapsed.TotalMinutes);
                _processedCountSinceLastUpdate = 0;
                _lastSpeedUpdate = DateTime.Now;
                UpdateQueueStats();  // 更新显示
            }
            UpdateStatusBar();  // 添加这行，刷新状态栏

            // 每次更新后，刷新总统计面板
            RefreshStatisticsPanel();  // ← 这行必须有
        }


        /// <summary>
        /// 刷新统计面板（总处理文件、总成功数、总失败数、成功率）
        /// </summary>
        private void RefreshStatisticsPanel()
        {
            // 如果是在后台线程调用，就切换到 UI 线程
            if (InvokeRequired)
            {
                Invoke(new Action(RefreshStatisticsPanel));
                return;
            }

            int total = 0, success = 0, fail = 0;

            total += _machine1Today + _machine2Today + _machine3Today + _machine4Today + _machine5Today + _machine6Today;
            success += _machine1Success + _machine2Success + _machine3Success + _machine4Success + _machine5Success + _machine6Success;
            fail += _machine1Fail + _machine2Fail + _machine3Fail + _machine4Fail + _machine5Fail + _machine6Fail;

            labelTotalFiles.Text = total.ToString();
            labelSuccess.Text = success.ToString();
            labelFail.Text = fail.ToString();

            double rate = total > 0 ? (double)success / total * 100 : 0;
            labelRate.Text = $"{rate:F1}%";
        }

        // ========== 更新所有机台显示 ==========
        private void UpdateAllMachinesUI()
        {
            UpdateMachine1UI();
            UpdateMachine2UI();
            UpdateMachine3UI();
            UpdateMachine4UI();
            UpdateMachine5UI();
            UpdateMachine6UI();
        }



        // ========== 更新机台1显示 ==========
        private void UpdateMachine1UI()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateMachine1UI));
                return;
            }
            if (_machine1Running)
            {
                Stus1.Text = "运行中";
                Stus1.ForeColor = Color.Green;
                btn1.Text = "停止";
                btn1.ForeColor = Color.Red;
                //GB1.BackColor = Color.FromArgb(240, 255, 240);
            }
            else
            {
                Stus1.Text = "已停止";
                Stus1.ForeColor = Color.Red;
                btn1.Text = "启动";
                btn1.ForeColor = Color.Black;
                //GB1.BackColor = Color.White;
            }

            Stus2.Text = _machine1Today.ToString();
            Stus3.Text = _machine1Success.ToString();
            Stus4.Text = _machine1Fail.ToString();
        }
        // ========== 更新机台2显示 ==========
        private void UpdateMachine2UI()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateMachine2UI));
                return;
            }
            if (_machine2Running)
            {
                Stus5.Text = "运行中";
                Stus5.ForeColor = Color.Green;
                btn2.Text = "停止";
                btn2.ForeColor = Color.Red;
                //GB2.BackColor = Color.FromArgb(240, 255, 240);
            }
            else
            {
                Stus5.Text = "已停止";
                Stus5.ForeColor = Color.Red;
                btn2.Text = "启动";
                btn2.ForeColor = Color.Black;
                //GB2.BackColor = Color.White;
            }

            Stus6.Text = _machine2Today.ToString();
            Stus7.Text = _machine2Success.ToString();
            Stus8.Text = _machine2Fail.ToString();
        }

        // ========== 更新机台3显示 ==========
        private void UpdateMachine3UI()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateMachine3UI));
                return;
            }
            if (_machine3Running)
            {
                Stus9.Text = "运行中";
                Stus9.ForeColor = Color.Green;
                btn3.Text = "停止";
                btn3.ForeColor = Color.Red;
                //GB3.BackColor = Color.FromArgb(240, 255, 240);
            }
            else
            {
                Stus9.Text = "已停止";
                Stus9.ForeColor = Color.Red;
                btn3.Text = "启动";
                btn3.ForeColor = Color.Black;
                //GB3.BackColor = Color.White;
            }

            Stus10.Text = _machine3Today.ToString();
            Stus11.Text = _machine3Success.ToString();
            Stus12.Text = _machine3Fail.ToString();
        }

        // ========== 更新机台4显示 ==========
        private void UpdateMachine4UI()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateMachine4UI));
                return;
            }
            if (_machine4Running)
            {
                Stus13.Text = "运行中";
                Stus13.ForeColor = Color.Green;
                btn4.Text = "停止";
                btn4.ForeColor = Color.Red;
                //GB4.BackColor = Color.FromArgb(240, 255, 240);
            }
            else
            {
                Stus13.Text = "已停止";
                Stus13.ForeColor = Color.Red;
                btn4.Text = "启动";
                btn4.ForeColor = Color.Black;
                //GB4.BackColor = Color.White;
            }

            Stus14.Text = _machine4Today.ToString();
            Stus15.Text = _machine4Success.ToString();
            Stus16.Text = _machine4Fail.ToString();
        }
        // ========== 更新机台5显示 ==========
        private void UpdateMachine5UI()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateMachine5UI));
                return;
            }
            if (_machine5Running)
            {
                Stus17.Text = "运行中";
                Stus17.ForeColor = Color.Green;
                btn5.Text = "停止";
                btn5.ForeColor = Color.Red;
                //GB5.BackColor = Color.FromArgb(240, 255, 240);
            }
            else
            {
                Stus17.Text = "已停止";
                Stus17.ForeColor = Color.Red;
                btn5.Text = "启动";
                btn5.ForeColor = Color.Black;
                //GB5.BackColor = Color.White;
            }

            Stus18.Text = _machine5Today.ToString();
            Stus19.Text = _machine5Success.ToString();
            Stus20.Text = _machine5Fail.ToString();
        }

        // ========== 更新机台6显示 ==========
        private void UpdateMachine6UI()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateMachine6UI));
                return;
            }
            if (_machine6Running)
            {
                Stus21.Text = "运行中";
                Stus21.ForeColor = Color.Green;
                btn6.Text = "停止";
                btn6.ForeColor = Color.Red;
                //GB6.BackColor = Color.FromArgb(240, 255, 240);
            }
            else
            {
                Stus21.Text = "已停止";
                Stus21.ForeColor = Color.Red;
                btn6.Text = "启动";
                btn6.ForeColor = Color.Black;
                //GB6.BackColor = Color.White;
            }

            Stus22.Text = _machine6Today.ToString();
            Stus23.Text = _machine6Success.ToString();
            Stus24.Text = _machine6Fail.ToString();
        }

        // 添加日志队列
        private List<string> _pendingLogs = new List<string>();
        private object _logLock = new object();
        private Timer _logFlushTimer;
        private bool _isFlushing = false;

        // 在构造函数中初始化
        private void InitLogFlusher()
        {
            _logFlushTimer = new Timer();
            _logFlushTimer.Interval = 200; // 每200ms刷新一次
            _logFlushTimer.Tick += (s, e) => FlushLogs();
            _logFlushTimer.Start();
        }

        private void AddLog(string message, LogLevel level)
        {
            string time = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
            string prefix = GetLogPrefix(level);
            Color color = GetLogColor(level);

            string logText = $"[{time}] [{prefix}] {message}\r\n";
            string fileLogText = $"[{time}] [{prefix}] {message}{Environment.NewLine}";

            // ========== 保存到文件（异步） ==========
            Task.Run(() =>
            {
                try
                {
                    string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                    if (!Directory.Exists(logDir))
                        Directory.CreateDirectory(logDir);

                    string logFilePath = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(logFilePath, fileLogText);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"写入日志文件失败: {ex.Message}");
                }
            });

            // ========== 加入界面显示队列 ==========
            lock (_logLock)
            {
                _pendingLogs.Add(logText);
            }
        }

        private void FlushLogs()
        {
            if (_isFlushing) return;

            List<string> logsToShow = null;
            lock (_logLock)
            {
                if (_pendingLogs.Count == 0) return;
                logsToShow = new List<string>(_pendingLogs);
                _pendingLogs.Clear();
            }

            if (logsToShow == null || logsToShow.Count == 0) return;

            _isFlushing = true;

            try
            {
                if (richTextBox1.InvokeRequired)
                {
                    richTextBox1.Invoke(new Action(() =>
                    {
                        // RichTextBox 没有 BeginUpdate，直接追加
                        foreach (var logText in logsToShow)
                        {
                            richTextBox1.AppendText(logText);
                        }
                        richTextBox1.ScrollToCaret();
                    }));
                }
                else
                {
                    foreach (var logText in logsToShow)
                    {
                        richTextBox1.AppendText(logText);
                    }
                    richTextBox1.ScrollToCaret();
                }
            }
            finally
            {
                _isFlushing = false;
            }
        }
        /// <summary>
        /// 获取日志前缀
        /// </summary>
        private string GetLogPrefix(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Info: return "信息";
                case LogLevel.Success: return "成功";
                case LogLevel.Warning: return "警告";
                case LogLevel.Error: return "错误";
                default: return "信息";
            }
        }

        /// <summary>
        /// 获取日志颜色
        /// </summary>
        private Color GetLogColor(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Info: return Color.LightGreen;   // 浅绿色
                case LogLevel.Success: return Color.Black;    // 黑色
                case LogLevel.Warning: return Color.Orange;       // 橙色
                case LogLevel.Error: return Color.Red;          // 红色
                default: return Color.White;
            }
        }
        /// <summary>
        /// 帮助按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toolStripButton3_Click(object sender, EventArgs e)
        {
            // 创建自定义帮助窗体
            Form helpForm = new Form
            {
                Text = "帮助？",
                Size = new Size(500, 800),  // 加大窗口
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.White
            };

            // 创建 RichTextBox 显示帮助内容
            RichTextBox txtHelp = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("微软雅黑", 11F),  // 加大字体
                BackColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            txtHelp.Text =
                "═════════════════════════════════════════\n" +
                "                    文件数据采集系统 v1.0                    \n" +
                "═════════════════════════════════════════\n\n" +
                "【使用说明】\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                "1. 启动机台\n" +
                "   • 点击机台上的【启动】按钮启动单个机台\n" +
                "   • 点击【全启动】启动所有机台\n\n" +
                "2. 文件采集\n" +
                "   • 机台启动后自动监控文件夹\n" +
                "   • 支持格式：TXT、CSV、Excel\n" +
                "   • 处理后的文件移动到 Success 或 Error 目录\n\n" +
                "3. 查看数据\n" +
                "   • 每个机卡显示今日产量\n" +
                "   • 底部统计面板显示总数据\n" +
                "   • 实时队列显示正在处理的文件\n\n" +
                "4. 日志查看\n" +
                "   • 点击【日志】按钮查看历史记录\n\n" +
                "5. 授权说明\n" +
                "   • 本软件需要激活码激活\n" +
                "   • 未激活可试用2小时\n\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                "技术支持：明正宏电子(益阳)股份有限公司\n" +
                "联系电话：18977448590\n" +
                "官方网站：www.1352288166.qq.com\n\n" +
                "版权所有 © 由.net开发工程师禤桂良全权开发";

            // 添加关闭按钮
            Button btnClose = new Button
            {
                Text = "关闭",
                Size = new Size(100, 35),
                Font = new Font("微软雅黑", 10F),
                BackColor = Color.FromArgb(240, 240, 240),
                FlatStyle = FlatStyle.Flat
            };
            btnClose.Location = new Point(helpForm.Width - 120, helpForm.Height - 55);
            btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnClose.Click += (s, e2) => helpForm.Close();

            helpForm.Controls.Add(txtHelp);
            helpForm.Controls.Add(btnClose);

            helpForm.Show();
        }

        // ========== 批量插入相关 ==========
        private class BatchItem
        {
            public int MachineId { get; set; }
            public object Model { get; set; }
            public CancellationToken CancellationToken { get; set; }
            public bool IsReadyToPersist { get; set; }
        }

        private List<BatchItem> _batchDataList = new List<BatchItem>();
        private object _batchLock = new object();
        private Timer _batchTimer;
        private readonly SemaphoreSlim _batchFlushGate = new SemaphoreSlim(1, 1);
        private int _batchSize = 100;      // 每100条批量插入一次
        private int _batchInterval = 5000;  // 或每5秒批量插入一次

        /// <summary>
        /// 初始化批量插入定时器
        /// </summary>
        private void InitBatchInsert()
        {
            _batchTimer = new Timer();
            _batchTimer.Interval = _batchInterval;
            _batchTimer.Tick += async (s, e) =>
            {
                // 静默刷新，不打印日志
                await FlushBatchAsync(silent: true);
            };
            _batchTimer.Start();
            // 只打印一次启动日志
            AddLog($"批量插入定时器已启动，间隔 {_batchInterval}ms，阈值 {_batchSize} 条", LogLevel.Info);
        }

        /// <summary>
        /// 添加数据到批量队列
        /// </summary>
        private BatchItem AddToBatch(int machineId, object model, CancellationToken cancellationToken)
        {
            if (model == null) return null;

            lock (_batchLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batchItem = new BatchItem
                {
                    MachineId = machineId,
                    Model = model,
                    CancellationToken = cancellationToken,
                    IsReadyToPersist = false
                };
                _batchDataList.Add(batchItem);
                AddLog($"加入批量队列，当前队列长度: {_batchDataList.Count}", LogLevel.Info);

                // 达到批量大小，立即刷新
                if (_batchDataList.Count >= _batchSize)
                {
                    AddLog($"达到批量阈值 {_batchSize}，触发批量插入", LogLevel.Info);
                    Task.Run(async () => await FlushBatchAsync());
                }

                return batchItem;
            }
        }

        private void MarkBatchItemReady(BatchItem batchItem, CancellationToken cancellationToken)
        {
            if (batchItem == null) return;

            lock (_batchLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                batchItem.IsReadyToPersist = true;
            }
        }

        private void RemoveBatchItem(BatchItem batchItem)
        {
            if (batchItem == null) return;

            lock (_batchLock)
            {
                _batchDataList.Remove(batchItem);
            }
        }

        private void DiscardCancelledBatchItems(int machineId)
        {
            lock (_batchLock)
            {
                int removed = _batchDataList.RemoveAll(
                    x => x.MachineId == machineId
                        && !x.IsReadyToPersist
                        && x.CancellationToken.IsCancellationRequested);
                if (removed > 0)
                {
                    AddLog($"[机台{machineId}] 已丢弃 {removed} 条暂停前尚未入库的数据", LogLevel.Info);
                }
            }
        }

        /// <summary>
        /// 批量插入数据库
        /// </summary>
        private async Task FlushBatchAsync(bool silent = false)
        {
            if (!await _batchFlushGate.WaitAsync(0)) return;

            List<BatchItem> dataToSave = null;
            try
            {
                lock (_batchLock)
                {
                    _batchDataList.RemoveAll(
                        x => !x.IsReadyToPersist && x.CancellationToken.IsCancellationRequested);
                    dataToSave = _batchDataList.Where(x => x.IsReadyToPersist).ToList();
                    if (dataToSave.Count == 0) return;
                    _batchDataList.RemoveAll(x => x.IsReadyToPersist);
                    AddLog($"准备批量插入 {dataToSave.Count} 条数据", LogLevel.Info);
                }

                if (dataToSave == null || dataToSave.Count == 0) return;

                var settings = SettingsHelper.LoadSettings();
                var primaryDb = settings.Databases?.FirstOrDefault(d => d.IsPrimary);

                if (primaryDb == null)
                {
                    throw new InvalidOperationException("未配置主数据库");
                }

                string connectionString = primaryDb.GetConnectionString();
                var dbType = GetDbType(primaryDb.DbType);

                using (var db = new SqlSugarClient(new ConnectionConfig
                {
                    ConnectionString = connectionString,
                    DbType = dbType,
                    IsAutoCloseConnection = true
                }))
                {
                    db.Ado.BeginTran();
                    try
                    {
                        int insertedRows = 0;
                        // 按类型分组
                        var groups = dataToSave.GroupBy(x => x.Model.GetType());

                        foreach (var group in groups)
                        {
                            var list = group.Select(x => x.Model).ToList();
                            AddLog($"插入类型 {group.Key.Name}，共 {list.Count} 条", LogLevel.Info);

                            int result = await db
                                .InsertableByObject(list)
                                .IgnoreColumns("CID")
                                .ExecuteCommandAsync();
                            insertedRows += result;
                        }

                        db.Ado.CommitTran();
                        AddLog($"批量事务提交成功，影响行数: {insertedRows}", LogLevel.Success);
                    }
                    catch
                    {
                        db.Ado.RollbackTran();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"批量插入失败: {ex.Message}", LogLevel.Error);

                if (dataToSave != null)
                {
                    // 事务失败时整批重新排队，避免部分提交或数据丢失。
                    lock (_batchLock)
                    {
                        _batchDataList.InsertRange(0, dataToSave);
                    }
                }
            }
            finally
            {
                _batchFlushGate.Release();
            }
        }

        /// <summary>
        /// 程序关闭时刷新剩余数据
        /// </summary>
        private async Task FlushRemainingData()
        {
            _batchTimer?.Stop();
            await FlushBatchAsync();
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_shutdownCompleted)
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;
            if (_shutdownInProgress) return;

            _shutdownInProgress = true;
            Enabled = false;
            try
            {
                _isLogDbRunning = false;
                await StopAllMachinesAsync();
                await FlushRemainingData();

                if (_batchTimer != null)
                {
                    _batchTimer.Stop();
                    _batchTimer.Dispose();
                }
            }
            catch (Exception ex)
            {
                AddLog($"程序关闭清理失败: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                _shutdownCompleted = true;
                _shutdownInProgress = false;
                Close();
            }
        }

        private void groupBox8_Enter(object sender, EventArgs e)
        {

        }

        /// <summary>
        /// 全启动 - 启动所有机台
        /// </summary>
        private void StartAllMachines()
        {
            AddLog("正在启动所有机台...", LogLevel.Info);

            // 启动机台1-6
            StartMachine(1);
            StartMachine(2);
            StartMachine(3);
            StartMachine(4);
            StartMachine(5);
            StartMachine(6);

            AddLog("所有机台启动完成", LogLevel.Success);
        }

        /// <summary>
        /// 全停止 - 停止所有机台
        /// </summary>
        private async Task StopAllMachinesAsync()
        {
            AddLog("正在停止所有机台...", LogLevel.Info);

            // 先同时向全部机台发出取消，再等待所有在途任务退出。
            Task[] stopTasks = Enumerable.Range(1, 6)
                .Select(StopMachineAsync)
                .ToArray();
            await Task.WhenAll(stopTasks);

            AddLog("所有机台已停止", LogLevel.Success);
        }

        /// <summary>
        /// 全启动
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toolStripButton6_Click(object sender, EventArgs e)
        {
            StartAllMachines();
        }
        /// <summary>
        /// 全停止
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void toolStripButton5_Click(object sender, EventArgs e)
        {
            await StopAllMachinesAsync();
        }
        /// <summary>
        /// 导航栏日志按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            var logViewer = new LogViewerForm();
            logViewer.Show();
        }
        /// <summary>
        /// 配置按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toolStripButton4_Click(object sender, EventArgs e)
        {
            var configForm = new ConfigForm();
            configForm.Show();
        }
        /// <summary>
        /// 查询按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            var queryForm = new QueryForm();
            queryForm.Show();
        }
        /// <summary>
        /// 模型设计
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnModelDesign_Click(object sender, EventArgs e)
        {
            ModelConfigForm form = new ModelConfigForm();
            form.ShowDialog();
        }
        /// <summary>
        /// 基类设计
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnBaseFieldConfig_Click(object sender, EventArgs e)
        {
            BaseFieldConfigForm form = new BaseFieldConfigForm();
            form.ShowDialog();
        }
    }
    /// <summary>
    /// 基类字段默认值配置
    /// </summary>
    public class BaseFieldDefault
    {
        public string FieldName { get; set; }
        public string FieldType { get; set; }
        public string DefaultValue { get; set; }
    }
}
