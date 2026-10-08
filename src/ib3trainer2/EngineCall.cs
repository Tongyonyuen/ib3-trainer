// ============================================================================
// EngineCall.cs v2 — 无感命令注入 · 【游戏主线程】信箱版（线程安全）
//
// 原理（2026-10-06 实机验证）：
//   1) 补丁 UEngine::Tick 入口 16 字节 → 跳板（每帧被游戏主线程调用）
//   2) 跳板每帧: 计数 → 保存 rcx/rdx/xmm1 → 查信箱旗
//   3) 训练器投递命令（写命令串+宿主对象+旗=1）→ 游戏下一帧自己执行：
//      跳板在游戏线程上调用 ProcessConsoleExec(宿主, 命令, 假FOutputDevice)
//      → 结果写回信箱 → 恢复寄存器 → 原指令 → 跳回
//   4) 训练器轮询读结果（1=命令已执行）
//
// 为什么必须这样（血的教训）：外部线程（CreateRemoteThread）直接执行命令
//   = setplayergems 等含列表/商店重建的命令会令游戏崩溃（线程不安全）；
//   在游戏主线程执行 = 与玩家亲手打字完全同环境，实机验证 ✓
//
// 全程不触碰窗口/焦点/控制台。挂钩只存在于游戏进程内存（进程结束即消失）。
// 适配: 若 Tick 入口已是【本训练器自己的】跳板（按字节比对确认）则直接复用。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Ib3Trainer2 {

static class EngineCall {
  // ---- 地址一律按【RVA（相对映像基址的偏移）】写，运行时再加真实基址 ----
  //
  // 游戏 exe 的 DllCharacteristics = 0x8100（未设 DYNAMICBASE），正常固定加载在 0x140000000，
  // 所以换算后与原来的硬编码绝对地址逐位相同、行为零变化。
  // 但若用户系统开了 Defender Exploit Guard 的「强制随机化映像（Mandatory ASLR）」，
  // 映像会被重定位 —— 那时原来的硬编码地址全部指向空白，Prepare() 会因字节不符而
  // 静默禁用整个注入器（表现为「什么命令都用不了」）。用 基址+RVA 就自动免疫。
  const long IMG_PREF = 0x140000000L;     // 无重定位时的首选基址
  static long imgBase = IMG_PREF;

  const long RVA_HOOK_SITE = 0x0D23AC;    // UEngine::Tick 入口
  const long RVA_HOOK_BACK = 0x0D23BC;    // 原 16 字节之后
  const long RVA_FUNC_PCE = 0x089600;     // UObject::ProcessConsoleExec
  const long RVA_VT_CHEAT = 0xB0EE30;     // CheatManager 类 vtable
  static readonly long[] RVA_VTABLES = {
    0xB5FF70, 0xB66BE0, 0xAB0B80, 0xAB0830, 0xB622F0, 0xB61B90, 0xB66870
  };

  static long HOOK_SITE { get { return imgBase + RVA_HOOK_SITE; } }
  static long HOOK_BACK { get { return imgBase + RVA_HOOK_BACK; } }
  public static long FUNC_PCE { get { return imgBase + RVA_FUNC_PCE; } }
  public static long VT_CHEATMANAGER { get { return imgBase + RVA_VT_CHEAT; } }

  static long[] _vtCache = null;
  static long _vtCacheBase = 0;
  public static long[] KNOWN_VTABLES {
    get {
      if (_vtCache == null || _vtCacheBase != imgBase) {
        long[] a = new long[RVA_VTABLES.Length];
        for (int i = 0; i < RVA_VTABLES.Length; i++) a[i] = imgBase + RVA_VTABLES[i];
        _vtCache = a; _vtCacheBase = imgBase;
      }
      return _vtCache;
    }
  }

