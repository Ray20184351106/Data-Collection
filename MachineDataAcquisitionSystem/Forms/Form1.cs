using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MachineDataAcquisitionSystem.Core;
using MachineDataAcquisitionSystem.Core.Mapping;
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
        private RemoteAgentBridge _remoteAgentBridge;
        private PendingUploadStore _pendingUploadStore;

        // 队列统计
        private int _queueLength = 0;
        private int _processSpeed = 0;
        private DateTime _lastSpeedUpdate = DateTime.Now;
        private int _processedCountSinceLastUpdate = 0;


        // 正在处理的文件集合（防止重复处理）内存锁
        private HashSet<string> _processingFiles = new HashSet<string>();
        private object _processingLock = new object();
        private readonly SemaphoreSlim _globalProcessingLimit = new SemaphoreSlim(4, 4);
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _deviceProcessingLimits = new ConcurrentDictionary<int, SemaphoreSlim>();
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
        private bool _exitRequested;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayContextMenu;
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
            InitFileLogWriter();

            _pendingUploadStore = new PendingUploadStore(DatabaseHelper.GetConnectionString());

            // 初始化批量插入
            InitBatchInsert();

            InitLogFlusher(); 

            InitQueueRefreshTimer(); 


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

            // Agent 不可用时不影响本地采集；远程停止复用本地的取消链路。
            InitRemoteAgentBridge();
            InitializeTrayIcon();

        }

        private void InitRemoteAgentBridge()
        {
            _remoteAgentBridge = new RemoteAgentBridge(
                machineId =>
                {
                    if (machineId.HasValue) StartMachine(machineId.Value); else StartAllMachines();
                    return Task.CompletedTask;
                },
                StopFromRemoteAgent,
                ReloadFromRemoteAgent,
                GetAgentDeviceSnapshots);
        }

        private async Task StopFromRemoteAgent(int? machineId)
        {
            try
            {
                if (machineId.HasValue)
                {
                    await StopMachineAsync(machineId.Value);
                }
                else
                {
                    await StopAllMachinesAsync();
                }
            }
            catch (Exception ex)
            {
                AddLog($"远程停止机台失败: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        private async Task ReloadFromRemoteAgent()
        {
            try
            {
                bool restart = _machine1Running || _machine2Running || _machine3Running || _machine4Running || _machine5Running || _machine6Running;
                await StopAllMachinesAsync();
                LoadConfiguration();
                if (restart) StartAllMachines();
            }
            catch (Exception ex)
            {
                AddLog($"远程重载配置失败: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        private IList<AgentDeviceSnapshot> GetAgentDeviceSnapshots()
        {
            int[] successes = { _machine1Success, _machine2Success, _machine3Success, _machine4Success, _machine5Success, _machine6Success };
            int[] failures = { _machine1Fail, _machine2Fail, _machine3Fail, _machine4Fail, _machine5Fail, _machine6Fail };
            bool[] running = { _machine1Running, _machine2Running, _machine3Running, _machine4Running, _machine5Running, _machine6Running };
            var result = new List<AgentDeviceSnapshot>();
            lock (_queueLock)
            {
                for (int i = 1; i <= 6; i++)
                {
                    var config = _machineConfigs.FirstOrDefault(x => x.Id == i);
                    result.Add(new AgentDeviceSnapshot
                    {
                        DeviceId = i.ToString(),
                        Name = config?.Name ?? $"机台{i}",
                        State = running[i - 1] ? 1 : 0,
                        QueueDepth = _queueItems.Count(x => x.MachineId == i && (x.Status == "等待中" || x.Status == "处理中")),
                        TodaySuccess = successes[i - 1],
                        TodayFailure = failures[i - 1]
                    });
                }
            }
            return result;
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
            long? parserVersionId = null;
            IReadOnlyList<BatchItem> batchItems = Array.Empty<BatchItem>();
            SemaphoreSlim deviceProcessingLimit = null;
            bool deviceProcessingLimitAcquired = false;
            bool globalProcessingLimitAcquired = false;

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

                deviceProcessingLimit = _deviceProcessingLimits.GetOrAdd(machineId, _ => new SemaphoreSlim(1, 1));
                await deviceProcessingLimit.WaitAsync(cancellationToken);
                deviceProcessingLimitAcquired = true;
                await _globalProcessingLimit.WaitAsync(cancellationToken);
                globalProcessingLimitAcquired = true;
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

                var runtimeSchemaService = new ModelSchemaService(DatabaseHelper.GetConnectionString());
                string currentModelHash = runtimeSchemaService.ComputeHash(script.ModelId);
                if (!string.Equals(currentModelHash, script.ModelSchemaHash, StringComparison.Ordinal))
                    throw new InvalidOperationException("关联模型结构已变化，当前发布规则必须重新验证后才能采集。");
                foreach (ParseScriptModelSnapshot snapshot in script.ModelSnapshots)
                {
                    string snapshotCurrentHash = runtimeSchemaService.ComputeHash(snapshot.ModelId);
                    if (!string.Equals(snapshotCurrentHash, snapshot.ModelSchemaHash, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "主子表关联模型结构已变化，当前发布规则必须重新验证后才能采集：" + snapshot.ModelType);
                }

                MappingRuleDefinition mappingDefinition = null;
                bool isMasterDetail = false;
                bool isImageFileName = false;
                if (script.RuleType == ParseRuleType.Mapping &&
                    !string.IsNullOrWhiteSpace(script.DefinitionJson))
                {
                    mappingDefinition = MappingRuleSerializer.Deserialize(script.DefinitionJson);
                    isMasterDetail = mappingDefinition.RecordMode == MappingRecordMode.MasterDetail;
                    isImageFileName = mappingDefinition.RecordMode == MappingRecordMode.ImageFileName;
                }

                AddLog($"[机台{machineId}] 找到脚本: {script.Name} (模型ID: {script.ModelId})", LogLevel.Success);
                parserVersionId = script.ParserVersionId.HasValue && script.ParserVersionId.Value > 0
                    ? script.ParserVersionId
                    : null;
                UpdateQueueStatus(machineId, fileName, "处理中", 50);

                // ========== 3. 执行脚本 ==========
                IReadOnlyList<object> models = Array.Empty<object>();
                MasterDetailParseResult masterDetail = null;
                bool masterDetailCommitted = false;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    object scriptResult = ScriptEngine.Execute(script, filePath, machineId);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (isMasterDetail)
                    {
                        masterDetail = MappingResultNormalizer.NormalizeMasterDetail(
                            scriptResult,
                            mappingDefinition.MasterDetail.Master.TargetModelType,
                            mappingDefinition.MasterDetail.Detail.TargetModelType);
                        recordCount = masterDetail.Details.Count;
                    }
                    else
                    {
                        models = MappingResultNormalizer.Normalize(scriptResult, script.TargetModelType);
                        recordCount = models.Count;
                    }
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
                if (masterDetail != null || models.Count > 0)
                {
                    try
                    {
                        //bool saveSuccess = await SaveToServerDatabase(model);

                        // ========== 补全基类默认值 ==========
                        cancellationToken.ThrowIfCancellationRequested();
                        if (masterDetail != null)
                        {
                            await FillDefaultValues(masterDetail.Master);
                            foreach (object detail in masterDetail.Details)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await FillDefaultValues(detail);
                            }
                            Guid pendingMasterDetailId = _pendingUploadStore.Enqueue(
                                machineId,
                                filePath,
                                script.ModelId);
                            MasterDetailPersistenceResult result = await ExecuteDatabaseOperationWithRetryAsync(
                                pendingMasterDetailId,
                                () => SaveMasterDetailToServerDatabase(masterDetail, cancellationToken),
                                cancellationToken);
                            masterDetailCommitted = true;
                            AddLog(
                                $"[机台{machineId}] 主子表事务提交成功：主表 1 条、子表 {result.DetailCount} 条，主表CID={result.MasterCid}",
                                LogLevel.Success);
                        }
                        if (isImageFileName)
                        {
                            if (models.Count != 1)
                                throw new MappingValidationException("图片文件名规则必须且只能生成一条目标记录。");
                            ImageArchiveResult archive = new ImageArchiveService().Archive(
                                filePath,
                                machineId,
                                mappingDefinition.ImageArchive);
                            ImageArchiveService.ApplyArchivedPath(
                                models[0],
                                mappingDefinition.ImageArchive,
                                archive.ArchivedPath);
                            AddLog(
                                archive.ReusedExistingFile
                                    ? $"[机台{machineId}] 已复用共享图片: {archive.ArchivedPath}"
                                    : $"[机台{machineId}] 图片已复制到共享目录: {archive.ArchivedPath}",
                                LogLevel.Success);
                        }
                        foreach (object model in models)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await FillDefaultValues(model);
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        if (masterDetail == null)
                        {
                            batchItems = AddBatchGroup(
                                machineId,
                                script.ModelId,
                                filePath,
                                models,
                                cancellationToken);
                            AddLog($"[机台{machineId}] {batchItems.Count} 条数据已作为同一文件批次加入队列", LogLevel.Success);
                        }
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

                AddLog(
                    masterDetail == null
                        ? $"[机台{machineId}] 处理成功，共解析 {recordCount} 条记录"
                        : $"[机台{machineId}] 处理成功，主表 1 条、子表 {recordCount} 条",
                    LogLevel.Success);

                UpdateQueueStatus(machineId, fileName, "处理中", 95);

                if (!masterDetailCommitted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MarkBatchGroupReady(batchItems, cancellationToken);
                    UpdateQueueStatus(machineId, fileName, "等待数据库提交", 95);
                    await WaitForBatchGroupPersistenceAsync(batchItems, cancellationToken);
                    AddLog($"[机台{machineId}] 数据库事务已提交", LogLevel.Success);
                }
                try
                {
                    if (_fileWatchers.ContainsKey(machineId))
                    {
                        _fileWatchers[machineId].MoveToSuccess(filePath);
                    }
                }
                catch
                {
                    RemoveBatchGroup(batchItems);
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
                RemoveBatchGroup(batchItems);
                AddLog($"[机台{machineId}] 采集已取消，文件保留在原目录: {fileName}", LogLevel.Warning);
                UpdateQueueStatus(machineId, fileName, "已取消", 100);
            }
            catch (Exception ex)
            {
                if (!acquisitionCommitted)
                {
                    RemoveBatchGroup(batchItems);
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
                if (globalProcessingLimitAcquired)
                {
                    _globalProcessingLimit.Release();
                }
                if (deviceProcessingLimitAcquired)
                {
                    deviceProcessingLimit.Release();
                }

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
                    SaveProcessRecord(machineId, fileName, isSuccess, recordCount, errorMsg, startTime, parserVersionId);
                    AddLog($"[机台{machineId}] 文件 {fileName} 处理完成", LogLevel.Info);
                }
            }
        }

        /// <summary>
        /// 补全基类字段默认值
        /// </summary>
        private Task FillDefaultValues(object model)
        {
            try
            {
                var type = model.GetType();
                var defaultValues = GetBaseFieldDefaultValues();

                foreach (var fieldConfig in defaultValues)
                {
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

                if (ModelIdentityInitializer.EnsureCid(model, () => YitIdHelper.NextId()))
                    AddLog("已自动生成 CID", LogLevel.Info);
            }
            catch (Exception ex)
            {
                AddLog($"补全默认值失败: {ex.Message}", LogLevel.Error);
            }
            return Task.CompletedTask;
        }

        private void InitializeTrayIcon()
        {
            components = TrayComponentContainer.Ensure(components);
            _trayContextMenu = new ContextMenuStrip(components);
            var openMainWindowItem = new ToolStripMenuItem("打开主界面");
            var exitApplicationItem = new ToolStripMenuItem("退出程序");
            openMainWindowItem.Click += (sender, args) => RestoreMainWindowFromTray();
            exitApplicationItem.Click += (sender, args) => RequestApplicationExit();
            _trayContextMenu.Items.Add(openMainWindowItem);
            _trayContextMenu.Items.Add(new ToolStripSeparator());
            _trayContextMenu.Items.Add(exitApplicationItem);

            _trayIcon = new NotifyIcon(components)
            {
                ContextMenuStrip = _trayContextMenu,
                Icon = Icon ?? SystemIcons.Application,
                Text = "产线数据采集系统",
                Visible = true
            };
            _trayIcon.MouseClick += TrayIcon_MouseClick;
        }

        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                RestoreMainWindowFromTray();
        }

        private void RestoreMainWindowFromTray()
        {
            if (_shutdownInProgress || _shutdownCompleted) return;

            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        private void HideMainWindowToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void RequestApplicationExit()
        {
            if (_shutdownInProgress || _shutdownCompleted) return;

            _exitRequested = true;
            Close();
        }

        private async Task<MasterDetailPersistenceResult> SaveMasterDetailToServerDatabase(
            MasterDetailParseResult aggregate,
            CancellationToken cancellationToken)
        {
            var settings = SettingsHelper.LoadSettings();
            var primaryDb = settings.Databases?.FirstOrDefault(database => database.IsPrimary);
            if (primaryDb == null)
                throw new InvalidOperationException("未配置主数据库。");

            string connectionString = primaryDb.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("主数据库连接字符串为空。");

            var schemaLoader = new TargetTableSchemaLoader(DatabaseHelper.GetConnectionString());
            TargetTableDefinition masterDefinition = schemaLoader.Load(aggregate.MasterModelId);
            TargetTableDefinition detailDefinition = schemaLoader.Load(aggregate.DetailModelId);

            using (var db = new SqlSugarClient(new ConnectionConfig
            {
                ConnectionString = connectionString,
                DbType = GetDbType(primaryDb.DbType),
                IsAutoCloseConnection = true
            }))
            {
                return await new MasterDetailPersistenceService().PersistAsync(
                    db,
                    aggregate,
                    masterDefinition,
                    detailDefinition,
                    () => YitIdHelper.NextId(),
                    cancellationToken);
            }
        }

        private async Task<T> ExecuteDatabaseOperationWithRetryAsync<T>(
            Guid pendingUploadId,
            Func<Task<T>> operation,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                T result;
                try
                {
                    result = await operation();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    attempt++;
                    if (!DatabaseRetryPolicy.IsTransient(ex))
                    {
                        _pendingUploadStore.RecordPermanentFailure(pendingUploadId, attempt, ex.Message);
                        throw new InvalidOperationException(
                            "数据库入库失败，错误不可自动重试：" + ex.Message,
                            ex);
                    }

                    TimeSpan delay = DatabaseRetryPolicy.GetDelay(attempt);
                    DateTime nextAttemptAt = DateTime.Now.Add(delay);
                    _pendingUploadStore.RecordTransientFailure(
                        pendingUploadId,
                        attempt,
                        nextAttemptAt,
                        ex.Message);
                    AddLog(
                        $"数据库暂时不可用，第 {attempt} 次失败，将在 {delay.TotalSeconds:0} 秒后重试: {ex.Message}",
                        LogLevel.Warning);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                try
                {
                    _pendingUploadStore.Complete(pendingUploadId);
                }
                catch (Exception cleanupException)
                {
                    AddLog(
                        "数据库已提交，但清理本地待入库状态失败: " + cleanupException.Message,
                        LogLevel.Warning);
                }
                return result;
            }
        }

        /// <summary>
        /// 保存数据到服务器数据库
        /// </summary>
        private async Task<bool> SaveToServerDatabase(object model, int modelId)
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

                ModelIdentityInitializer.EnsureCid(model, () => YitIdHelper.NextId());

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
                    TargetTableProvisionResult provision = new TargetTableProvisioner()
                        .EnsureTable(
                            db,
                            model.GetType(),
                            new TargetTableSchemaLoader(DatabaseHelper.GetConnectionString()).Load(modelId));
                    if (provision.Created)
                        AddLog("已自动创建主数据库目标表：" + provision.TableName, LogLevel.Success);
                    else if (provision.AdjustedColumnCount > 0)
                        AddLog(
                            string.Format("已按模型配置修正目标表 {0} 的 {1} 个字段", provision.TableName, provision.AdjustedColumnCount),
                            LogLevel.Success);

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
                        string sql = "SELECT FieldName, FieldType, DefaultValue FROM BaseFields WHERE DefaultValue IS NOT NULL AND DefaultValue != ''";

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
                case "sqlite":
                    return DbType.Sqlite;
                default:
                    return DbType.SqlServer;
            }
        }

        // 添加日志写入队列
        private static BlockingCollection<ProcessRecordWrite> _logDbQueue = new BlockingCollection<ProcessRecordWrite>();
        private Task _logDbWriterTask;
        private sealed class FileLogWrite
        {
            public DateTime Timestamp { get; set; }
            public string Text { get; set; }
        }
        private readonly BlockingCollection<FileLogWrite> _fileLogQueue =
            new BlockingCollection<FileLogWrite>(10000);
        private Task _fileLogWriterTask;

        // 在构造函数中启动后台写入线程
        private void InitLogDbWriter()
        {
            _logDbWriterTask = Task.Run(() =>
            {
                using (var conn = new SQLiteConnection(DatabaseHelper.GetConnectionString()))
                {
                    conn.Open();
                    foreach (ProcessRecordWrite record in _logDbQueue.GetConsumingEnumerable())
                    {
                        try
                        {
                            const string sql = @"
INSERT INTO FileProcessRecord
    (MachineId, FileName, Status, RecordCount, ErrorMsg, ProcessTime, Duration, ParserVersionId)
VALUES
    (@MachineId, @FileName, @Status, @RecordCount, @ErrorMsg, @ProcessTime, @Duration, @ParserVersionId);";
                            using (var cmd = new SQLiteCommand(sql, conn))
                            {
                                cmd.Parameters.AddWithValue("@MachineId", record.MachineId);
                                cmd.Parameters.AddWithValue("@FileName", record.FileName);
                                cmd.Parameters.AddWithValue("@Status", record.Status);
                                cmd.Parameters.AddWithValue("@RecordCount", record.RecordCount);
                                cmd.Parameters.AddWithValue("@ErrorMsg", (object)record.ErrorMsg ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@ProcessTime", record.ProcessTime);
                                cmd.Parameters.AddWithValue("@Duration", record.Duration);
                                cmd.Parameters.AddWithValue("@ParserVersionId", (object)record.ParserVersionId ?? DBNull.Value);
                                cmd.CommandTimeout = 5;
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

        private void InitFileLogWriter()
        {
            _fileLogWriterTask = Task.Run(() =>
            {
                foreach (FileLogWrite record in _fileLogQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                        Directory.CreateDirectory(logDir);
                        string logFilePath = Path.Combine(
                            logDir,
                            record.Timestamp.ToString("yyyy-MM-dd") + ".log");
                        File.AppendAllText(logFilePath, record.Text);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("写入日志文件失败: " + ex.Message);
                    }
                }
            });
        }

        /// <summary>
        /// 保存文件处理记录到 SQLite 数据库
        /// </summary>
        private void SaveProcessRecord(
            int machineId,
            string fileName,
            bool success,
            int recordCount,
            string errorMsg,
            DateTime startTime,
            long? parserVersionId)
        {
            try
            {
                int duration = (int)(DateTime.Now - startTime).TotalMilliseconds;
                string status = success ? "成功" : "失败";

                _logDbQueue.Add(new ProcessRecordWrite
                {
                    MachineId = machineId,
                    FileName = fileName,
                    Status = status,
                    RecordCount = recordCount,
                    ErrorMsg = errorMsg,
                    ProcessTime = DateTime.Now,
                    Duration = duration,
                    ParserVersionId = parserVersionId
                });

                AddLog($"[机台{machineId}] 处理记录已加入队列 (状态:{status}, 记录数:{recordCount}, 耗时:{duration}ms)", LogLevel.Info);
            }
            catch (Exception ex)
            {
                AddLog($"保存处理记录失败: {ex.Message}", LogLevel.Error);
            }
        }

        private sealed class ProcessRecordWrite
        {
            public int MachineId { get; set; }
            public string FileName { get; set; }
            public string Status { get; set; }
            public int RecordCount { get; set; }
            public string ErrorMsg { get; set; }
            public DateTime ProcessTime { get; set; }
            public int Duration { get; set; }
            public long? ParserVersionId { get; set; }
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
        private const int MaximumVisibleLogCharacters = 200000;

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

            _fileLogQueue.TryAdd(new FileLogWrite
            {
                Timestamp = DateTime.Now,
                Text = fileLogText
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
                        TrimVisibleLogs();
                        richTextBox1.ScrollToCaret();
                    }));
                }
                else
                {
                    foreach (var logText in logsToShow)
                    {
                        richTextBox1.AppendText(logText);
                    }
                    TrimVisibleLogs();
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
            public Guid FileGroupId { get; set; }
            public Guid PendingUploadId { get; set; }
            public int MachineId { get; set; }
            public int ModelId { get; set; }
            public object Model { get; set; }
            public CancellationToken CancellationToken { get; set; }
            public bool IsReadyToPersist { get; set; }
            public int AttemptCount { get; set; }
            public DateTime NextAttemptAt { get; set; }
            public UploadCommitSignal CommitSignal { get; set; }
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
            _batchTimer.Tick += (s, e) =>
            {
                Task.Run(() => FlushBatchAsync());
            };
            _batchTimer.Start();
            // 只打印一次启动日志
            AddLog($"批量插入定时器已启动，间隔 {_batchInterval}ms，阈值 {_batchSize} 条", LogLevel.Info);
        }

        /// <summary>
        /// 添加数据到批量队列
        /// </summary>
        private IReadOnlyList<BatchItem> AddBatchGroup(
            int machineId,
            int modelId,
            string filePath,
            IReadOnlyList<object> models,
            CancellationToken cancellationToken)
        {
            if (models == null || models.Count == 0)
                throw new InvalidOperationException("同一文件批次不能为空。");

            lock (_batchLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Guid fileGroupId = Guid.NewGuid();
                Guid pendingUploadId = _pendingUploadStore.Enqueue(machineId, filePath, modelId);
                var commitSignal = new UploadCommitSignal();
                var batchItems = models.Select(model => new BatchItem
                {
                    FileGroupId = fileGroupId,
                    PendingUploadId = pendingUploadId,
                    MachineId = machineId,
                    ModelId = modelId,
                    Model = model ?? throw new InvalidOperationException("同一文件批次中包含空记录。"),
                    CancellationToken = cancellationToken,
                    IsReadyToPersist = false,
                    AttemptCount = 0,
                    NextAttemptAt = DateTime.Now,
                    CommitSignal = commitSignal
                }).ToList();

                _batchDataList.AddRange(batchItems);
                AddLog($"加入文件批次 {fileGroupId:N}，批次 {batchItems.Count} 条，当前队列长度: {_batchDataList.Count}", LogLevel.Info);
                return batchItems;
            }
        }

        private static Task WaitForBatchGroupPersistenceAsync(
            IReadOnlyList<BatchItem> batchItems,
            CancellationToken cancellationToken)
        {
            if (batchItems == null || batchItems.Count == 0)
                return Task.CompletedTask;
            return batchItems[0].CommitSignal.WaitAsync(cancellationToken);
        }

        private void MarkBatchGroupReady(IReadOnlyList<BatchItem> batchItems, CancellationToken cancellationToken)
        {
            if (batchItems == null || batchItems.Count == 0) return;

            lock (_batchLock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (BatchItem batchItem in batchItems)
                {
                    if (!_batchDataList.Contains(batchItem))
                        throw new InvalidOperationException("文件批次在标记可入库前已不完整。");
                }

                foreach (BatchItem batchItem in batchItems)
                    batchItem.IsReadyToPersist = true;

                int readyCount = _batchDataList.Count(item => item.IsReadyToPersist);
                if (readyCount >= _batchSize)
                {
                    AddLog($"可入库数据达到批量阈值 {_batchSize}，触发批量插入", LogLevel.Info);
                    Task.Run(() => FlushBatchAsync());
                }
            }
        }

        private void RemoveBatchGroup(IReadOnlyList<BatchItem> batchItems)
        {
            if (batchItems == null || batchItems.Count == 0) return;

            lock (_batchLock)
            {
                var itemSet = new HashSet<BatchItem>(batchItems);
                _batchDataList.RemoveAll(itemSet.Contains);
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
        private async Task FlushBatchAsync(bool waitForGate = false)
        {
            if (waitForGate)
                await _batchFlushGate.WaitAsync();
            else if (!await _batchFlushGate.WaitAsync(0))
                return;

            List<BatchItem> dataToSave = null;
            bool transactionCommitted = false;
            try
            {
                lock (_batchLock)
                {
                    _batchDataList.RemoveAll(
                        x => !x.IsReadyToPersist && x.CancellationToken.IsCancellationRequested);
                    dataToSave = SelectDueBatchItems(DateTime.Now);
                    if (dataToSave.Count == 0) return;
                    var selected = new HashSet<BatchItem>(dataToSave);
                    _batchDataList.RemoveAll(selected.Contains);
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
                    var groups = dataToSave.GroupBy(x => new
                    {
                        ModelType = x.Model.GetType(),
                        x.ModelId
                    }).ToList();
                    var provisioner = new TargetTableProvisioner();
                    var schemaLoader = new TargetTableSchemaLoader(DatabaseHelper.GetConnectionString());
                    foreach (var group in groups)
                    {
                        TargetTableProvisionResult provision = provisioner.EnsureTable(
                            db,
                            group.Key.ModelType,
                            schemaLoader.Load(group.Key.ModelId));
                        if (provision.Created)
                            AddLog("已自动创建主数据库目标表：" + provision.TableName, LogLevel.Success);
                        else if (provision.AdjustedColumnCount > 0)
                            AddLog(
                                string.Format("已按模型配置修正目标表 {0} 的 {1} 个字段", provision.TableName, provision.AdjustedColumnCount),
                                LogLevel.Success);
                    }

                    db.Ado.BeginTran();
                    try
                    {
                        int insertedRows = 0;

                        foreach (var group in groups)
                        {
                            var list = group.Select(x => x.Model).ToList();
                            foreach (object model in list)
                                ModelIdentityInitializer.EnsureCid(model, () => YitIdHelper.NextId());
                            AddLog($"插入类型 {group.Key.ModelType.Name}，共 {list.Count} 条", LogLevel.Info);

                            int result = await db
                                .InsertableByObject(list)
                                .ExecuteCommandAsync();
                            insertedRows += result;
                        }

                        db.Ado.CommitTran();
                        transactionCommitted = true;
                        AddLog($"批量事务提交成功，影响行数: {insertedRows}", LogLevel.Success);
                    }
                    catch
                    {
                        db.Ado.RollbackTran();
                        throw;
                    }
                }

                CompleteBatchGroups(dataToSave);
            }
            catch (Exception ex)
            {
                if (transactionCommitted)
                {
                    AddLog("数据库已提交，但清理本地待入库状态失败: " + ex.Message, LogLevel.Warning);
                    MarkBatchGroupsCommitted(dataToSave);
                }
                else
                {
                    HandleBatchFailure(dataToSave, ex);
                }
            }
            finally
            {
                _batchFlushGate.Release();
            }
        }

        private void TrimVisibleLogs()
        {
            int removalLength = LogRetentionPolicy.GetRemovalLength(
                richTextBox1.TextLength,
                MaximumVisibleLogCharacters);
            if (removalLength <= 0) return;
            richTextBox1.Select(0, removalLength);
            richTextBox1.SelectedText = string.Empty;
            richTextBox1.SelectionStart = richTextBox1.TextLength;
        }

        private List<BatchItem> SelectDueBatchItems(DateTime now)
        {
            var selected = new List<BatchItem>();
            var dueGroups = _batchDataList
                .Where(item => item.IsReadyToPersist &&
                               !item.CancellationToken.IsCancellationRequested &&
                               item.NextAttemptAt <= now)
                .GroupBy(item => item.FileGroupId)
                .OrderBy(group => group.Min(item => item.NextAttemptAt));
            foreach (var group in dueGroups)
            {
                List<BatchItem> items = group.ToList();
                if (selected.Count > 0 && selected.Count + items.Count > _batchSize)
                    break;
                selected.AddRange(items);
                if (selected.Count >= _batchSize)
                    break;
            }
            return selected;
        }

        private void CompleteBatchGroups(IReadOnlyList<BatchItem> batchItems)
        {
            if (batchItems == null) return;
            foreach (var group in batchItems.GroupBy(item => item.FileGroupId))
            {
                BatchItem first = group.First();
                try
                {
                    _pendingUploadStore.Complete(first.PendingUploadId);
                }
                catch (Exception ex)
                {
                    AddLog("清理本地待入库记录失败: " + ex.Message, LogLevel.Warning);
                }
                first.CommitSignal.MarkCommitted();
            }
        }

        private static void MarkBatchGroupsCommitted(IReadOnlyList<BatchItem> batchItems)
        {
            if (batchItems == null) return;
            foreach (var group in batchItems.GroupBy(item => item.FileGroupId))
                group.First().CommitSignal.MarkCommitted();
        }

        private void HandleBatchFailure(IReadOnlyList<BatchItem> batchItems, Exception exception)
        {
            if (batchItems == null || batchItems.Count == 0)
            {
                AddLog("批量插入失败: " + exception.Message, LogLevel.Error);
                return;
            }

            bool transient = DatabaseRetryPolicy.IsTransient(exception);
            var retryItems = new List<BatchItem>();
            foreach (var group in batchItems.GroupBy(item => item.FileGroupId))
            {
                List<BatchItem> items = group.ToList();
                BatchItem first = items[0];
                int attempt = items.Max(item => item.AttemptCount) + 1;
                if (transient)
                {
                    TimeSpan delay = DatabaseRetryPolicy.GetDelay(attempt);
                    DateTime nextAttemptAt = DateTime.Now.Add(delay);
                    foreach (BatchItem item in items)
                    {
                        item.AttemptCount = attempt;
                        item.NextAttemptAt = nextAttemptAt;
                    }
                    try
                    {
                        _pendingUploadStore.RecordTransientFailure(
                            first.PendingUploadId,
                            attempt,
                            nextAttemptAt,
                            exception.Message);
                    }
                    catch (Exception storeException)
                    {
                        AddLog("记录数据库重试状态失败: " + storeException.Message, LogLevel.Warning);
                    }
                    if (!items.Any(item => item.CancellationToken.IsCancellationRequested))
                        retryItems.AddRange(items);
                    AddLog(
                        $"数据库暂时不可用，第 {attempt} 次失败，将在 {delay.TotalSeconds:0} 秒后重试: {exception.Message}",
                        LogLevel.Warning);
                }
                else
                {
                    try
                    {
                        _pendingUploadStore.RecordPermanentFailure(
                            first.PendingUploadId,
                            attempt,
                            exception.Message);
                    }
                    catch (Exception storeException)
                    {
                        AddLog("记录永久入库错误失败: " + storeException.Message, LogLevel.Warning);
                    }
                    first.CommitSignal.MarkPermanentFailure(new InvalidOperationException(
                        "数据库入库失败，错误不可自动重试：" + exception.Message,
                        exception));
                    AddLog("批量插入永久失败，已停止自动重试: " + exception.Message, LogLevel.Error);
                }
            }

            if (retryItems.Count > 0)
            {
                lock (_batchLock)
                {
                    _batchDataList.AddRange(retryItems);
                }
            }
        }

        /// <summary>
        /// 程序关闭时刷新剩余数据
        /// </summary>
        private async Task FlushRemainingData()
        {
            _batchTimer?.Stop();
            await FlushBatchAsync(waitForGate: true);
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_shutdownCompleted)
            {
                base.OnFormClosing(e);
                return;
            }

            if (TrayClosePolicy.ShouldHide(e.CloseReason, _exitRequested))
            {
                e.Cancel = true;
                HideMainWindowToTray();
                return;
            }

            e.Cancel = true;
            if (_shutdownInProgress) return;

            _shutdownInProgress = true;
            Enabled = false;
            try
            {
                _remoteAgentBridge?.Dispose();
                await StopAllMachinesAsync();
                await FlushRemainingData();
                if (!_logDbQueue.IsAddingCompleted)
                    _logDbQueue.CompleteAdding();
                if (_logDbWriterTask != null)
                    await _logDbWriterTask;
                if (!_fileLogQueue.IsAddingCompleted)
                    _fileLogQueue.CompleteAdding();
                if (_fileLogWriterTask != null)
                    await _fileLogWriterTask;

                if (_batchTimer != null)
                {
                    _batchTimer.Stop();
                    _batchTimer.Dispose();
                }
                if (_logFlushTimer != null)
                {
                    _logFlushTimer.Stop();
                    _logFlushTimer.Dispose();
                }
                if (_queueRefreshTimer != null)
                {
                    _queueRefreshTimer.Stop();
                    _queueRefreshTimer.Dispose();
                }
                _pendingUploadStore?.Dispose();
            }
            catch (Exception ex)
            {
                AddLog($"程序关闭清理失败: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                _shutdownCompleted = true;
                _shutdownInProgress = false;
                if (_trayIcon != null)
                    _trayIcon.Visible = false;
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
