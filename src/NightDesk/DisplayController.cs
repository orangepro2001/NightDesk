using System.Runtime.InteropServices;
using System.Windows;

namespace NightDesk;

public sealed class DisplayController : IDisposable
{
    private readonly VddClient vdd = new();
    private readonly SettingsStore store;
    private readonly AppSettings settings;
    private readonly Dictionary<int, VirtualDisplayInfo> displays = new();
    private readonly SleepInhibitor sleep = new();
    private RecoverySnapshot? recovery;
    private int? remoteIndex;
    private bool busy;
    private bool automaticRemoteDisplay;
    public bool IsRemoteMode => recovery is not null;
    public bool PreventSleep => sleep.IsActive;
    public IReadOnlyCollection<VirtualDisplayInfo> Displays => displays.Values.ToArray();
    public bool HasRecoveryFile => store.LoadRecovery() is not null;
    public event Action? Changed;

    public DisplayController(SettingsStore store, AppSettings settings) { this.store = store; this.settings = settings; }

    public object Status() => new
    {
        remoteMode = IsRemoteMode, preventSleep = PreventSleep, busy,
        managedDisplays = Displays, monitors = NativeDisplays.Enumerate(),
        configuredMode = new DisplayMode(settings.Width, settings.Height, settings.RefreshRate),
        hotkey = settings.Hotkey, recoverable = HasRecoveryFile
    };

