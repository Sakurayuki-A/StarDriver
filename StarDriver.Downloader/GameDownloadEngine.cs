using StarDriver.Core.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace StarDriver.Downloader;

/// <summary>
/// 游戏下载引擎 - 核心下载和校验逻辑
/// </summary>
public sealed class GameDownloadEngine : IDisposable
{
    private readonly PSO2HttpClient _httpClient;
    private readonly FileHashCache _hashCache;
    private readonly string _gameDirectory;
    private readonly CancellationTokenSource _cts;
    private readonly ConnectionHealthMonitor _healthMonitor;
    
    private int _concurrentDownloads = 16; // 16并发 + 智能调度策略
    private int _maxRetries = 30; // 增加重试次数
    private bool _isRunning;
    private DateTime _lastHealthCheck = DateTime.UtcNow;

    public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;
    public event EventHandler<FileVerificationEventArgs>? FileVerified;
    public event EventHandler<DownloadCompletedEventArgs>? DownloadCompleted;
    public event EventHandler<ScanProgressEventArgs>? ScanProgress;
    public event EventHandler<DownloadStartedEventArgs>? DownloadStarted;

    public int ConcurrentDownloads
    {
        get => _concurrentDownloads;
        set => _concurrentDownloads = Math.Max(1, Math.Min(32, value)); // 最大支持32并发
    }

    public int MaxRetries
    {
        get => _maxRetries;
        set => _maxRetries = Math.Max(1, value);
    }

    public bool IsRunning => _isRunning;

    public GameDownloadEngine(string baseDirectory, string? cacheDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentNullException(nameof(baseDirectory));
        
        // 创建正确的PSO2目录结构：baseDirectory/PHANTASYSTARONLINE2_JP/pso2_bin/
        var pso2Root = Path.Combine(baseDirectory, "PHANTASYSTARONLINE2_JP");
        _gameDirectory = Path.Combine(pso2Root, "pso2_bin");
        
        // 确保目录存在
        Directory.CreateDirectory(_gameDirectory);
        
        _httpClient = new PSO2HttpClient(cacheDirectory: cacheDirectory);
        
        var cacheFile = Path.Combine(_gameDirectory, "StarDriver.cache.json");
        _hashCache = new FileHashCache(cacheFile);
        
        _healthMonitor = new ConnectionHealthMonitor();
        _cts = new CancellationTokenSource();
    }