  // 解析真实映像基址（每次附着都重新解析）
  static void ResolveBase(int pid, Action<string> log) {
    long b = IMG_PREF;
    try {
      using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(pid)) {
        if (p.MainModule != null) b = p.MainModule.BaseAddress.ToInt64();
      }
    } catch (Exception ex) {
      if (log != null) log("注入器：读取游戏映像基址失败（" + ex.Message + "）——按首选基址 0x140000000 假定");
    }
    imgBase = b;
    if (b != IMG_PREF && log != null)
      log("注入器：游戏映像被重定位到 0x" + b.ToString("X") + "（系统启用了强制随机化映像？已按 基址+偏移 换算）");
  }

  static readonly byte[] HOOK_ORIG = { 0x48,0x8B,0xC4, 0x48,0x89,0x58,0x08, 0x48,0x89,0x70,0x20, 0xF3,0x0F,0x11,0x48,0x10 };

  // ---- 跳板页槽位 ----
  const long S_CNT    = 0x180;   // tick 计数
  const long S_FLAG   = 0x188;   // 信箱旗
  const long S_HOST   = 0x190;   // 宿主对象
  const long S_RESULT = 0x198;   // 结果
  const long S_RCX    = 0x1A0;   // 保存 rcx
  const long S_RDX    = 0x1A8;   // 保存 rdx
  const long S_XMM1   = 0x1B0;   // 保存 xmm1
  const long S_FIRES  = 0x1C0;   // 信箱命中次数
  const long S_EXE    = 0x1C8;   // ★ 执行器（r9）= 真身玩家对象；作弊/发放类命令必需
  const long S_CMD    = 0x200;   // 命令串（UTF-16，512B）
  const long S_DEV    = 0x400;   // 假 FOutputDevice
  static readonly long SENT = unchecked((long)0xDEADBEEFCAFEBABEUL);

  // 控制台候选对象类 vtable 白名单的**含义**（数值见上方 RVA_VTABLES；顺序=试投顺序）：
  //   序号0  SwordPC 系（setplayergold / enablecheats / setplayergiveallitems / 龙战 / 收藏家…）
  //   序号1  玩家对象系（fillsuperandmagicmeters / setplayergems / giveitemonce / addconsumable…）
  //   其余  候选（含疑似 CheatManager 系）
  // 注意：这些 vtable 是【映像内地址】，映像被重定位时对象里存的指针也会被加载器一起重定位，
  // 所以它们必须和 RVA_VTABLES 一样按 基址+RVA 换算，不能写死。

  static readonly object gate = new object();
  static IntPtr H = IntPtr.Zero;
  static int procPid = 0;
  static long page = 0;
  // 每个白名单类的全部实例（地址升序；cap 16/类）。玩家类（序号1）执行时按"真身分"重排。
  static readonly List<List<long>> classHosts = new List<List<long>>();
  static readonly Dictionary<long, int> hostScore = new Dictionary<long, int>();
  static long goldAnchor = 0;          // 金币地址锚点推导的玩家真身对象（= 金币地址 - 0x2070）
  static readonly Dictionary<string, long> cmdHostMemo = new Dictionary<string, long>();
  static long lastWinner = 0;
  public static string LastDiag = "";
  public static bool Ready { get { return page != 0 && H != IntPtr.Zero; } }
  public static int HostCount {
    get { int n = 0; foreach (List<long> g in classHosts) n += g.Count; return n; }
  }
  public static long PageAddr { get { return page; } }
  public static long LastHost { get { lock (gate) { return lastWinner; } } }
  public static long ReadPtr(IntPtr h, long a) { lock (gate) { return ReadQ(h, a); } }
  // 真身玩家对象（金币锚定→评分；用于结构自动绑定 金币/筹码 等字段）
  public static long LivePlayerHint(IntPtr h) { lock (gate) { return PickExecutor(h); } }

  static long boundLive = 0;
  // 绑定真身（世界切换后重调用；同时把金币锚点更新为最新真身）
  public static long BindLive(IntPtr h) {
    lock (gate) {
      boundLive = PickExecutor(h);
      if (boundLive != 0) goldAnchor = boundLive + 0x2070;
      return boundLive;
    }
  }
  // 绑定是否仍然有效。
  // ★ 2026-10-08 收紧：判据从"vtable 还在"改成"**真身分仍 ≥4**（背包或商店至少一组装载）"。
  //   旧判据是这次"金币/筹码错值、四维未定位"整条故障链的最后一环：
  //   空壳影子与真身**同 vtable**，所以一旦绑上空壳，vtable 永远不会变 ⇒ LiveSane 永远 true ⇒
  //   ProbeRebind（AttachTick 里每 2 秒轮询一次）**再也不会重绑** ⇒ 错一次 = 整场都错。
  //   新判据下空壳分数不足 ⇒ LiveSane 立刻转 false ⇒ 下一个 2 秒 tick 自动重绑，
  //   玩家走进藏身地（数组装载）之后最多 2 秒就自愈，不需要手动重新附着。
  public static bool LiveSane(IntPtr h) {
    lock (gate) {
      if (boundLive == 0) return false;
      long vt = ReadQ(h, boundLive);
      if (vt == long.MinValue || Array.IndexOf(KNOWN_VTABLES, vt) < 0) return false;
      return BodyScore(h, boundLive) >= 4;
    }
  }
  // 对象是否为活着的合法宿主（注入前必查——防打已释放对象崩溃）
  static bool ObjSane(IntPtr h, long o) {
    long vt = ReadQ(h, o);
    return vt != long.MinValue && Array.IndexOf(KNOWN_VTABLES, vt) >= 0;
  }

  // 金币定位成功时调用：给出真身对象锚点（真身 = 金币地址 - 0x2070，跨重启稳定）
  public static void NoteGoldAddr(long goldAddr) {
    if (goldAddr > 0x2070) goldAnchor = goldAddr - 0x2070;
  }

  static string StatePath {
    get {
      string d = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
      return Path.Combine(d, "ib3_mailbox.json");
    }
  }

  // ---------------- 底层读写 ----------------
  static long ReadQ(IntPtr h, long a) {
    byte[] b = new byte[8]; int r;
    if (Win32.ReadProcessMemory(h, (IntPtr)a, b, 8, out r) && r == 8) return BitConverter.ToInt64(b, 0);
    return long.MinValue;
  }
  static bool WPM(IntPtr h, long a, byte[] data) {
    int w;
    return Win32.WriteProcessMemory(h, (IntPtr)a, data, data.Length, out w) && w == data.Length;
  }
  static bool WPM8(IntPtr h, long a, long v) {
    return WPM(h, a, BitConverter.GetBytes(v));
  }
  static bool WritePatched(IntPtr h, long a, byte[] data) {
    uint old;
    if (!Win32.VirtualProtectEx(h, (IntPtr)a, (IntPtr)data.Length, 0x40, out old)) return false;
    int w;
    bool ok = Win32.WriteProcessMemory(h, (IntPtr)a, data, data.Length, out w) && w == data.Length;
    uint dummy;
    Win32.VirtualProtectEx(h, (IntPtr)a, (IntPtr)data.Length, old, out dummy);
    Win32.FlushInstructionCache(h, (IntPtr)a, (IntPtr)data.Length);
    return ok;
  }

  // ---------------- 跳板构建（与 hook2.py 同规格，字节级一致） ----------------
  static int EmitRip(byte[] blob, int o, byte[] op, long T, long targetOff) {
    Array.Copy(op, 0, blob, o, op.Length);
    int disp = (int)((T + targetOff) - (T + o + op.Length + 4));
    BitConverter.GetBytes(disp).CopyTo(blob, o + op.Length);
    return o + op.Length + 4;
  }
  // 这个注入页是不是**本训练器**（任意版本）留下的？
  //
  // ★ 判据刻意只用"不随版本变化"的两点，两者都在那 160 字节比对范围之内，所以**新构建看旧页时照样成立**：
  //   ① 页尾 14 字节是 `FF 25 00 00 00 00 <HOOK_BACK>` —— 跳回本游戏的 HOOK_SITE+16。
  //      外来补丁（Steam 叠加层 / 杀软 / 别的工具）不会恰好往那里跳。
  //   ② 页内偏移 130 起的 16 字节 == HOOK_ORIG（BuildPage 固定在尾跳之前回填本函数的原入口字节）。
  //   两点同时满足 ⇒ 这页只可能是我们自己某个版本写的。
  //
  // 偏移值来自 BuildPage 尾部的固定布局：先 16 字节原入口，再 14 字节尾跳，合计正好 160。
  // ⚠ 若将来改 BuildPage 的**尾部布局**，这里的两个偏移要跟着改（改前面的指令流不影响尾跳起点，
  //    因为尾跳两段是顺序写在第 o 偏移之后、总长恒为 160）。
  static bool IsOursStalePage(byte[] got) {
    if (got == null || got.Length < 160) return false;
    if (got[146] != 0xFF || got[147] != 0x25) return false;
    if (BitConverter.ToInt64(got, 148) != HOOK_BACK) return false;
    for (int k = 0; k < 16; k++) if (got[130 + k] != HOOK_ORIG[k]) return false;
    return true;
  }

  static byte[] BuildPage(long T) {
    byte[] blob = new byte[0x1000];
    int o = 0;
    o = EmitRip(blob, o, new byte[] { 0x48, 0xFF, 0x05 }, T, S_CNT);        // inc [cnt]
    o = EmitRip(blob, o, new byte[] { 0x48, 0x89, 0x0D }, T, S_RCX);        // mov [s_rcx], rcx
    o = EmitRip(blob, o, new byte[] { 0x48, 0x89, 0x15 }, T, S_RDX);        // mov [s_rdx], rdx
    o = EmitRip(blob, o, new byte[] { 0x0F, 0x11, 0x0D }, T, S_XMM1);       // movups [s_xmm1], xmm1
    // cmp qword [rip+S_FLAG], 0
    Array.Copy(new byte[] { 0x48, 0x83, 0x3D }, 0, blob, o, 3);
    BitConverter.GetBytes((int)((T + S_FLAG) - (T + o + 8))).CopyTo(blob, o + 3);
    blob[o + 7] = 0x00; o += 8;
    int jeOff = o; blob[o] = 0x74; blob[o + 1] = 0; o += 2;                 // je rel8
    blob[o] = 0x31; blob[o + 1] = 0xC0; o += 2;                             // xor eax,eax
    o = EmitRip(blob, o, new byte[] { 0x48, 0x89, 0x05 }, T, S_FLAG);       // mov [flag], rax
    o = EmitRip(blob, o, new byte[] { 0x48, 0xFF, 0x05 }, T, S_FIRES);      // inc [fires]
    blob[o] = 0x48; blob[o + 1] = 0xB8;                                     // mov rax, PCE
    BitConverter.GetBytes(FUNC_PCE).CopyTo(blob, o + 2); o += 10;
    o = EmitRip(blob, o, new byte[] { 0x48, 0x8B, 0x0D }, T, S_HOST);       // mov rcx, [host]
    o = EmitRip(blob, o, new byte[] { 0x48, 0x8D, 0x15 }, T, S_CMD);        // lea rdx, [cmd]
    o = EmitRip(blob, o, new byte[] { 0x4C, 0x8D, 0x05 }, T, S_DEV);        // lea r8, [dev]
    o = EmitRip(blob, o, new byte[] { 0x4C, 0x8B, 0x0D }, T, S_EXE);       // mov r9, [executor] ★
    byte[] mid = { 0x48,0x83,0xEC,0x28, 0xFF,0xD0, 0x48,0x83,0xC4,0x28 };
    Array.Copy(mid, 0, blob, o, mid.Length); o += mid.Length;
    o = EmitRip(blob, o, new byte[] { 0x48, 0x89, 0x05 }, T, S_RESULT);     // mov [result], rax
    int skip = o;
    blob[jeOff + 1] = (byte)(skip - (jeOff + 2));                           // je → SKIP
    o = EmitRip(blob, o, new byte[] { 0x48, 0x8B, 0x0D }, T, S_RCX);        // mov rcx, [s_rcx]
    o = EmitRip(blob, o, new byte[] { 0x48, 0x8B, 0x15 }, T, S_RDX);        // mov rdx, [s_rdx]
    o = EmitRip(blob, o, new byte[] { 0x0F, 0x10, 0x0D }, T, S_XMM1);       // movups xmm1, [s_xmm1]
    Array.Copy(HOOK_ORIG, 0, blob, o, 16); o += 16;                         // 原 16 字节
    blob[o] = 0xFF; blob[o + 1] = 0x25;                                     // jmp [rip → BACK]
    BitConverter.GetBytes(HOOK_BACK).CopyTo(blob, o + 6); o += 14;
    // 假 FOutputDevice：对象 {vt} → 16 槽全指向 ret 桩
    BitConverter.GetBytes(T + S_DEV + 8).CopyTo(blob, S_DEV);
    for (int k = 0; k < 16; k++) BitConverter.GetBytes(T + 0x500).CopyTo(blob, S_DEV + 8 + k * 8);
    blob[0x500] = 0x31; blob[0x501] = 0xC0; blob[0x502] = 0xC3;             // xor eax,eax; ret
    BitConverter.GetBytes(SENT).CopyTo(blob, S_RESULT);
    return blob;
  }

  // 换了目标进程：清掉上一场遗留的全部缓存。
  // ★ 2026-10-08 新增。原来这三处（Prepare / ExecuteOn / Execute）只清
  //   classHosts / hostScore / cmdHostMemo / lastWinner，**漏了 goldAnchor 与 boundLive** ——
  //   关掉游戏再开、而训练器不关的话，上一场的金币锚点会留到新进程里。
  //   实践中旧堆已解除映射，旧 ValidAnchor 多半会失败，所以没炸；但那是"靠运气"，
  //   新进程的第一次绑定不该受上一场影响（现在 ValidAnchor 变强了，这个口子更要堵死）。
  static void ResetProcessCaches(IntPtr h) {
    H = h; page = 0;
    classHosts.Clear(); hostScore.Clear(); cmdHostMemo.Clear(); lastWinner = 0;
    goldAnchor = 0; boundLive = 0;
  }

  // ---------------- 安装 / 复用 ----------------
  // 复用条件：Tick 入口已是跳转 且 目标页前 160 字节与我们的构建逐字节一致（绝不覆盖未知补丁）
  public static bool Prepare(IntPtr h, int pid, Action<string> log) {
    lock (gate) {
      if (Ready && H == h) return true;
      bool newProc = (H != h);
      if (newProc) ResetProcessCaches(h); else { H = h; page = 0; }
      procPid = pid;
      ResolveBase(pid, log);   // 必须先解析基址：下面所有 HOOK_SITE/vtable 都按它换算
      byte[] site = new byte[16]; int rd;
      if (!Win32.ReadProcessMemory(h, (IntPtr)HOOK_SITE, site, 16, out rd) || rd != 16) {
        LastDiag = "读 Tick 入口失败（err=" + Marshal.GetLastWin32Error() + "）";
        if (log != null) log("注入器： " + LastDiag);
        return false;
      }
      bool installed = false;
      if (site[0] == 0xFF && site[1] == 0x25) {
        long T = BitConverter.ToInt64(site, 6);
        byte[] expect = BuildPage(T);
        byte[] got = new byte[160]; rd = 0;
        bool adopt = Win32.ReadProcessMemory(h, (IntPtr)T, got, 160, out rd) && rd == 160;
        if (adopt) for (int k = 0; k < 160; k++) if (got[k] != expect[k]) { adopt = false; break; }
        if (adopt) {
          page = T;
          installed = true;
          if (log != null) log("注入器：复用已装信箱挂钩 @0x" + T.ToString("X"));
        } else if (IsOursStalePage(got)) {
          // ★★ 认得出是**自己人**的旧版注入页（2026-10-08，作者实测「游戏不关、换个修改器版本」失败的成因）。
          //
          //   为什么会有旧页：跳板页是 VirtualAllocEx 分配在**游戏进程**里的，修改器退出不会释放它
          //   （全项目从不调 VirtualFreeEx）。于是换版本时，游戏里的 Tick 入口仍是 FF 25 指向旧页，
          //   而旧页的指令流/槽位布局属于旧构建 ⇒ 那 160 字节比对必然失败。
          //
          //   为什么旧行为很糟：原代码在这里直接拒绝并把 page 留 0；而 AttachTick 在游戏存活期间
          //   **不会再次调用 Prepare**（Ib3Trainer2.cs:678 起直接转 ProbeRebind）⇒ 整个会话都卡在
          //   「注入器未就绪」，唯一出路是重启游戏 —— 正是作者观察到的现象。
          //
          //   现在：认得出就把 Tick 入口**还原成原 16 字节**，再走下面的正常安装路径。
          //   安全性：① 只认"尾跳回本游戏的 HOOK_SITE+16 且页内嵌着本函数入口原 16 字节"的页，
          //             外来补丁（Steam 叠加层/杀软/别的工具）不会同时满足这两点；
          //           ② 旧页尾跳目标是 HOOK_SITE+16 而**不是** HOOK_SITE，所以即便有线程正停在页内，
          //             还原入口之后它仍能正确返回；
          //           ③ **不** VirtualFreeEx 旧页 —— 可能有线程正在页内执行，释放会崩。泄漏 4KB 是这里
          //             的正确取舍。旧页就此变成孤儿，不再被引用。
          if (log != null) log("注入器：检测到本训练器的旧版注入页 @0x" + T.ToString("X") +
                               "（多半是换了修改器版本而游戏没重启）——还原 Tick 入口后重装挂钩");
          if (!WritePatched(h, HOOK_SITE, HOOK_ORIG)) {
            LastDiag = "还原 Tick 入口失败 err=" + Marshal.GetLastWin32Error();
            if (log != null) log("注入器： " + LastDiag);
            return false;
          }
          Array.Copy(HOOK_ORIG, 0, site, 0, 16);   // 本地副本同步，好让下面的校验通过
        } else {
          LastDiag = "Tick 入口已有未知补丁（非本训练器），拒绝覆盖";
          if (log != null) log("注入器： " + LastDiag);
          return false;
        }
      }
      if (!installed) {
        bool same = true;
        for (int k = 0; k < 16; k++) if (site[k] != HOOK_ORIG[k]) { same = false; break; }
        if (!same) {
          LastDiag = "Tick 入口字节与预期不符（版本漂移？），注入器禁用";
          if (log != null) log("注入器： " + LastDiag);
          return false;
        }
        IntPtr p = Win32.VirtualAllocEx(h, IntPtr.Zero, (IntPtr)0x1000, 0x3000, 0x40);
        if (p == IntPtr.Zero) {
          LastDiag = "VirtualAllocEx 失败 err=" + Marshal.GetLastWin32Error();
          if (log != null) log("注入器： " + LastDiag);
          return false;
        }
        long T = p.ToInt64();
        byte[] blob = BuildPage(T);
        if (!WPM(h, T, blob)) { LastDiag = "写跳板页失败"; if (log != null) log("注入器： " + LastDiag); return false; }
        byte[] patch = new byte[16];
        patch[0] = 0xFF; patch[1] = 0x25;
        BitConverter.GetBytes(T).CopyTo(patch, 6);
        patch[14] = 0x90; patch[15] = 0x90;
        if (!WritePatched(h, HOOK_SITE, patch)) {
          LastDiag = "Tick 补丁写入失败 err=" + Marshal.GetLastWin32Error();
          if (log != null) log("注入器： " + LastDiag);
          return false;
        }
        page = T;
        try { File.WriteAllText(StatePath, "{\"pid\":" + pid + ",\"tramp\":" + T + "}"); } catch { }
        if (log != null) log("注入器：信箱挂钩已安装 @0x" + T.ToString("X"));
      }
      long c1 = ReadQ(h, page + S_CNT);
      Thread.Sleep(150);
      long c2 = ReadQ(h, page + S_CNT);
      if (c1 != long.MinValue && c2 > c1) {
        if (log != null) log("注入器就绪：游戏主线程信箱运行中（tick " + ((c2 - c1) * 1000 / 150) + "/s）");
      } else {
        if (log != null) log("注入器已就绪（游戏帧暂停中；命令将在游戏恢复的下一帧生效）");
      }
      return true;
    }
  }

  // ---------------- 宿主扫描（可写私有区 + vtable 白名单 + 形状校验） ----------------
  // .text 段范围（RVA 0x1000 .. 0x8DC000；SizeOfImage = 0xE87000）
  static bool InText(long v) { return v >= imgBase + 0x1000 && v < imgBase + 0x8DC000; }
  static bool ShapeOk(IntPtr h, long vt) {
    for (int k = 0; k < 8; k++) {
      long v = ReadQ(h, vt + k * 8);
      if (!InText(v)) return false;
    }
    return true;
  }
  // TArray 头 {int64 Data, int32 Count, int32 Max} 的"已装载"判据。
  // 与 Tabs.Gems.cs 的 ArrayLoaded 同源 —— 那边的版本是 MainForm 的私有静态，EngineCall 够不着，
  // 所以这里复制一份；**改判据时两处要一起改**。
  static bool ArrayLoaded(IntPtr h, long headerAddr, out int count) {
    count = 0;
    byte[] hdr = new byte[16]; int r;
    if (!Win32.ReadProcessMemory(h, (IntPtr)headerAddr, hdr, 16, out r) || r != 16) return false;
    long data = BitConverter.ToInt64(hdr, 0);
    count = BitConverter.ToInt32(hdr, 8);
    int max = BitConverter.ToInt32(hdr, 12);
    if (count <= 0 || count > 4096) return false;
    if (max < count || max > 65536) return false;
    if (data < 0x10000 || data > 0x7FFFFFFFFFFF) return false;
    return true;
  }

  // "真身分"：背包装载 4 / 商店装载 4 / 金币 2 / 筹码 1。
  // ★ 2026-10-08 重写（原 CurrencyScore）。旧版只算 金币 + 筹码 + **商店**数组，
  //   **完全不看背包（+0x1FEC）** —— 而背包才是"这个对象真的装着状态"的最强证据。
  //   权重必须让**数组装载(4+4) 压过 金币(2)**：实测同进程 8 个带玩家 vtable 的对象里，
  //   有一个"金币非 0 但背包/商店都 Count=0"的空壳；旧评分会把它与真身判成同分甚至更高，
  //   一旦被选中，金币/筹码读到错的值、四维自动绑定因全 0 而放弃（= 用户看到的"未定位"）。
  //   判据与 Tabs.Gems.cs 的 BodyScore 同源，改这里时那边要一起看。
  static int BodyScore(IntPtr h, long o) {
    int s = 0, c;
    if (ArrayLoaded(h, o + 0x1FEC, out c)) s += 4;   // 背包
    if (ArrayLoaded(h, o + 0x1FFC, out c)) s += 4;   // 商店
    long gold = ReadQ(h, o + 0x2070);
    if (gold > 0 && gold < 1000000000000000L) s += 2;
    byte[] b4 = new byte[4]; int r;
    if (Win32.ReadProcessMemory(h, (IntPtr)(o + 0x2094), b4, 4, out r) && r == 4) {
      if (BitConverter.ToInt32(b4, 0) > 0) s += 1;   // 筹码是 I32，别按 I64 读
    }
    return s;
  }

  // 锚点是否可信。
  // ★ 2026-10-08 收紧。旧实现只验「vtable ∈ 白名单 + 金币 ∈ (0,1e15)」：
  //   空壳影子与真身**同 vtable**、金币也可能非 0，于是一个陈旧或被复用的堆地址照样"通过校验"，
  //   然后被当成真身用一整场。现在必须**真身分 ≥4**，即至少一组数组真的装着。
  static bool ValidAnchor(IntPtr h, long o) {
    if (o == 0) return false;
    long vt = ReadQ(h, o);
    if (vt == long.MinValue || Array.IndexOf(KNOWN_VTABLES, vt) < 0) return false;
    return BodyScore(h, o) >= 4;
  }
  static bool AnyHost(long o) {
    for (int i = 0; i < classHosts.Count; i++) if (classHosts[i].Contains(o)) return true;
    return false;
  }
  // 玩家类执行顺序：真身分降序（同分时金币锚点优先）→ 地址升序。
  // ★ 2026-10-08：不再"金币锚定第一"。锚定只在**同分**时打破平局 ——
  //   否则一个金币非 0 的空壳仅凭锚定身份就会排到真身前面（见 ValidAnchor / PickExecutor）。
  static List<long> OrderPlayerClass(IntPtr h, List<long> group) {
    List<long> g = new List<long>(group);
    Dictionary<long, int> sc = new Dictionary<long, int>();
    for (int i = 0; i < g.Count; i++) sc[g[i]] = BodyScore(h, g[i]);
    g.Sort(delegate(long a, long b) {
      int sa = sc[a], sb = sc[b];
      if (sa != sb) return sb - sa;
      bool aa = (a == goldAnchor), ba = (b == goldAnchor);
      if (aa != ba) return aa ? -1 : 1;
      return a < b ? -1 : (a > b ? 1 : 0);   // 同分取地址小的：结果可复现，不随扫描顺序漂移
    });
    return g;
  }

  public static int Discover(IntPtr h, Action<string> log) {
    lock (gate) {
      classHosts.Clear();
      hostScore.Clear();
      Dictionary<long, List<long>> found = new Dictionary<long, List<long>>();
      foreach (long vt in KNOWN_VTABLES) found[vt] = new List<long>();
      long addr = ScanCore.MIN_ADDR;
      while (addr < ScanCore.MAX_ADDR) {
        Win32.MBI m;
        if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
        long size = m.RegionSize.ToInt64();
        if (size <= 0) break;
        if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) {
          long p = addr, end = addr + size;
          byte[] buf = new byte[0x100000];
          while (p < end) {
            int chunk = (int)Math.Min(0x100000, end - p);
            int r;
            if (Win32.ReadProcessMemory(h, (IntPtr)p, buf, chunk, out r) && r >= 8) {
              for (int i = 0; i + 8 <= r; i += 8) {
                long v = BitConverter.ToInt64(buf, i);
                if (Array.IndexOf(KNOWN_VTABLES, v) >= 0) {
                  List<long> lst = found[v];
                  if (lst.Count < 16 && !lst.Contains(p + i) && ShapeOk(h, v)) lst.Add(p + i);
                }
              }
            }
            p += chunk;
          }
        }
        addr += size;
      }
      foreach (long vt in KNOWN_VTABLES) classHosts.Add(found[vt]);
      if (classHosts.Count > 1) {
        foreach (long o in classHosts[1]) hostScore[o] = BodyScore(h, o);
      }
      int total = 0;
      foreach (List<long> g in classHosts) total += g.Count;
      string extra = "";
      if (classHosts.Count > 1 && classHosts[1].Count > 0) {
        List<long> og = OrderPlayerClass(h, classHosts[1]);
        long pick = og[0];
        int sc = 0; hostScore.TryGetValue(pick, out sc);
        // 真身分 <4 表示**没有任何一组数组装载** —— 此时不会被选作执行器（见 PickExecutor）。
        // 「玩家在关卡里 / 商店熔接室界面时背包数组为空」是正常状态，日志要说清以免误判为故障。
        extra = "；玩家真身优先 0x" + pick.ToString("X") + "（真身分 " + sc +
                (sc >= 4 ? "" : "·不足，暂不可用：数组未装载，回藏身地主界面即可") + "）";
      }
      if (log != null) log("注入器自检：候选宿主 " + KNOWN_VTABLES.Length + " 类共 " + total + " 个实例已定位" + extra);
      return total;
    }
  }

  // 执行器：玩家类里**真身分最高**者（作弊/发放类命令必需）。
  // ★ 2026-10-08 重写：删掉了"金币锚点一票通过"的短路。
  //   旧实现第一行就是 `if (goldAnchor != 0 && ValidAnchor(h, goldAnchor)) return goldAnchor;`，
  //   而旧 ValidAnchor 只验 vtable + 金币区间 —— 这正是"金币/筹码读到错值、四维未定位"的直接成因。
  //   现在锚点只在**同分**时作偏好（见 OrderPlayerClass）。
  //   并且：**没有任何实例真身分 ≥4 时返回 0**（拒绝执行），而不是退回一个空壳。
  //   这是本项目一贯的取舍 —— 宁可让命令明确失败，也不在一个错对象上执行
  //   （在空壳上跑 giveitemonce 之类不是"没效果"，而是可能把状态写进错的对象）。
  static long PickExecutor(IntPtr h) {
    if (classHosts.Count <= 1) return 0;
    List<long> g = classHosts[1];
    if (g.Count == 0) return 0;
    long pick = OrderPlayerClass(h, g)[0];
    return BodyScore(h, pick) >= 4 ? pick : 0;
  }

  // 按 vtable 找对象实例（CheatManager 等非白名单类；先扫玩家堆区快速通道）
  public static long FindByVtable(IntPtr h, long vt) {
    lock (gate) {
      long f = ScanForVt(h, vt, 0x7FF4E00000L, 0x7FF5000000L);
      if (f != 0) return f;
      return ScanForVt(h, vt, ScanCore.MIN_ADDR, ScanCore.MAX_ADDR);
    }
  }
  static long ScanForVt(IntPtr h, long vt, long min, long max) {
    byte[] pat = BitConverter.GetBytes(vt);
    long addr = min;
    while (addr < max) {
      Win32.MBI m;
      if (Win32.VirtualQueryEx(h, (IntPtr)addr, out m, Marshal.SizeOf(typeof(Win32.MBI))) == IntPtr.Zero) break;
      long size = m.RegionSize.ToInt64();
      if (size <= 0) break;
      if (ScanCore.RegionOk(m.State, m.Protect) && ScanCore.RegionWritable(m.Protect) && m.Type == 0x20000) {
        long p = addr, end = Math.Min(addr + size, max);
        byte[] buf = new byte[0x100000];
        while (p < end) {
          int chunk = (int)Math.Min(0x100000, end - p);
          int r;
          if (Win32.ReadProcessMemory(h, (IntPtr)p, buf, chunk, out r) && r >= 8) {
            for (int i = 0; i + 8 <= r; i += 8) {
              if (BitConverter.ToInt64(buf, i) == vt && ShapeOk(h, p + i)) return p + i;
            }
          }
          p += chunk;
        }
      }
      addr += size;
    }
    return 0;
  }

  // 指定宿主执行（如 CheatManager 上的 god）
  public static bool ExecuteOn(IntPtr h, string cmd, long host, out string err) {
    err = null;
    lock (gate) {
      if (H != h) ResetProcessCaches(h);
      if (page == 0) { err = "注入器未就绪"; return false; }
      {
        long c1 = ReadQ(h, page + S_CNT);
        Thread.Sleep(100);
        long c2 = ReadQ(h, page + S_CNT);
        if (c1 == long.MinValue || c2 == long.MinValue) { err = "读信箱计数失败（注入器未就绪？）"; return false; }
        if (c1 == c2) { err = "游戏暂停/加载中（帧未推进）——已拒绝注入，请等游戏画面恢复后再点"; return false; }
      }
      byte[] cb = Encoding.Unicode.GetBytes(cmd + "\0");
      if (cb.Length > 0x200) { err = "命令过长"; return false; }
      byte[] buf = new byte[0x200];
      Array.Copy(cb, buf, cb.Length);
      if (!WPM(h, page + S_CMD, buf)) { err = "写命令缓冲区失败"; return false; }
      long ex = PickExecutor(h);
      if (ex == 0) ex = host;   // 执行器缺省回退宿主
      if (!WPM8(h, page + S_EXE, ex) || !WPM8(h, page + S_HOST, host) || !WPM8(h, page + S_RESULT, SENT) || !WPM8(h, page + S_FLAG, 1)) {
        err = "写信箱失败（err=" + Marshal.GetLastWin32Error() + "）"; return false;
      }
      long r = SENT; bool got = false;
      long t0 = Environment.TickCount;
      while (Environment.TickCount - t0 < 1500) {
        r = ReadQ(h, page + S_RESULT);
        if (r != SENT) { got = true; break; }
        Thread.Sleep(6);
      }
      if (!got) { WPM8(h, page + S_FLAG, 0); err = "执行超时"; return false; }
      if (r == 1) { lastWinner = host; return true; }
      err = "该对象未执行此命令（返回 " + r + "）";
      return false;
    }
  }

  // 训练器的自定义控制台命令 —— 全部是随包 SwordGame.upk 里的脚本函数，
  // 游戏本体 IB3.exe 里一个都不存在（已用二进制搜索核实）。用于失败分诊：
  // 这类命令全部宿主都没命中时，基本可断定是 upk 没部署。
  static readonly string[] CUSTOM_CMDS = {
    "giveitemonce", "addconsumable", "setplayergiveallitems", "masterallowneditems",
    "setplayergems", "setgivekeyitem", "setplayercreatenewlistofstoregems"
  };
  static bool IsCustomCmd(string tok) { return Array.IndexOf(CUSTOM_CMDS, tok) >= 0; }

  // 注入留痕：把"打到哪个宿主、用哪个执行器、结果如何"写进训练器日志。
  // 为什么必须留：崩溃类问题**只有用户那边能复现**，我们这边游戏一关就什么都看不到。
  // 2026-10-07 用户报「点"商店刷新一轮稀有宝石"游戏闪退」时，日志里除了那句无条件弹出的
  // "已刷新"之外没有任何现场信息 —— 连命令有没有送到游戏里都判不出来。有了这一行，
  // 下次只要把日志发回来就能定位：是没送到、送错了宿主、还是游戏自己执行时崩的。
  public static Action<string> LogHook = null;
  static void Trace(string s) { try { if (LogHook != null) LogHook(s); } catch { } }

  // ---------------- 命令执行（信箱投递） ----------------
  // 顺序：命令名上次命中宿主 → 总上次命中 → 其余。返回 1 = 游戏主线程已执行。
  public static bool Execute(IntPtr h, string cmd, out string err) {
    err = null;
    lock (gate) {
      if (H != h) ResetProcessCaches(h);
      if (page == 0) { err = "注入器未就绪（请重新附着：训练器会自动安装信箱挂钩）"; return false; }
      if (HostCount == 0) Discover(h, null);
      if (HostCount == 0) { err = "未找到候选宿主对象（版本可能不符）"; return false; }
      // 预检：游戏帧必须推进（防在读档/加载窗口注入——曾致崩）
      {
        long c1 = ReadQ(h, page + S_CNT);
        Thread.Sleep(100);
        long c2 = ReadQ(h, page + S_CNT);
        if (c1 == long.MinValue || c2 == long.MinValue) { err = "读信箱计数失败（注入器未就绪？）"; return false; }
        if (c1 == c2) { err = "游戏暂停/加载中（帧未推进）——已拒绝注入，请等游戏画面恢复后再点"; return false; }
      }
      byte[] cb = Encoding.Unicode.GetBytes(cmd + "\0");
      if (cb.Length > 0x200) { err = "命令过长"; return false; }
      byte[] buf = new byte[0x200];
      Array.Copy(cb, buf, cb.Length);
      if (!WPM(h, page + S_CMD, buf)) { err = "写命令缓冲区失败"; return false; }
      long ex = PickExecutor(h);   // ★ 执行器 = 真身玩家（作弊/发放类命令必需）

      string tok = cmd.Split(' ')[0];
      if (tok == "killboss") ex = 0;   // killboss 例外：带执行器会崩（实测），历史成功配置 = 无执行器
      else if (ex == 0) {
        // ★ 2026-10-08 新增留痕。真身分不足时 PickExecutor 会返回 0 —— 这是**有意的**
        //   （拒绝在一个空壳对象上执行命令）。但它表现为"点了没反应"，所以必须留一行痕，
        //   否则用户/排查者分不清是"命令没送到"还是"根本没找到真身"。
        Trace("注入：" + tok + " 未找到可用的真身执行器（背包与商店数组都未装载）——" +
              "该命令很可能无效。回藏身地主界面让数组装载后重试。");
      }
      List<long> order = new List<long>();
      long memo;
      if (cmdHostMemo.TryGetValue(tok, out memo) && AnyHost(memo)) order.Add(memo);
      if (lastWinner != 0 && !order.Contains(lastWinner) && AnyHost(lastWinner)) order.Add(lastWinner);
      for (int ci = 0; ci < classHosts.Count; ci++) {
        List<long> group = classHosts[ci];
        if (ci == 1) group = OrderPlayerClass(h, group);
        foreach (long o in group) if (!order.Contains(o)) order.Add(o);
      }

      int firedAny = 0;
      for (int i = 0; i < order.Count; i++) {
        long host = order[i];
        if (!ObjSane(h, host)) continue;   // 宿主已被世界切换释放 → 跳过（防崩）
        firedAny++;
        if (!WPM8(h, page + S_EXE, ex) || !WPM8(h, page + S_HOST, host) || !WPM8(h, page + S_RESULT, SENT) || !WPM8(h, page + S_FLAG, 1)) {
          err = "写信箱失败（err=" + Marshal.GetLastWin32Error() + "）";
          return false;
        }
        long r = SENT;
        bool got = false;
        long t0 = Environment.TickCount;
        while (Environment.TickCount - t0 < 1500) {
          r = ReadQ(h, page + S_RESULT);
          if (r != SENT) { got = true; break; }
          Thread.Sleep(6);
        }
        if (!got) {
          long c1 = ReadQ(h, page + S_CNT);
          Thread.Sleep(150);
          long c2 = ReadQ(h, page + S_CNT);
          if (c1 == long.MinValue || c2 == long.MinValue) { err = "读信箱计数失败"; return false; }
          if (c1 == c2) {
            WPM8(h, page + S_FLAG, 0);   // 取消排队（加载窗口注入=危险，不再挂起）
            err = "游戏暂停/加载中（帧未推进）——已取消，请等游戏恢复后再点";
            return false;
          }
          WPM8(h, page + S_FLAG, 0);   // 帧在跑但无结果 → 取消，防悬挂
          err = "执行超时（帧在跑但无回执）";
          return false;
        }
        if (r == 1) {
          lastWinner = host; cmdHostMemo[tok] = host;
          Trace("注入 " + tok + " 成功：宿主 0x" + host.ToString("X") + " 执行器 0x" + ex.ToString("X") +
                (ex != 0 ? ("（执行器金币=" + ReadQ(h, ex + 0x2070) + "）") : "（无执行器）"));
          return true;
        }
        // r == 0 → 该宿主类不含此命令 → 试下一个
      }
      if (firedAny == 0) { err = "宿主对象已全部失效（游戏世界刚切换）——请等几秒自动重新绑定后再试"; return false; }
      if (IsCustomCmd(tok)) {
        // UE3 的 ProcessConsoleExec 对「函数不存在」和「函数存在但条件不符」都返回 0，
        // 单看返回值分不开。但训练器的自定义命令在游戏 exe 里根本不存在（它们是随包
        // SwordGame.upk 里的脚本函数），所以全部宿主都没命中时，最可能的原因是 upk 没部署。
        err = "全部宿主都没执行 " + tok + " —— 该命令属于随包 SwordGame.upk 的自定义脚本函数，" +
              "极可能是 upk 未部署到游戏目录（点底部「部署/校验 UPK」修复，需先关闭游戏）";
      } else {
        err = "所有宿主均未执行该命令（命令不存在或游戏条件不符）";
      }
      Trace("注入 " + tok + " 失败：" + err + "（试过 " + firedAny + " 个宿主，执行器 0x" + ex.ToString("X") + "）");
      return false;
    }
  }
}

} // namespace