    /// <summary>
    /// 启动时接管“上一个实例已经切到虚拟屏”的现场：驱动仍保留着虚拟屏时继续保活，
    /// 避免重启导致虚拟屏被驱动看门狗删除、远程画面中断。只有拓扑确实为“仅一块虚拟屏”时才接管。
    /// </summary>
    public bool TryAdoptRemoteState(out string note)
    {
        note = "";
        if (IsRemoteMode) return true;
        RecoverySnapshot? snapshot;
        try { snapshot = store.LoadRecovery(); }
        catch (Exception ex) { note = "恢复快照无法读取：" + ex.Message; return false; }
        if (snapshot is null) return false;
        var monitors = NativeDisplays.Enumerate();
        var parsec = monitors.Where(x => x.IsParsec).ToArray();
        var active = parsec.Where(x => x.Active).ToArray();
        if (active.Length != 1 || NativeDisplays.HasActiveHardwareOutput(monitors)) return false;
        var target = active[0];
        int index = Array.FindIndex(parsec, x => x.DeviceName.Equals(target.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) { note = "无法定位现有虚拟屏的驱动索引。"; return false; }
        try { vdd.AdoptDisplay(index); }
        catch (Exception ex)
        {
            RemoteDiagnostics.Log("adopt display failed", ex);
            note = "接管现有虚拟屏失败：" + ex.Message;
            return false;
        }
        var mode = NativeDisplays.Current(target.DeviceName);
        displays[index] = new VirtualDisplayInfo(index, target.DeviceName,
            mode is null ? "虚拟显示" : new DisplayMode(mode.Width, mode.Height, mode.RefreshRate).ToString());
        remoteIndex = index;
        automaticRemoteDisplay = true;
        recovery = snapshot;
        RemoteDiagnostics.Log($"adopted remote display={target.DeviceName} index={index} snapshot={snapshot.CreatedUtc:O}");
        note = $"已接管上次远程会话中的虚拟屏 {target.DeviceName}；物理屏恢复前请保持程序运行。";
        Changed?.Invoke();
        return true;
    }

    public async Task<VirtualDisplayInfo> AddAsync(DisplayMode requested)
    {
        if (busy) throw new InvalidOperationException("显示操作正在进行，请稍后重试。");
        if (IsRemoteMode) throw new InvalidOperationException("请先点亮并恢复布局，再添加虚拟显示器。");
        busy = true;
        try { return await AddCoreAsync(requested); }
        finally { busy = false; Changed?.Invoke(); }
    }

    private async Task<VirtualDisplayInfo> AddCoreAsync(DisplayMode requested)
    {
        requested.Validate();
        if (SystemParameters.IsRemoteSession) throw new InvalidOperationException("请在 Windows 控制台用户会话中运行；RDP 的显示拓扑与物理会话不同。");
        if (displays.Count >= 3) throw new InvalidOperationException("NightDesk 最多同时管理 3 个虚拟屏幕。");
        var before = NativeDisplays.Enumerate().Where(x => x.IsParsec && x.Active).Select(x => x.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int index = await Task.Run(() => vdd.AddDisplay());
        try
        {
            MonitorInfo? created = null;
            for (int i = 0; i < 40; i++)
            {
                created = NativeDisplays.Enumerate().FirstOrDefault(x => x.IsParsec && x.Active && !before.Contains(x.DeviceName));
                if (created is not null && NativeDisplays.Current(created.DeviceName) is not null) break;
                await Task.Delay(100);
            }
            if (created is null || !created.Active) throw new InvalidOperationException("驱动已创建屏幕，但 Windows 未在 4 秒内激活它。请检查控制台会话和驱动状态。");
            NativeDisplays.ApplyVirtualMode(created.DeviceName, requested);
            var applied = NativeDisplays.Current(created.DeviceName);
            if (applied is null || applied.Width != requested.Width || applied.Height != requested.Height || applied.RefreshRate != requested.RefreshRate)
                throw new InvalidOperationException("Windows 未应用请求的虚拟显示模式。");
            var info = new VirtualDisplayInfo(index, created.DeviceName, requested.ToString());
            displays.Add(index, info);
            return info;
        }
        catch
        {
            try { await Task.Run(() => vdd.RemoveDisplay(index)); } catch { /* Keep owned index for driver watchdog; original failure is surfaced. */ }
            throw;
        }
    }

    public async Task RemoveAsync(int index)
    {
        if (busy) throw new InvalidOperationException("显示操作正在进行，请稍后重试。");
        if (!displays.ContainsKey(index)) throw new ArgumentException("请选择 NightDesk 创建的虚拟显示器。");
        if (IsRemoteMode) throw new InvalidOperationException("请先点亮并恢复布局，再删除虚拟显示器；保存的布局可能引用此屏幕。");
        if (NativeDisplays.Enumerate().Count(x => x.Active) <= 1) throw new InvalidOperationException("拒绝删除最后一个活动显示器，请先恢复物理显示器。");
        busy = true;
        try { await Task.Run(() => vdd.RemoveDisplay(index)); displays.Remove(index); }
        finally { busy = false; Changed?.Invoke(); }
    }

    public async Task SetRemoteModeAsync(bool enabled)
    {
        if (!enabled && recovery is null && store.LoadRecovery() is not null) { RestoreSaved(); return; }
        if (enabled == IsRemoteMode) return;
        if (busy) throw new InvalidOperationException("显示操作正在进行，请稍后重试。");
        busy = true;
        try
        {
            if (enabled)
            {
                if (SystemParameters.IsRemoteSession) throw new InvalidOperationException("RDP 会话不支持此操作；请在控制台会话运行 NightDesk。");
                if (store.LoadRecovery() is not null) throw new InvalidOperationException("存在尚未处理的恢复快照。请先点击“恢复保存布局”。");
                var monitors = NativeDisplays.Enumerate();
                if (!NativeDisplays.HasActiveHardwareOutput(monitors)) throw new InvalidOperationException("未找到活动物理显示器，拒绝切换。");
                var snapshot = DisplayTopology.Capture();
                store.SaveRecovery(snapshot);
                recovery = snapshot;
                try
                {
                    var chosen = displays.Values.FirstOrDefault();
                    automaticRemoteDisplay = chosen is null;
                    chosen ??= await AddCoreAsync(new DisplayMode(settings.Width, settings.Height, settings.RefreshRate));
                    remoteIndex = chosen.Index;
                    DisplayTopology.UseOnly(chosen.DeviceName);
                }
                catch (Exception operationError)
                {
                    try
                    {
                        DisplayTopology.Restore(snapshot);
                        VerifyRestored(snapshot);
                        if (automaticRemoteDisplay && remoteIndex is int index && displays.ContainsKey(index))
                        { await Task.Run(() => vdd.RemoveDisplay(index)); displays.Remove(index); }
                        remoteIndex = null; automaticRemoteDisplay = false;
                        store.ClearRecovery(); recovery = null;
                    }
                    catch (Exception restoreError) { throw new AggregateException("切换失败，自动恢复也失败。快照已保留，请使用“恢复保存布局”。", operationError, restoreError); }
                    throw;
                }
            }
            else
            {
                var snapshot = recovery ?? store.LoadRecovery() ?? throw new InvalidOperationException("没有保存的布局。");
                DisplayTopology.Restore(snapshot);
                VerifyRestored(snapshot);
                if (automaticRemoteDisplay && remoteIndex is int index && displays.ContainsKey(index))
                { await Task.Run(() => vdd.RemoveDisplay(index)); displays.Remove(index); }
                remoteIndex = null; automaticRemoteDisplay = false;
                store.ClearRecovery(); recovery = null;
            }
        }
        finally { busy = false; Changed?.Invoke(); }
    }

    public void RestoreSaved()
    {
        if (busy) throw new InvalidOperationException("显示操作正在进行。");
        var snapshot = recovery ?? store.LoadRecovery() ?? throw new InvalidOperationException("没有待恢复的显示布局。");
        DisplayTopology.Restore(snapshot);
        VerifyRestored(snapshot);
        if (automaticRemoteDisplay && remoteIndex is int index && displays.ContainsKey(index))
        { vdd.RemoveDisplay(index); displays.Remove(index); }
        remoteIndex = null; automaticRemoteDisplay = false;
        store.ClearRecovery(); recovery = null;
        Changed?.Invoke();
    }

    private static void VerifyRestored(RecoverySnapshot snapshot)
    {
        var expected = DisplayTopology.ActiveSourceNames(snapshot);
        var actual = DisplayTopology.ActiveSourceNames(DisplayTopology.Capture());
        if (!expected.All(x => actual.Contains(x, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("部分原显示输出尚未恢复；快照已保留。");
    }

    public void SetPreventSleep(bool enabled)
    {
        sleep.Set(enabled); settings.PreventSleep = enabled; store.Save(settings); Changed?.Invoke();
    }

    public void Dispose()
    {
        // Restore before ending Parsec keepalive. A failure must keep the process alive for user recovery.
        if (recovery is not null || store.LoadRecovery() is not null) RestoreSaved();
        var ownedNames = displays.Values.Select(x => x.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (vdd.OwnedIndices.Count > 0 && !NativeDisplays.HasActiveHardwareOutput(NativeDisplays.Enumerate().Where(x => !ownedNames.Contains(x.DeviceName))))
            throw new InvalidOperationException("没有独立的活动物理显示器，请先在 Windows 中启用物理显示器，再退出。");
        foreach (var index in vdd.OwnedIndices) { vdd.RemoveDisplay(index); displays.Remove(index); }
        vdd.Dispose(); sleep.Dispose();
    }
}
