using StarDriver.Core.Models;
using System.Collections.Concurrent;

namespace StarDriver.Downloader;

/// <summary>
/// 智能任务调度器 - 基于节点速度动态分配任务
/// </summary>
public sealed class SmartTaskScheduler
{
    private readonly List<DownloadNode> _nodes;
    private readonly ConcurrentQueue<DownloadTask> _taskQueue = new();
    private readonly ConcurrentDictionary<string, List<DownloadTask>> _fileChunks = new(); // 跟踪文件的所有块
    private readonly ConcurrentDictionary<string, HashSet<DownloadTask>> _completedChunks = new(); // 已完成的块
    private readonly object _lock = new();
    
    // 动态块大小配置
    private long _currentLargeChunkSize = 10 * 1024 * 1024;  // 10MB
    private long _currentMediumChunkSize = 2 * 1024 * 1024;  // 2MB
    private long _currentSmallChunkSize = 500 * 1024;        // 500KB
    
    private DateTime _lastChunkSizeAdjustment = DateTime.UtcNow;
    private double _recentAverageSpeed;
    
    public int TotalTasksInQueue => _taskQueue.Count;
    public int ActiveNodes => _nodes.Count(n => !n.IsIdle);
    
    public SmartTaskScheduler(int nodeCount)
    {
        _nodes = Enumerable.Range(0, nodeCount)
            .Select(i => new DownloadNode(i))
            .ToList();
    }
    
    /// <summary>初始化任务队列 - 将文件列表转换为任务</summary>
    public void InitializeTaskQueue(List<DownloadItem> downloadItems)
    {
        foreach (var item in downloadItems)
        {
            var fileSize = item.PatchInfo.FileSize;
            var filePath = item.LocalPath;
            
            // 小文件直接作为单个任务
            if (fileSize < 1 * 1024 * 1024) // < 1MB
            {
                var task = new DownloadTask(item);
                _taskQueue.Enqueue(task);
                
                // 记录文件块
                _fileChunks[filePath] = new List<DownloadTask> { task };
                _completedChunks[filePath] = new HashSet<DownloadTask>();
            }
            else
            {
                // 大文件分块
                var chunks = SplitFileIntoChunks(item, fileSize);
                
                // 记录文件块
                _fileChunks[filePath] = chunks;
                _completedChunks[filePath] = new HashSet<DownloadTask>();
                
                foreach (var chunk in chunks)
                {
                    _taskQueue.Enqueue(chunk);
                }
            }
        }
        
        Console.WriteLine($"[调度器] 初始化完成，共 {_taskQueue.Count} 个任务，{_fileChunks.Count} 个文件");
    }
    
    /// <summary>将大文件分成多个块</summary>
    private List<DownloadTask> SplitFileIntoChunks(DownloadItem item, long fileSize)
    {
        var chunks = new List<DownloadTask>();
        
        // 根据文件大小决定块大小
        long chunkSize = fileSize switch
        {
            > 100 * 1024 * 1024 => _currentLargeChunkSize,  // > 100MB 用大块
            > 10 * 1024 * 1024 => _currentMediumChunkSize,  // 10-100MB 用中块
            _ => _currentSmallChunkSize                      // 1-10MB 用小块
        };
        
        long offset = 0;
        while (offset < fileSize)
        {
            var end = Math.Min(offset + chunkSize, fileSize);
            chunks.Add(new DownloadTask(item, offset, end));
            offset = end;
        }
        
        return chunks;
    }
    
