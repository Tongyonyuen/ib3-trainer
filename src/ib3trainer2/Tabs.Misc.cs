// ============================================================================
// Tabs.Misc.cs — 助手方法：金币/筹码定位写入 + 真身结构自动绑定（UI 已并入"战斗·商店"页）
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Ib3Trainer2 {

partial class MainForm {
  TextBox txtGoldCur, txtGoldNew, txtChipCur, txtChipNew;
  Label lblGoldAddr, lblChipAddr;
  long goldAddr = 0, chipAddr = 0;
  ScanType goldType = ScanType.I64, chipType = ScanType.I32;

  // 真身结构自动绑定（附着后由后台线程调用）——金币/筹码地址每局游戏自动校正
  public void AutoBindGoldChip(long ga, long ca) {
    try {
      BeginInvoke((MethodInvoker)delegate {
        goldAddr = ga; goldType = ScanType.I64;
        chipAddr = ca; chipType = ScanType.I32;
        // Tag 记后缀种类，供语言切换时重拼（这三个后缀措辞各不相同，不能统一成一句）
        if (lblGoldAddr != null) { lblGoldAddr.Tag = "auto"; lblGoldAddr.Text = I18n.T("地址: 0x") + ga.ToString("X") + I18n.T("（真身结构·自动绑定）"); }
        if (lblChipAddr != null) { lblChipAddr.Tag = "auto"; lblChipAddr.Text = I18n.T("地址: 0x") + ca.ToString("X") + I18n.T("（真身结构·自动绑定）"); }
      });
    } catch { }
  }

  // ============ 金币/筹码定位 ============
  void LocateValue(bool gold) {
    if (!RequireH()) return;
    TextBox curBox = gold ? txtGoldCur : txtChipCur;
    string seed = curBox.Text.Trim();
    long sv;
    try { sv = TypeUtil.ParseInt(seed); } catch { ToastMgr.Warn(I18n.T("请输入游戏内当前数值")); return; }
    bool isGold = gold;
    RunBackground(gold ? I18n.T("金币定位") : I18n.T("筹码定位"), delegate {
      List<ScanHit> best = null; ScanType bestType = ScanType.I64;
      if (!isGold) {
        List<ScanHit> a = ScanCore.FirstScanKnown(H, ScanType.I32, TypeUtil.Encode(ScanType.I32, seed), 200, null, null);
        if (a.Count > 0) { best = a; bestType = ScanType.I32; }
      }
      if (best == null) {
        List<ScanHit> b = ScanCore.FirstScanKnown(H, ScanType.I64, TypeUtil.Encode(ScanType.I64, seed), 200, null, null);
        best = b; bestType = ScanType.I64;
      }
      StringBuilder sb = new StringBuilder();
      sb.Append((isGold ? "金币" : "筹码") + "扫描命中 " + best.Count + " 条").Append(Environment.NewLine);
      int show = Math.Min(8, best.Count);
      for (int i = 0; i < show; i++)
        sb.Append("  0x" + best[i].Addr.ToString("X") + " = " + TypeUtil.Decode(bestType, best[i].Prev)).Append(Environment.NewLine);
      if (best.Count == 1) {
        long ad = best[0].Addr;
        string err;
        string now;
        MemIO.ReadValue(H, ad, bestType, out now, out err);
        MethodInvoker act = delegate {
          if (isGold) { goldAddr = ad; goldType = bestType; lblGoldAddr.Text = I18n.T("地址: 0x") + ad.ToString("X") + I18n.T("（已定位 ") + TypeUtil.Name(bestType) + I18n.T("）"); BookPut("misc.gold", "金币", ad, bestType, now, "scan"); EngineCall.NoteGoldAddr(ad); Log("注入器：金币锚定玩家真身 0x" + (ad - 0x2070).ToString("X")); }
          else { chipAddr = ad; chipType = bestType; lblChipAddr.Text = I18n.T("地址: 0x") + ad.ToString("X") + I18n.T("（已定位 ") + TypeUtil.Name(bestType) + I18n.T("）"); BookPut("misc.chip", "筹码", ad, bestType, now, "scan"); }
          ToastMgr.Show((isGold ? I18n.T("金币") : I18n.T("筹码")) + I18n.T("已定位，可填写目标值后写入"));
        };
        try { BeginInvoke(act); } catch { }
        return sb.ToString();
      }
      try { BeginInvoke((MethodInvoker)delegate { ToastMgr.Warn(I18n.T("命中 ") + best.Count + I18n.T(" 条，无法自动采用：请去发现模式做两遍扫描")); }); } catch { }
      return sb.ToString();
    });
  }

  void WriteGold() {
    if (goldAddr == 0) { ToastMgr.Warn(I18n.T("请先扫描定位金币地址")); return; }
    if (!WriteOne("misc.gold", "金币", goldAddr, goldType, txtGoldNew.Text.Trim(), false, false, null)) return;
    BookPut("misc.gold", "金币", goldAddr, goldType, txtGoldNew.Text.Trim(), "scan");
    ToastMgr.Show(I18n.T("金币已设置为 ") + txtGoldNew.Text.Trim() + I18n.T("（随存档持久）"));
  }

  void WriteChip() {
    if (chipAddr == 0) { ToastMgr.Warn(I18n.T("请先扫描定位筹码地址")); return; }
    if (!WriteOne("misc.chip", "筹码", chipAddr, chipType, txtChipNew.Text.Trim(), false, false, null)) return;
    BookPut("misc.chip", "筹码", chipAddr, chipType, txtChipNew.Text.Trim(), "scan");
    ToastMgr.Show(I18n.T("筹码已设置为 ") + txtChipNew.Text.Trim() + I18n.T("（持久性未验证，建议重启确认）"));
  }

  // ---- 金币/筹码地址栏：切换语言后按新语言重拼 ----
  // 这两行是拼接串（"地址: 0x…（后缀）"），原先只在设置那一刻翻过一次 ⇒ 切语言不会自己变。
  // 后缀有三种措辞（地址簿 / 已定位 X / 真身结构·自动绑定），信息量不同，所以用 Tag 记住种类，
  // 不把它们统一成一句。只读：只用现有字段，不碰游戏内存、不写盘。
  void RenderGoldChipLabels() {
    RenderOneAddrLabel(lblGoldAddr, goldAddr, goldType);
    RenderOneAddrLabel(lblChipAddr, chipAddr, chipType);
  }
  static void RenderOneAddrLabel(Label l, long a, ScanType t) {
    if (l == null) return;
    if (a == 0) { l.Text = I18n.T("地址: 未定位"); return; }
    string kind = (l.Tag as string) == null ? "located" : (string)l.Tag;
    string suffix = (kind == "book") ? I18n.T("（地址簿）")
                  : (kind == "auto") ? I18n.T("（真身结构·自动绑定）")
                  : I18n.T("（已定位 ") + TypeUtil.Name(t) + I18n.T("）");
    l.Text = I18n.T("地址: 0x") + a.ToString("X") + suffix;
  }
}

} // namespace