    /// <summary>扫描并下载文件</summary>
    public async Task ScanAndDownloadAsync(
        GameClientSelection selection,
        FileScanFlags scanFlags = FileScanFlags.Default,
        CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("Download is already running");

        _isRunning = true;
        
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            var token = linkedCts.Token;

            // 1. 加载缓存
            await _hashCache.LoadAsync(token);

            // 2. 获取补丁列表和服务器配置
            var rootInfo = await _httpClient.GetPatchRootInfoAsync(token);
            
            // 使用服务器建议的配置，但设置合理的最小值
            // 服务器的 ThreadNum 可能是 1（限制单线程），我们使用 ParallelThreadNum 或默认值
            var serverThreads = Math.Max(rootInfo.ThreadNum, rootInfo.ParallelThreadNum);
            _concurrentDownloads = serverThreads > 1 ? Math.Min(serverThreads, 16) : 16; // 16并发 + 智能调度
            _maxRetries = rootInfo.RetryNum;
            
            Console.WriteLine($"[服务器配置] ThreadNum: {rootInfo.ThreadNum}, ParallelThreadNum: {rootInfo.ParallelThreadNum}");
            Console.WriteLine($"[下载配置] 实际并发数: {_concurrentDownloads}, 重试次数: {_maxRetries}, 超时: {rootInfo.TimeOut}ms");
            
            var patchList = await GetPatchListBySelection(selection, rootInfo, token);

            Console.WriteLine($"Total files in patch list: {patchList.Count}");

            // 3. 扫描需要下载的文件
            var downloadQueue = await ScanFilesAsync(patchList, scanFlags, token);

            Console.WriteLine($"Files to download: {downloadQueue.Count}");

            if (downloadQueue.Count == 0)
            {
                OnDownloadCompleted(new DownloadCompletedEventArgs(true, 0, 0, 0));
                return;
            }

            // 触发下载开始事件，传递总文件数
            OnDownloadStarted(new DownloadStartedEventArgs(downloadQueue.Count));

            // 4. 并发下载
            await DownloadFilesAsync(downloadQueue, token);

            // 5. 保存缓存
            await _hashCache.SaveAsync(token);

            // 6. 统计结果
            var succeeded = downloadQueue.Count(d => d.Status == DownloadStatus.Completed);
            var failed = downloadQueue.Count(d => d.Status == DownloadStatus.Failed);
            var cancelled = downloadQueue.Count(d => d.Status == DownloadStatus.Cancelled);

            OnDownloadCompleted(new DownloadCompletedEventArgs(!token.IsCancellationRequested, succeeded, failed, cancelled));
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <summary>取消下载</summary>
    public void Cancel()
    {
        _cts.Cancel();
    }

    private async Task<List<PatchListItem>> GetPatchListBySelection(
        GameClientSelection selection,
        PatchRootInfo rootInfo,
        CancellationToken cancellationToken)
    {
        return selection switch
        {
            GameClientSelection.NGS_Full => await _httpClient.GetPatchListNGSAsync(rootInfo, cancellationToken),
            GameClientSelection.NGS_MainOnly => await _httpClient.GetPatchListNGSMainOnlyAsync(rootInfo, cancellationToken),
            GameClientSelection.Launcher_Only => await _httpClient.GetPatchListLauncherAsync(rootInfo, cancellationToken),
            _ => throw new ArgumentException($"Unsupported selection: {selection}")
        };
    }

    private async Task<List<DownloadItem>> ScanFilesAsync(
        List<PatchListItem> patchList,
        FileScanFlags scanFlags,
        CancellationToken cancellationToken)
    {
        var downloadQueue = new ConcurrentBag<DownloadItem>();
        var totalFiles = patchList.Count;
        var scannedFiles = 0;

        // 限制并发扫描数量，避免资源竞争
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
        
        var scanTasks = patchList.Select(async item =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var localPath = Path.Combine(_gameDirectory, item.GetFilenameWithoutAffix());
                var needsDownload = await ShouldDownloadFileAsync(item, localPath, scanFlags, cancellationToken);

                if (needsDownload)
                {
                    downloadQueue.Add(new DownloadItem(item, localPath));
                }

                var current = Interlocked.Increment(ref scannedFiles);
                
                // 减少进度报告频率，每100个文件报告一次
                if (current % 100 == 0 || current == totalFiles)
                {
                    OnScanProgress(new ScanProgressEventArgs(current, totalFiles));
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(scanTasks);
        
        // 确保最终进度是100%
        OnScanProgress(new ScanProgressEventArgs(totalFiles, totalFiles));

        return downloadQueue.ToList();
    }

    private async Task<bool> ShouldDownloadFileAsync(
        PatchListItem item,
        string localPath,
        FileScanFlags scanFlags,
        CancellationToken cancellationToken)
    {
        // 文件不存在
        if (!File.Exists(localPath))
            return true;

        // 仅检查缺失文件
        if (scanFlags.HasFlag(FileScanFlags.MissingFilesOnly))
            return false;

        var fileInfo = new FileInfo(localPath);
        var filename = item.GetFilenameWithoutAffix();

        // 检查缓存
        if (!scanFlags.HasFlag(FileScanFlags.ForceRefreshCache) &&
            scanFlags.HasFlag(FileScanFlags.CacheOnly))
        {
            if (_hashCache.IsValid(filename, fileInfo.LastWriteTimeUtc, fileInfo.Length))
            {
                return false;
            }
        }

        // 检查文件大小
        if (scanFlags.HasFlag(FileScanFlags.FileSizeMismatch))
        {
            if (fileInfo.Length != item.FileSize)
                return true;
        }

        // 检查 MD5
        if (scanFlags.HasFlag(FileScanFlags.MD5HashMismatch))
        {
            try
            {
                var localMD5 = await FileHashCache.ComputeMD5Async(localPath, cancellationToken: cancellationToken);
                _hashCache.Set(filename, localMD5, fileInfo.Length, fileInfo.LastWriteTimeUtc);

                if (!string.Equals(localMD5, item.MD5Hash, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private async Task DownloadFilesAsync(
        List<DownloadItem> downloadQueue,
        CancellationToken cancellationToken)
    {
        // 使用智能调度器
        var scheduler = new SmartDownloadScheduler();
        scheduler.Enqueue(downloadQueue);
        
        // 分层并发策略：16大 + 6中 + 6小 = 28线程（优化稳定性）
        var largeWorkers = 16;   // 大文件线程
        var mediumWorkers = 6;   // 中等文件线程
        var smallWorkers = 6;    // 小文件线程
        var totalWorkers = largeWorkers + mediumWorkers + smallWorkers;
        
        Console.WriteLine($"[下载引擎] 启动 {totalWorkers} 个并发任务");
        Console.WriteLine($"[分层策略] 大文件:{largeWorkers}线程, 中等:{mediumWorkers}线程, 小文件:{smallWorkers}线程");
        Console.WriteLine($"[智能调度] 空闲线程自动帮忙下载其他类型文件");
        
        var tasks = new List<Task>();
        
        // 16个线程专门下载大文件
        for (int i = 0; i < largeWorkers; i++)
        {
            var taskId = i;
            tasks.Add(DownloadWorkerByTypeAsync(taskId, scheduler, "large", cancellationToken));
        }
        
        // 6个线程专门下载中等文件
        for (int i = 0; i < mediumWorkers; i++)
        {
            var taskId = largeWorkers + i;
            tasks.Add(DownloadWorkerByTypeAsync(taskId, scheduler, "medium", cancellationToken));
        }
        
        // 6个线程专门下载小文件
        for (int i = 0; i < smallWorkers; i++)
        {
            var taskId = largeWorkers + mediumWorkers + i;
            tasks.Add(DownloadWorkerByTypeAsync(taskId, scheduler, "small", cancellationToken));
        }

        await Task.WhenAll(tasks);
        
        Console.WriteLine($"[下载引擎] 所有任务完成");
    }
    
    private async Task DownloadWorkerByTypeAsync(
        int taskId,
        SmartDownloadScheduler scheduler,
        string fileType,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!scheduler.IsEmpty)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                
                DownloadItem? item = null;
                bool hasItem = false;
                
                // 优先从自己的队列获取任务
                if (fileType == "large")
                {
                    hasItem = scheduler.TryDequeueLarge(out item);
                    // 大文件队列空了，帮忙下载中等文件
                    if (!hasItem) hasItem = scheduler.TryDequeueMedium(out item);
                    // 中等也空了，帮忙下载小文件
                    if (!hasItem) hasItem = scheduler.TryDequeueSmall(out item);
                }
                else if (fileType == "medium")
                {
                    hasItem = scheduler.TryDequeueMedium(out item);
                    // 中等队列空了，帮忙下载小文件
                    if (!hasItem) hasItem = scheduler.TryDequeueSmall(out item);
                    // 小文件也空了，帮忙下载大文件
                    if (!hasItem) hasItem = scheduler.TryDequeueLarge(out item);
                }
                else if (fileType == "small")
                {
                    hasItem = scheduler.TryDequeueSmall(out item);
                    // 小文件队列空了，帮忙下载中等文件
                    if (!hasItem) hasItem = scheduler.TryDequeueMedium(out item);
                    // 中等也空了，帮忙下载大文件
                    if (!hasItem) hasItem = scheduler.TryDequeueLarge(out item);
                }
                
                if (hasItem && item != null)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        item.Status = DownloadStatus.Cancelled;
                        continue;
                    }

                    try
                    {
                        await DownloadSingleFileAsync(taskId, item, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // 捕获所有未处理的异常，防止线程崩溃
                        Console.WriteLine($"[线程{taskId}] 未捕获异常: {ex.GetType().Name} - {ex.Message}");
                        _healthMonitor.RecordError($"Unhandled_{ex.GetType().Name}");
                        
                        // 标记文件为失败
                        item.Status = DownloadStatus.Failed;
                        item.ErrorMessage = $"未捕获异常: {ex.Message}";
                        OnFileVerified(new FileVerificationEventArgs(taskId, item, false));
                        
                        // 继续处理下一个文件，不退出线程
                    }
                }
                else
                {
                    // 所有队列都空了，退出
                    if (scheduler.IsEmpty)
                        break;
                        
                    // 队列暂时为空，短暂等待
                    await Task.Delay(50, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 用户取消，正常退出
            Console.WriteLine($"[线程{taskId}] 用户取消下载");
        }
        catch (Exception ex)
        {
            // 最外层异常捕获，确保线程不会崩溃
            Console.WriteLine($"[线程{taskId}] 致命错误: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"[线程{taskId}] 堆栈: {ex.StackTrace}");
            _healthMonitor.RecordError($"Fatal_{ex.GetType().Name}");
        }
        finally
        {
            Console.WriteLine($"[线程{taskId}] 退出 (类型: {fileType})");
        }
    }

    private async Task DownloadWorkerAsync(
        int taskId,
        ConcurrentQueue<DownloadItem> queue,
        CancellationToken cancellationToken)
    {
        try
        {
            while (queue.TryDequeue(out var item))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    item.Status = DownloadStatus.Cancelled;
                    continue;
                }

                // 定期健康检查（每30秒）
                var now = DateTime.UtcNow;
                if ((now - _lastHealthCheck).TotalSeconds >= 30)
                {
                    _lastHealthCheck = now;
                    if (_healthMonitor.ShouldResetConnectionPool())
                    {
                        Console.WriteLine($"[健康监控] {_healthMonitor.GetStatistics()}");
                        Console.WriteLine($"[健康监控] 建议：考虑降低并发数或检查网络连接");
                    }
                }

                try
                {
                    await DownloadSingleFileAsync(taskId, item, cancellationToken);
                }
                catch (Exception ex)
                {
                    // 捕获所有未处理的异常，防止线程崩溃
                    Console.WriteLine($"[线程{taskId}] 未捕获异常: {ex.GetType().Name} - {ex.Message}");
                    _healthMonitor.RecordError($"Unhandled_{ex.GetType().Name}");
                    
                    // 标记文件为失败
                    item.Status = DownloadStatus.Failed;
                    item.ErrorMessage = $"未捕获异常: {ex.Message}";
                    OnFileVerified(new FileVerificationEventArgs(taskId, item, false));
                    
                    // 继续处理下一个文件，不退出线程
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 用户取消，正常退出
            Console.WriteLine($"[线程{taskId}] 用户取消下载");
        }
        catch (Exception ex)
        {
            // 最外层异常捕获，确保线程不会崩溃
            Console.WriteLine($"[线程{taskId}] 致命错误: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"[线程{taskId}] 堆栈: {ex.StackTrace}");
            _healthMonitor.RecordError($"Fatal_{ex.GetType().Name}");
        }
        finally
        {
            Console.WriteLine($"[线程{taskId}] 退出");
        }
    }

    private async Task DownloadSingleFileAsync(
        int taskId,
        DownloadItem item,
        CancellationToken cancellationToken)
    {
        item.Status = DownloadStatus.Downloading;
        var tempPath = item.LocalPath + ".dtmp"; // 使用 .dtmp 与原始 launcher 一致
        var filename = Path.GetFileName(item.LocalPath);

        // 租用缓冲区（参考 PSO2-Launcher-CSharp）
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 32); // 至少 32KB
        var chunkSize = Math.Min(1024 * 64, buffer.Length); // 最大 64KB，平衡吞吐量和进度报告
        
        // 进度报告节流：每256KB或每秒报告一次
        long lastReportedBytes = 0;
        var lastReportTime = DateTime.UtcNow;

        try
        {
            for (int retry = 0; retry < _maxRetries; retry++)
            {
                try
                {
                    // 确保目录存在
                    var directory = Path.GetDirectoryName(item.LocalPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    using var response = await _httpClient.OpenForDownloadAsync(item.PatchInfo, false, cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var statusCode = (int)response.StatusCode;
                        
                        // 4xx 客户端错误 - 标记失败但继续重试循环
                        if (statusCode >= 400 && statusCode < 500)
                        {
                            Console.WriteLine($"[客户端错误] {filename} - HTTP {statusCode} (重试 {retry + 1}/{_maxRetries})");
                            _healthMonitor.RecordError($"HTTP {statusCode}");
                            item.ErrorMessage = $"HTTP {statusCode} {response.StatusCode}";
                            await Task.Delay(2000, cancellationToken); // 延长延迟
                            continue;
                        }
                        
                        // 5xx 服务器错误 - 延迟后重试
                        if (statusCode >= 500 && statusCode < 600)
                        {
                            Console.WriteLine($"[服务器错误] {filename} - HTTP {statusCode} (重试 {retry + 1}/{_maxRetries})");
                            _healthMonitor.RecordError($"HTTP {statusCode}");
                            await Task.Delay(1000, cancellationToken);
                            continue;
                        }
                        
                        Console.WriteLine($"[HTTP错误] {filename} - HTTP {statusCode} (重试 {retry + 1}/{_maxRetries})");
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    // 获取文件大小
                    long remoteSizeInBytes = response.Content.Headers.ContentLength ?? item.PatchInfo.FileSize;

                    using (var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    {
                        // 使用 IncrementalHash 边下载边计算 MD5（参考原始 launcher）
                        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
                        
                        // 使用 FileOptions.Asynchronous 并预分配文件大小（参考原始 launcher）
                        using var fileHandle = File.OpenHandle(
                            tempPath, 
                            FileMode.Create, 
                            FileAccess.Write, 
                            FileShare.Read, 
                            FileOptions.Asynchronous,
                            remoteSizeInBytes > 0 ? remoteSizeInBytes : 0);
                        
                        using (var fileStream = new FileStream(fileHandle, FileAccess.Write, 4096 * 2, true))
                        {
                            long totalRead = 0;
                            int bytesRead;

                            // 边下载边计算哈希
                            while ((bytesRead = await remoteStream.ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken)) > 0)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                // 并行写入和哈希计算
                                var writeTask = fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                                md5.AppendData(buffer, 0, bytesRead);
                                totalRead += bytesRead;
                                
                                await writeTask;
                                
                                item.DownloadedBytes = totalRead;
                                
                                // 节流进度报告：每256KB或每秒报告一次
                                var now = DateTime.UtcNow;
                                if (totalRead - lastReportedBytes >= 256 * 1024 || (now - lastReportTime).TotalSeconds >= 1.0)
                                {
                                    OnDownloadProgress(new DownloadProgressEventArgs(taskId, item, totalRead, remoteSizeInBytes));
                                    lastReportedBytes = totalRead;
                                    lastReportTime = now;
                                }
                            }
                            
                            // 最终进度报告
                            if (totalRead != lastReportedBytes)
                            {
                                OnDownloadProgress(new DownloadProgressEventArgs(taskId, item, totalRead, remoteSizeInBytes));
                            }

                            // 刷新到磁盘
                            fileStream.Flush();
                        } // FileStream 在这里关闭

                        // 获取最终哈希
                        var computedHash = Convert.ToHexString(md5.GetHashAndReset());

                        // 验证 MD5
                        item.Status = DownloadStatus.Verifying;
                        
                        if (string.Equals(computedHash, item.PatchInfo.MD5Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            // 成功，原子替换文件（参考原始 launcher）
                            if (File.Exists(item.LocalPath))
                            {
                                var attrFlags = File.GetAttributes(item.LocalPath);
                                if (attrFlags.HasFlag(FileAttributes.ReadOnly))
                                {
                                    File.SetAttributes(item.LocalPath, attrFlags & ~FileAttributes.ReadOnly);
                                }
                            }
                            
                            File.Move(tempPath, item.LocalPath, true);
                            
                            var fileInfo = new FileInfo(item.LocalPath);
                            _hashCache.Set(
                                item.PatchInfo.GetFilenameWithoutAffix(),
                                computedHash,
                                fileInfo.Length,
                                fileInfo.LastWriteTimeUtc);

                            item.Status = DownloadStatus.Completed;
                            _healthMonitor.RecordSuccess(); // 记录成功
                            OnFileVerified(new FileVerificationEventArgs(taskId, item, true));
                            return;
                        }
                        else
                        {
                            // MD5 不匹配，重试
                            Console.WriteLine($"[MD5错误] {filename} - 期望: {item.PatchInfo.MD5Hash}, 实际: {computedHash} (重试 {retry + 1}/{_maxRetries})");
                            if (File.Exists(tempPath))
                            {
                                File.Delete(tempPath);
                            }
                            await Task.Delay(500, cancellationToken);
                        }
                    } // remoteStream 在这里关闭
                }
                catch (HttpRequestException ex) when (retry < _maxRetries - 1)
                {
                    // HTTP请求异常
                    if (ex.StatusCode.HasValue)
                    {
                        var statusCode = (int)ex.StatusCode.Value;
                        
                        // 4xx 客户端错误 - 记录但继续重试
                        if (statusCode >= 400 && statusCode < 500)
                        {
                            Console.WriteLine($"[客户端错误] {filename} - HTTP {statusCode} (重试 {retry + 1}/{_maxRetries})");
                            _healthMonitor.RecordError($"HTTP {statusCode}");
                            item.ErrorMessage = $"HTTP {statusCode}";
                            if (File.Exists(tempPath)) File.Delete(tempPath);
                            await Task.Delay(2000, cancellationToken);
                            continue;
                        }
                        
                        // 5xx 服务器错误 - 延迟后重试
                        if (statusCode >= 500 && statusCode < 600)
                        {
                            Console.WriteLine($"[服务器错误] {filename} - HTTP {statusCode} (重试 {retry + 1}/{_maxRetries})");
                            _healthMonitor.RecordError($"HTTP {statusCode}");
                            if (File.Exists(tempPath)) File.Delete(tempPath);
                            await Task.Delay(1000, cancellationToken);
                            continue;
                        }
                    }
                    else
                    {
                        // 无状态码的网络错误 - 重试
                        Console.WriteLine($"[网络错误] {filename} - {ex.Message} (重试 {retry + 1}/{_maxRetries})");
                        _healthMonitor.RecordError("NetworkError");
                        item.ErrorMessage = ex.Message;
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                        await Task.Delay(2000, cancellationToken);
                        continue;
                    }
                    
                    Console.WriteLine($"[HTTP异常] {filename} - {ex.Message} (重试 {retry + 1}/{_maxRetries})");
                    _healthMonitor.RecordError("HttpException");
                    item.ErrorMessage = ex.Message;
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    await Task.Delay(1000, cancellationToken);
                }
                catch (System.Net.Sockets.SocketException ex) when (retry < _maxRetries - 1)
                {
                    // Socket异常 - 都重试，因为可能是临时网络问题
                    if (ex.ErrorCode == 10054)
                    {
                        // 连接被远程主机强制关闭 - 短延迟后重试
                        Console.WriteLine($"[连接关闭] {filename} - 连接被远程主机关闭 (重试 {retry + 1}/{_maxRetries})");
                        _healthMonitor.RecordError("ConnectionReset");
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                        await Task.Delay(500, cancellationToken);
                    }
                    else
                    {
                        // 其他Socket错误 - 也重试
                        Console.WriteLine($"[Socket错误] {filename} - ErrorCode {ex.ErrorCode} (重试 {retry + 1}/{_maxRetries})");
                        _healthMonitor.RecordError($"Socket{ex.ErrorCode}");
                        item.ErrorMessage = $"Socket错误: {ex.ErrorCode}";
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                        await Task.Delay(1000, cancellationToken);
                    }
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && retry < _maxRetries - 1)
                {
                    // 超时异常（非用户取消）- 重试
                    Console.WriteLine($"[超时] {filename} - 请求超时 (重试 {retry + 1}/{_maxRetries})");
                    _healthMonitor.RecordError("Timeout");
                    item.ErrorMessage = "请求超时";
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    await Task.Delay(1000, cancellationToken);
                }
                catch (IOException ex) when (retry < _maxRetries - 1)
                {
                    Console.WriteLine($"[IO错误] {filename} - {ex.Message} (重试 {retry + 1}/{_maxRetries})");
                    _healthMonitor.RecordError("IOException");
                    item.ErrorMessage = $"IO错误: {ex.Message}";
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    await Task.Delay(500, cancellationToken);
                }
                catch (Exception ex) when (retry < _maxRetries - 1)
                {
                    Console.WriteLine($"[未知错误] {filename} - {ex.GetType().Name}: {ex.Message} (重试 {retry + 1}/{_maxRetries})");
                    _healthMonitor.RecordError(ex.GetType().Name);
                    item.ErrorMessage = ex.Message;
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    await Task.Delay(1000, cancellationToken);
                }
                catch (Exception ex)
                {
                    // 最后一次重试失败
                    Console.WriteLine($"[最终失败] {filename} - {ex.GetType().Name}: {ex.Message}");
                    _healthMonitor.RecordError($"Final_{ex.GetType().Name}");
                    item.Status = DownloadStatus.Failed;
                    item.ErrorMessage = ex.Message;
                    OnFileVerified(new FileVerificationEventArgs(taskId, item, false));
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    return;
                }
            }

            Console.WriteLine($"[超过重试] {filename} - 已重试 {_maxRetries} 次");
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = $"超过最大重试次数 ({_maxRetries})";
            OnFileVerified(new FileVerificationEventArgs(taskId, item, false));
            
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        finally
        {
            // 归还缓冲区
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void OnDownloadProgress(DownloadProgressEventArgs e) => DownloadProgress?.Invoke(this, e);
    private void OnFileVerified(FileVerificationEventArgs e) => FileVerified?.Invoke(this, e);
    private void OnDownloadCompleted(DownloadCompletedEventArgs e) => DownloadCompleted?.Invoke(this, e);
    private void OnScanProgress(ScanProgressEventArgs e) => ScanProgress?.Invoke(this, e);
    private void OnDownloadStarted(DownloadStartedEventArgs e) => DownloadStarted?.Invoke(this, e);

    public void Dispose()
    {
        _cts?.Dispose();
        _hashCache?.Dispose();
        _httpClient?.Dispose();
    }
}

// 事件参数类
public class DownloadProgressEventArgs : EventArgs
{
    public int TaskId { get; }
    public DownloadItem Item { get; }
    public long BytesDownloaded { get; }
    public long TotalBytes { get; }
    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;

    public DownloadProgressEventArgs(int taskId, DownloadItem item, long bytesDownloaded, long totalBytes)
    {
        TaskId = taskId;
        Item = item;
        BytesDownloaded = bytesDownloaded;
        TotalBytes = totalBytes;
    }
}

public class FileVerificationEventArgs : EventArgs
{
    public int TaskId { get; }
    public DownloadItem Item { get; }
    public bool IsValid { get; }

    public FileVerificationEventArgs(int taskId, DownloadItem item, bool isValid)
    {
        TaskId = taskId;
        Item = item;
        IsValid = isValid;
    }
}

public class DownloadCompletedEventArgs : EventArgs
{
    public bool Success { get; }
    public int SucceededCount { get; }
    public int FailedCount { get; }
    public int CancelledCount { get; }

    public DownloadCompletedEventArgs(bool success, int succeededCount, int failedCount, int cancelledCount)
    {
        Success = success;
        SucceededCount = succeededCount;
        FailedCount = failedCount;
        CancelledCount = cancelledCount;
    }
}

public class ScanProgressEventArgs : EventArgs
{
    public int ScannedFiles { get; }
    public int TotalFiles { get; }
    public double ProgressPercentage => TotalFiles > 0 ? (double)ScannedFiles / TotalFiles * 100 : 0;

    public ScanProgressEventArgs(int scannedFiles, int totalFiles)
    {
        ScannedFiles = scannedFiles;
        TotalFiles = totalFiles;
    }
}

public class DownloadStartedEventArgs : EventArgs
{
    public int TotalFilesToDownload { get; }

    public DownloadStartedEventArgs(int totalFilesToDownload)
    {
        TotalFilesToDownload = totalFilesToDownload;
    }
}