    /// <summary>为节点分配最合适的任务</summary>
    public DownloadTask? AssignTaskToNode(int nodeId)
    {
        lock (_lock)
        {
            var node = _nodes[nodeId];
            
            // 根据节点速度等级选择合适的任务
            var suitableTasks = new List<DownloadTask>();
            
            foreach (var task in _taskQueue)
            {
                if (IsTaskSuitableForNode(task, node))
                {
                    suitableTasks.Add(task);
                }
            }
            
            // 选择最合适的任务
            var selectedTask = SelectBestTask(suitableTasks, node);
            
            if (selectedTask != null)
            {
                // 从队列中移除
                var tempQueue = new ConcurrentQueue<DownloadTask>();
                while (_taskQueue.TryDequeue(out var task))
                {
                    if (task != selectedTask)
                    {
                        tempQueue.Enqueue(task);
                    }
                }
                
                // 重新入队未选中的任务
                while (tempQueue.TryDequeue(out var task))
                {
                    _taskQueue.Enqueue(task);
                }
                
                node.CurrentTask = selectedTask;
                selectedTask.StartTime = DateTime.UtcNow;
                
                Console.WriteLine($"[Node {nodeId}] 分配任务: {Path.GetFileName(selectedTask.Item.LocalPath)} " +
                    $"Range: {selectedTask.RangeStart}-{selectedTask.RangeEnd} ({selectedTask.TaskSize / 1024.0:F1} KB, " +
                    $"优先级: {selectedTask.Priority}, 节点速度: {node.SpeedTier})");
            }
            
            return selectedTask;
        }
    }
    
    /// <summary>判断任务是否适合节点</summary>
    private bool IsTaskSuitableForNode(DownloadTask task, DownloadNode node)
    {
        return node.SpeedTier switch
        {
            NodeSpeedTier.Fast => true, // 快节点可以处理任何任务
            NodeSpeedTier.Medium => task.Priority <= TaskPriority.Medium, // 中等节点处理中小任务
            NodeSpeedTier.Slow => task.Priority == TaskPriority.Small, // 慢节点只处理小任务
            NodeSpeedTier.Unknown => true, // 未知节点可以处理任何任务（初始阶段需要测速）
            _ => false
        };
    }
    
    /// <summary>选择最佳任务</summary>
    private DownloadTask? SelectBestTask(List<DownloadTask> suitableTasks, DownloadNode node)
    {
        if (suitableTasks.Count == 0)
        {
            // 如果没有合适的任务，尝试从队列中获取任何任务
            return _taskQueue.TryPeek(out var anyTask) ? anyTask : null;
        }
        
        // 快节点优先大任务，慢节点优先小任务
        return node.SpeedTier switch
        {
            NodeSpeedTier.Fast => suitableTasks.OrderByDescending(t => t.TaskSize).First(),
            NodeSpeedTier.Medium => suitableTasks.OrderBy(t => Math.Abs(t.TaskSize - 2 * 1024 * 1024)).First(),
            _ => suitableTasks.OrderBy(t => t.TaskSize).First()
        };
    }
    
    /// <summary>任务完成回调</summary>
    public void OnTaskCompleted(int nodeId, bool success, bool wasSlow)
    {
        var node = _nodes[nodeId];
        var task = node.CurrentTask;
        
        if (task != null && success)
        {
            // 记录已完成的块
            var filePath = task.Item.LocalPath;
            if (_completedChunks.TryGetValue(filePath, out var completed))
            {
                completed.Add(task);
                
                // 检查文件是否所有块都完成
                if (_fileChunks.TryGetValue(filePath, out var allChunks))
                {
                    if (completed.Count == allChunks.Count)
                    {
                        Console.WriteLine($"[调度器] 文件所有块已完成: {Path.GetFileName(filePath)} ({completed.Count}/{allChunks.Count})");
                    }
                }
            }
        }
        
        node.OnTaskCompleted(success, wasSlow);
    }
    
    /// <summary>检查文件是否所有块都已完成</summary>
    public bool IsFileCompleted(string filePath)
    {
        if (_completedChunks.TryGetValue(filePath, out var completed) &&
            _fileChunks.TryGetValue(filePath, out var allChunks))
        {
            return completed.Count == allChunks.Count;
        }
        return false;
    }
    
    /// <summary>获取文件的所有已完成块（按顺序）</summary>
    public List<DownloadTask> GetCompletedChunks(string filePath)
    {
        if (_completedChunks.TryGetValue(filePath, out var completed))
        {
            return completed.OrderBy(t => t.RangeStart).ToList();
        }
        return new List<DownloadTask>();
    }
    
    /// <summary>更新节点速度</summary>
    public void UpdateNodeSpeed(int nodeId, long totalBytesDownloaded)
    {
        _nodes[nodeId].UpdateSpeed(totalBytesDownloaded);
    }
    
