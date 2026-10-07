// ============================================================================
// MemIO.cs — 读写原语：SafeWrite 页校验（committed+writable+非guard+非MEM_IMAGE）
//            + 写入后回读校验。训练器所有内存写入统一经此出口。
// ============================================================================
using System;
using System.Runtime.InteropServices;

namespace Ib3Trainer2 {

static class MemIO {
  // ---------- 错误码友好化 ----------
  public static string FriendlyErr(int err) {
    switch (err) {
      case 299: return "地址无效或已失效（部分读取失败）";
      case 487: return "无效地址（未分配）";
      case 5: return "拒绝访问（权限不足）";
      case 6: return "句柄无效";
      case 87: return "参数错误";
      case 8: return "内存不足";
    }
    return "err=" + err;
  }

  // ---------- 原始字节读 ----------
  public static bool ReadBytes(IntPtr h, long addr, byte[] buf, out string err) {
    int r;
    if (Win32.ReadProcessMemory(h, (IntPtr)addr, buf, buf.Length, out r) && r == buf.Length) { err = null; return true; }
    err = "读失败 " + FriendlyErr(Marshal.GetLastWin32Error());
    return false;
  }

  // ---------- 值读（解码为文本） ----------
  public static bool ReadValue(IntPtr h, long addr, ScanType t, out string val, out string err) {
    int size = TypeUtil.Size(t);
    byte[] buf = new byte[size];
    int r;
    if (Win32.ReadProcessMemory(h, (IntPtr)addr, buf, size, out r) && r == size) {
      val = TypeUtil.Decode(t, buf); err = null; return true;
    }
    val = null; err = "读失败 " + FriendlyErr(Marshal.GetLastWin32Error());
    return false;
  }

  // ---------- 页可写判定（写前校验） ----------
  public static bool WritablePage(Win32.MBI m) {
    return m.State == 0x1000              // MEM_COMMIT
        && (m.Protect & 0xCC) != 0         // RW/WC/ERW/EWC 之一
        && (m.Protect & 0x100) == 0        // 非 PAGE_GUARD
        && m.Type != 0x1000000;            // 非 MEM_IMAGE（绝不改写游戏映像）
  }

  // 诊断描述
  public static string RegionDesc(Win32.MBI m) {
    return "state=0x" + m.State.ToString("X") + " protect=0x" + m.Protect.ToString("X") + " type=0x" + m.Type.ToString("X");
  }

  // ---------- SafeWrite：页校验 → 写 → 回读校验 ----------
  public static bool SafeWrite(IntPtr h, long addr, byte[] data, out string err) {
    Win32.MBI m;
    if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) {
      err = "VirtualQuery 失败 err=" + Marshal.GetLastWin32Error(); return false;
    }
    if (!WritablePage(m)) { err = "目标页不可写 (" + RegionDesc(m) + ")"; return false; }
    // 跨区时校验末端页
    long end = addr + data.Length - 1;
    long regionEnd = m.BaseAddress.ToInt64() + m.RegionSize.ToInt64() - 1;
    if (end > regionEnd) {
      Win32.MBI m2;
      if (Win32.VirtualQueryEx(h, (IntPtr)end, out m2, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero || !WritablePage(m2)) {
        err = "跨区末端不可写"; return false;
      }
    }
    int w;
    if (!Win32.WriteProcessMemory(h, (IntPtr)addr, data, data.Length, out w) || w != data.Length) {
      err = "写失败 err=" + Marshal.GetLastWin32Error(); return false;
    }
    byte[] back = new byte[data.Length];
    int r;
    if (!Win32.ReadProcessMemory(h, (IntPtr)addr, back, data.Length, out r) || r != data.Length) {
      err = "回读失败 err=" + Marshal.GetLastWin32Error(); return false;
    }
    for (int i = 0; i < data.Length; i++) {
      if (back[i] != data[i]) { err = "回读不一致 @" + i; return false; }
    }
    err = null;
    return true;
  }

  // ---------- SafeWriteValue：文本值 → 编码 → SafeWrite ----------
  public static bool SafeWriteValue(IntPtr h, long addr, ScanType t, string val, out byte[] wrote, out string err) {
    wrote = null;
    byte[] data;
    try { data = TypeUtil.Encode(t, val); } catch (Exception ex) { err = "值解析失败: " + ex.Message; return false; }
    if (!MemIO.SafeWrite(h, addr, data, out err)) return false;
    wrote = data;
    return true;
  }
}

} // namespace
