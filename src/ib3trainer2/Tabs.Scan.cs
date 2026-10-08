// ============================================================================
// Tabs.Scan.cs — 发现模式（RE 工作台）
// 已知值首扫 / 命中筛选（增大/减小/变化/未变化/等于）/ 未知值快照 / 快照差分 /
// 1.5s 稳定化（筛掉游戏计时器噪声）/ 试写探针 / 加入地址表。
// 说明：RE 期间的引擎探针命令由玩家本人用自己的控制台执行——训练器不注入任何人。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Ib3Trainer2 {

partial class MainForm {
  ComboBox cboScanType;
  TextBox txtScanVal, txtScanDesc;
  ProgressBar pbScan;
  Label lblScanStat;
  ListView lvScan;
  Button btnFirst, btnInc, btnDec, btnChg, btnUnch, btnEq, btnSnap, btnSnapChg, btnSnapEq, btnStable, btnCancel, btnProbe, btnBook;
  List<ScanHit> scanHits = new List<ScanHit>();
  List<SnapRegion> scanSnap = new List<SnapRegion>();
  volatile bool scanCancel = false;
  public volatile bool ScanBusy = false;

  TabPage BuildTabScan() {
    TabPage p = new TabPage("发现模式");
    p.BackColor = Theme.BG; p.ForeColor = Theme.Text;

    p.Controls.Add(Theme.MkLabelInk("类型", 14, 12, 34));
    cboScanType = new ComboBox();
    cboScanType.SetBounds(52, 9, 92, 23);
    Theme.StyleCombo(cboScanType);
    cboScanType.Items.AddRange(new object[] { "Int32", "Int64", "Float", "Double", "Int16", "Int8" });
    cboScanType.SelectedIndex = 0;
    p.Controls.Add(cboScanType);
    p.Controls.Add(Theme.MkLabelInk("值 / 参照值", 152, 12, 76));
    txtScanVal = Theme.MkText(232, 9, 130, "");
    p.Controls.Add(txtScanVal);
    Label h1 = Theme.MkHint("唯一命中可直接入表锁定；多命中就改值后再筛选。", 372, 6, 400);
    h1.ForeColor = Theme.Ink;
    p.Controls.Add(h1);

    int x = 14;
    btnFirst = Theme.MkButton("首次扫描", x, 40, 90, 27, delegate { DoFirstScan(); }); x += 96;
    btnInc = Theme.MkButton("筛选:增大", x, 40, 84, 27, delegate { DoFilter(FilterKind.Increased); }); x += 90;
    btnDec = Theme.MkButton("减小", x, 40, 60, 27, delegate { DoFilter(FilterKind.Decreased); }); x += 66;
    btnChg = Theme.MkButton("变化", x, 40, 60, 27, delegate { DoFilter(FilterKind.Changed); }); x += 66;
    btnUnch = Theme.MkButton("未变化", x, 40, 74, 27, delegate { DoFilter(FilterKind.Unchanged); }); x += 80;
    btnEq = Theme.MkButton("等于参照值", x, 40, 96, 27, delegate { DoFilter(FilterKind.Equal); }); x += 102;
    btnSnap = Theme.MkButton("新快照", 596, 40, 84, 27, delegate { DoSnapshot(); });
    btnCancel = Theme.MkButton("取消", 686, 40, 60, 27, delegate { scanCancel = true; ToastMgr.Show(I18n.T("已请求取消…")); });
    p.Controls.Add(btnFirst); p.Controls.Add(btnInc); p.Controls.Add(btnDec); p.Controls.Add(btnChg);
    p.Controls.Add(btnUnch); p.Controls.Add(btnEq); p.Controls.Add(btnSnap); p.Controls.Add(btnCancel);

    x = 14;
    btnSnapChg = Theme.MkButton("快照筛:变化", x, 74, 96, 27, delegate { DoSnapFilter(false); }); x += 102;
    btnSnapEq = Theme.MkButton("快照筛:等于", x, 74, 96, 27, delegate { DoSnapFilter(true); }); x += 102;
    btnStable = Theme.MkButton("1.5s稳定化", x, 74, 100, 27, delegate { DoStabilize(); }); x += 106;
    btnProbe = Theme.MkButton("试写+1", x, 74, 74, 27, delegate { DoProbe(); }); x += 80;
    btnBook = Theme.MkButton("加入地址表", x, 74, 100, 27, delegate { DoBook(); }); x += 106;
    p.Controls.Add(btnSnapChg); p.Controls.Add(btnSnapEq); p.Controls.Add(btnStable);
    p.Controls.Add(btnProbe); p.Controls.Add(btnBook);

    p.Controls.Add(Theme.MkLabelInk("条目描述", 448, 79, 64));
    txtScanDesc = Theme.MkText(514, 76, 130, "");
    p.Controls.Add(txtScanDesc);
    Label hFlow = Theme.MkHint("流程: 快照 → 游戏内做动作/打探针 → 变化 → 1.5s稳定化。", 14, 104, 500);
    hFlow.Height = 20;
    hFlow.ForeColor = Theme.Ink;
    p.Controls.Add(hFlow);

    pbScan = new ProgressBar();
    pbScan.SetBounds(14, 132, 560, 14);
    pbScan.Style = ProgressBarStyle.Continuous;
    p.Controls.Add(pbScan);
    lblScanStat = Theme.MkLabel("", 584, 130, 230);
    lblScanStat.ForeColor = Theme.Ink;
    p.Controls.Add(lblScanStat);

    lvScan = new DarkListView();
    Theme.ApplyDarkScroll(lvScan);
    lvScan.SetBounds(14, 154, 790, 296);
    Theme.StyleList(lvScan);
    lvScan.Columns.Add("地址", 170);
    lvScan.Columns.Add("值", 160);
    lvScan.Columns.Add("说明", 430);
    p.Controls.Add(lvScan);
    lvScan.HandleCreated += delegate {
      SendMessage(lvScan.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)0, (IntPtr)LVS_EX_DOUBLEBUFFER);
    };
    return p;
  }

  ScanType CurScanType() {
    switch (cboScanType.SelectedIndex) {
      case 0: return ScanType.I32;
      case 1: return ScanType.I64;
      case 2: return ScanType.F32;
      case 3: return ScanType.F64;
      case 4: return ScanType.I16;
      case 5: return ScanType.I8;
    }
    return ScanType.I32;
  }

  // ---------- 后台任务公用 ----------
  public void RunBackground(string what, Func<string> job) {
    if (ScanBusy) { ToastMgr.Show(I18n.T("已有扫描任务在执行（可点「取消」）")); return; }
    ScanBusy = true;
    scanCancel = false;
    SetScanStatus(what + "…");
    BusyShow(what);
    Thread th = new Thread(delegate() {
      string result = null;
      try { result = job(); } catch (Exception ex) { result = "异常: " + ex.Message; }
      try {
        BeginInvoke((MethodInvoker)delegate {
          ScanBusy = false;
          SetScanStatus("");
          pbScan.Value = 0;
          BusyHide();
          if (result != null && result.Trim().Length > 0) Log(result.TrimEnd());
        });
      } catch { BusyHideAll(); }
    });
    th.IsBackground = true;
    th.Start();
  }

  void SetScanStatus(string s) {
    try {
      if (lblScanStat == null) return;
      if (InvokeRequired) { BeginInvoke((MethodInvoker)delegate { SetScanStatus(s); }); return; }
      // 文案随扫描进度反复变化 ⇒ 走 Register（key = 控件本身）：切语言时重贴的是"最后一次"那条，
      // 不会被 MkLabel("") 当初登记的空白顶掉（同 Ib3Trainer2.cs 的 Status()）。
      I18n.Register(lblScanStat, delegate(string v) { lblScanStat.Text = v; }, s);
    } catch { }
  }

  void ShowHits(string tag) {
    lvScan.BeginUpdate();
    lvScan.Items.Clear();
    int show = Math.Min(2000, scanHits.Count);
    string typeName = TypeUtil.Name(CurScanType());
    for (int i = 0; i < show; i++) {
      ListViewItem it = new ListViewItem("0x" + scanHits[i].Addr.ToString("X"));
      it.SubItems.Add(TypeUtil.Decode(CurScanType(), scanHits[i].Prev));
      it.SubItems.Add(tag);
      lvScan.Items.Add(it);
    }
    lvScan.EndUpdate();
  }

  ScanHit SelectedHit() {
    if (lvScan.SelectedItems.Count == 0) return null;
    string s = lvScan.SelectedItems[0].Text;
    if (!s.StartsWith("0x")) return null;
    long a;
    try { a = TypeUtil.ParseInt(s); } catch { return null; }
    for (int i = 0; i < scanHits.Count; i++) if (scanHits[i].Addr == a) return scanHits[i];
    ScanHit h = new ScanHit(); h.Addr = a;
    return h;
  }

  // ---------- 动作 ----------
  void DoFirstScan() {
    if (!RequireH()) return;
    ScanType t = CurScanType();
    string seed = txtScanVal.Text.Trim();
    if (seed.Length == 0) { ToastMgr.Warn(I18n.T("请输入当前已知数值（或先用「新快照」走未知值模式）")); return; }
    RunBackground(I18n.T("首次扫描") + " " + TypeUtil.Name(t) + " = " + seed, delegate {
      byte[] pat;
      try { pat = TypeUtil.Encode(t, seed); } catch (Exception ex) { return "值无效: " + ex.Message; }
      List<ScanHit> hits = ScanCore.FirstScanKnown(H, t, pat, ScanCore.MAX_HITS,
        delegate { return scanCancel; },
        delegate(long a, long b) { SetPb(a, b); });
      scanHits = hits;
      try { BeginInvoke((MethodInvoker)delegate {
        ShowHits(I18n.T("首扫 ") + seed);
        ToastMgr.Show(I18n.T("首扫命中 ") + hits.Count + I18n.T(" 条") + (hits.Count == 1 ? I18n.T("（唯一，可试写+1 验证）") : ""));
      }); } catch { }
      return "首扫[" + TypeUtil.Name(t) + " " + seed + "] 命中 " + hits.Count + " 条";
    });
  }

  void DoFilter(FilterKind f) {
    if (!RequireH()) return;
    if (scanHits.Count == 0) { ToastMgr.Warn(I18n.T("请先做「首次扫描」或「快照筛」得到候选")); return; }
    ScanType t = CurScanType();
    string refText = txtScanVal.Text.Trim();
    if ((f == FilterKind.Equal) && refText.Length == 0) { ToastMgr.Warn(I18n.T("「等于参照值」需要填写参照值")); return; }
    RunBackground(I18n.T("筛选 ") + f, delegate {
      List<ScanHit> keep;
      try {
        if (refText.Length == 0) keep = ScanCore.FilterHitsExact(H, t, scanHits, f, "0", delegate { return scanCancel; }, null);
        else keep = ScanCore.FilterHitsExact(H, t, scanHits, f, refText, delegate { return scanCancel; }, null);
      } catch (Exception ex) { return "筛选失败: " + ex.Message; }
      scanHits = keep;
      try { BeginInvoke((MethodInvoker)delegate {
        ShowHits(I18n.T("筛选:") + f);
        ToastMgr.Show(I18n.T("筛选后剩 ") + keep.Count + I18n.T(" 条"));
      }); } catch { }
      return "筛选 " + f + " 后剩 " + keep.Count + " 条";
    });
  }

  void DoSnapshot() {
    if (!RequireH()) return;
    ScanType t = CurScanType();
    RunBackground(I18n.T("内存快照（可写私有区）"), delegate {
      long total; bool capped; int skipped;
      DateTime t0 = DateTime.Now;
      List<SnapRegion> snap = ScanCore.SnapshotUnknown(H, t, delegate { return scanCancel; },
        delegate(long a, long b) { SetPb(a, b); }, out total, out capped, out skipped);
      scanSnap = snap;
      return "快照完成: " + snap.Count + " 区 / " + (total / 1048576) + " MB" + (capped ? "（超上限，部分跳过）" : "") +
             " / 跳过 " + skipped + " / 用时 " + (int)(DateTime.Now - t0).TotalSeconds + "s";
    });
  }

  void DoSnapFilter(bool equal) {
    if (!RequireH()) return;
    if (scanSnap.Count == 0) { ToastMgr.Warn(I18n.T("请先「新快照」")); return; }
    ScanType t = CurScanType();
    string refText = txtScanVal.Text.Trim();
    if (equal) {
      if (refText.Length == 0) { ToastMgr.Warn(I18n.T("「快照筛:等于」需要填写参照值")); return; }
      RunBackground(I18n.T("快照筛:等于") + " " + refText, delegate {
        bool cap2;
        List<ScanHit> keep = ScanCore.FilterSnapshotExact(H, t, scanSnap, FilterKind.Equal, refText,
          delegate { return scanCancel; }, delegate(long a, long b) { SetPb(a, b); }, out cap2);
        scanHits = keep;
        try { BeginInvoke((MethodInvoker)delegate {
          ShowHits(I18n.T("快照:等于") + refText);
          ToastMgr.Show(I18n.T("快照筛:等于") + I18n.T(" 命中 ") + keep.Count + I18n.T(" 条") + (cap2 ? I18n.T("（截断）") : ""));
        }); } catch { }
        return "快照筛:等于 " + refText + " → " + keep.Count + " 条";
      });
    } else {
      RunBackground(I18n.T("快照筛:变化"), delegate {
        bool cap2;
        List<ScanHit> keep = ScanCore.FilterSnapshot(H, t, scanSnap, FilterKind.Changed, 0,
          delegate { return scanCancel; }, delegate(long a, long b) { SetPb(a, b); }, out cap2);
        scanHits = keep;
        try { BeginInvoke((MethodInvoker)delegate {
          ShowHits(I18n.T("快照:变化"));
          ToastMgr.Show(I18n.T("变化") + " " + keep.Count + I18n.T(" 条") + (cap2 ? I18n.T("（截断，建议换类型或再筛）") : ""));
        }); } catch { }
        return "快照筛:变化 → " + keep.Count + " 条";
      });
    }
  }

  void DoStabilize() {
    if (!RequireH()) return;
    if (scanHits.Count == 0) { ToastMgr.Warn(I18n.T("没有可稳定化的候选")); return; }
    ScanType t = CurScanType();
    RunBackground(I18n.T("1.5s 稳定化"), delegate {
      Thread.Sleep(1500);
      List<ScanHit> keep = ScanCore.FilterHitsExact(H, t, scanHits, FilterKind.Unchanged, "0",
        delegate { return scanCancel; }, null);
      scanHits = keep;
      try { BeginInvoke((MethodInvoker)delegate {
        ShowHits(I18n.T("稳定化"));
        ToastMgr.Show(I18n.T("稳定化后剩 ") + keep.Count + I18n.T(" 条（被游戏自己重算的已剔除）"));
      }); } catch { }
      return "稳定化（1.5s未变化）后剩 " + keep.Count + " 条";
    });
  }

  void DoProbe() {
    if (!RequireH()) return;
    ScanHit h = SelectedHit();
    if (h == null) { ToastMgr.Warn(I18n.T("先在结果里选中一行")); return; }
    ScanType t = CurScanType();
    string cur; string err;
    if (!MemIO.ReadValue(H, h.Addr, t, out cur, out err)) { ToastMgr.Warn(I18n.T("读取失败: ") + err); return; }
    string nv;
    if (TypeUtil.IsFloat(t)) { double d; double.TryParse(cur, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d); nv = (d + 1.0).ToString(System.Globalization.CultureInfo.InvariantCulture); }
    else { long l; try { l = TypeUtil.ParseInt(cur); } catch { l = 0; } nv = (l + 1).ToString(); }
    byte[] wrote;
    if (!MemIO.SafeWriteValue(H, h.Addr, t, nv, out wrote, out err)) { ToastMgr.Warn(I18n.T("试写失败: ") + err); return; }
    Log("试写+1 @" + "0x" + h.Addr.ToString("X") + " : " + cur + " → " + nv);
    ToastMgr.Show(I18n.T("已试写 +1 @0x") + h.Addr.ToString("X") + I18n.T("（回游戏看现象确认）"));
  }

  void DoBook() {
    ScanHit h = SelectedHit();
    if (h == null) { ToastMgr.Warn(I18n.T("先在结果里选中一行")); return; }
    string desc = txtScanDesc.Text.Trim();
    if (desc.Length == 0) desc = "候选@0x" + h.Addr.ToString("X");
    ScanType t = CurScanType();
    string cur; string err;
    MemIO.ReadValue(H, h.Addr, t, out cur, out err);
    BookPut(desc, desc, h.Addr, t, cur, "scan");
    LockAdd(desc, desc, h.Addr, t, cur, false, "scan");
    for (int i = 0; i < Locks.Count; i++) if (Locks[i].Key == desc) Locks[i].Active = false;
    RefreshAddrTable();
    Log("已加入地址表: " + desc + " @0x" + h.Addr.ToString("X") + " = " + cur + "（未锁定）");
    ToastMgr.Show(I18n.T("已加入右侧地址表（未锁定）"));
  }

  void SetPb(long done, long total) {
    try {
      if (pbScan == null || total <= 0) return;
      int v = (int)(done * 100 / total);
      if (v < 0) v = 0; if (v > 100) v = 100;
      BeginInvoke((MethodInvoker)delegate { pbScan.Value = v; });
    } catch { }
  }
}

} // namespace