    /// <summary>检查并处理卡住的任务</summary>
    public List<(int NodeId, DownloadTask Task)> CheckStuckTasks()
    {
        var stuckTasks = new List<(int, DownloadTask)>();
        
        foreach (var node in _nodes)
        {
            if (node.CurrentTask != null && node.CurrentTask.IsStuck())
            {
                Console.WriteLine($"[Node {node.NodeId}] 任务卡住: {Path.GetFileName(node.CurrentTask.Item.LocalPath)}");
                stuckTasks.Add((node.NodeId, node.CurrentTask));
            }
        }
        
        return stuckTasks;
    }
    
    /// <summary>重新分配卡住的任务（切分成小块）</summary>
    public void ReassignStuckTask(int nodeId, DownloadTask stuckTask)
    {
        lock (_lock)
        {
            var node = _nodes[nodeId];
            
            // 如果任务还有剩余部分，切分成小块重新入队
            var remainingStart = stuckTask.RangeStart + stuckTask.BytesDownloaded;
            var remainingSize = stuckTask.RangeEnd - remainingStart;
            
            if (remainingSize > _currentSmallChunkSize)
            {
                // 切分成小块
                var smallChunkSize = _currentSmallChunkSize;
                var offset = remainingStart;
                
                while (offset < stuckTask.RangeEnd)
                {
                    var end = Math.Min(offset + smallChunkSize, stuckTask.RangeEnd);
                    var newTask = new DownloadTask(stuckTask.Item, offset, end)
                    {
                        Priority = TaskPriority.Small // 强制设为小任务
                    };
                    _taskQueue.Enqueue(newTask);
                    offset = end;
                }
                
                Console.WriteLine($"[调度器] 卡住任务已切分为 {(remainingSize / smallChunkSize) + 1} 个小块");
            }
            else
            {
                // 剩余部分太小，直接重新入队
                _taskQueue.Enqueue(new DownloadTask(stuckTask.Item, remainingStart, stuckTask.RangeEnd));
            }
            
            node.CurrentTask = null;
        }
    }
    
    /// <summary>动态调整块大小（根据整体速度）</summary>
    public void AdjustChunkSizes(double totalSpeedBytesPerSecond)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastChunkSizeAdjustment).TotalSeconds < 10)
            return; // 每10秒调整一次
        
        _recentAverageSpeed = totalSpeedBytesPerSecond;
        var speedMBps = totalSpeedBytesPerSecond / (1024 * 1024);
        
        // 如果总速度低，缩小块大小
        if (speedMBps < 2.0)
        {
            _currentLargeChunkSize = Math.Max(2 * 1024 * 1024, _currentLargeChunkSize / 2);
            _currentMediumChunkSize = Math.Max(1 * 1024 * 1024, _currentMediumChunkSize / 2);
            _currentSmallChunkSize = Math.Max(256 * 1024, _currentSmallChunkSize / 2);
            
            Console.WriteLine($"[调度器] 降低块大小（总速度: {speedMBps:F2} MB/s）");
        }
        // 如果总速度高，增大块大小
        else if (speedMBps > 5.0)
        {
            _currentLargeChunkSize = Math.Min(20 * 1024 * 1024, _currentLargeChunkSize * 2);
            _currentMediumChunkSize = Math.Min(5 * 1024 * 1024, _currentMediumChunkSize * 2);
            
            Console.WriteLine($"[调度器] 提高块大小（总速度: {speedMBps:F2} MB/s）");
        }
        
        _lastChunkSizeAdjustment = now;
    }
    
    /// <summary>获取节点统计信息</summary>
    public string GetStatistics()
    {
        var fastNodes = _nodes.Count(n => n.SpeedTier == NodeSpeedTier.Fast);
        var mediumNodes = _nodes.Count(n => n.SpeedTier == NodeSpeedTier.Medium);
        var slowNodes = _nodes.Count(n => n.SpeedTier == NodeSpeedTier.Slow);
        var avgSpeed = _nodes.Where(n => !n.IsIdle).Average(n => n.AverageSpeedBytesPerSecond) / 1024.0;
        
        return $"节点: 快={fastNodes} 中={mediumNodes} 慢={slowNodes} | " +
               $"活跃: {ActiveNodes}/{_nodes.Count} | " +
               $"队列: {TotalTasksInQueue} | " +
               $"平均速度: {avgSpeed:F1} KB/s";
    }
}
