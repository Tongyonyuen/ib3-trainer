// ============================================================================
// ConfigSelfTest.cs — 配置文件改写（GameConfig.cs / ParryGem）的离线夹具自测
//                     **不进 build.sh 的 SRC 列表**
//
//   sh build.sh cfgtest    → 编译并运行 cfgtest.exe
//
// 夹具全部内联，写在系统临时目录里，**绝不碰真实游戏目录**。
// 唯一会读真实文件的是最后一条 real.probe，它**只读**，文件不存在就记 SKIP。
//
// 为什么这条链值得单独钉：本次要改的这一个键，在两个真实文件里都有**两处**同值出现
//   （全招架宝石段 + Potion_ParryAll 药水段），而"还原"方向最容易踩的坑正是
//   全局替换把药水段的 BattleEffect 也改掉 —— potion.untouched 就是那根钉。
//
// C# 5：不能用字符串插值、?.、out var、表达式体成员。
// ============================================================================
using System;
using System.IO;
using System.Text;

namespace Ib3Trainer2 {

static class ConfigSelfTest {

  static int fail = 0, total = 0;

  static void T(string name, Func<string> f) {
    total++;
    string why = null;
    try { why = f(); } catch (Exception ex) { why = "抛异常: " + ex.GetType().Name + ": " + ex.Message; }
    if (why == null) { Console.WriteLine("PASS " + name); }
    else { fail++; Console.WriteLine("FAIL " + name + "  —— " + why); }
  }

  static string Eq(string got, string want, string what) {
    if (got == want) return null;
    return what + " 期望 [" + (want == null ? "<null>" : want) + "]，实得 [" + (got == null ? "<null>" : got) + "]";
  }
  static string EqL(long got, long want, string what) {
    if (got == want) return null;
    return what + " 期望 " + want + "，实得 " + got;
  }

  // -------------------------------------------------------------------------
  // 夹具
  // -------------------------------------------------------------------------

  // 刻意把 Potion_ParryAll 段放在宝石段**之前**，且它的 BattleEffect 值就等于 VAL_ON ——
  // 这正是"全局替换/全局还原会写坏药水段"的真实形状。
  // 末尾的 PerfectParryGem_1 段里还有一行 `MPParent=GreatParryAllGem`，
  // 用来钉住"段头必须按行首 + 方括号 + 全名匹配"（否则会误命中 MPParent 那一行）。
  static string Body(string nl) {
    string[] lines = new string[] {
      "[AttackGem SwordInventoryItemGem]",
      "ItemType=SIT_Gem",
      "BattleEffect=BE_PlayerStats",
      "",
      "[Potion_ParryAll SwordInventoryItemGem]",
      "ItemType=SIT_Potion",
      "BattleEffect=BE_Time_ParryAllAttacks",
      "BattleEffectValue=1",
      "",
      "[GreatParryAllGem SwordInventoryItemGem]",
      "ItemType=SIT_Gem",
      "ItemSubType=4",
      "BattleTrigger=BT_Passive",
      "BattleEffect=BE_VarAdd_GreatParryAll",
      "BattleEffectValue=1",
      "Cost=12500000",
      "bUniqueItem=TRUE",
      "",
      "[PerfectParryGem_1 SwordInventoryItemGem]",
      "BattleEffect=BE_PlayerGetHealth",
      "MPParent=GreatParryAllGem"
    };
    return string.Join(nl, lines) + nl;
  }

  static string NewDir() {
    string d = Path.Combine(Path.GetTempPath(),
               "ib3cfg_" + Guid.NewGuid().ToString("N").Substring(0, 8));
    Directory.CreateDirectory(d);
    return d;
  }

  static string Write(string path, string text) {
    File.WriteAllBytes(path, Encoding.ASCII.GetBytes(text));
    return path;
  }

  // -------------------------------------------------------------------------
  // 断言辅助
  // -------------------------------------------------------------------------

  // 只允许 [vs,ve) 这一段变（写入用的是同一个函数算出来的区间时才有意义 —— 这里用于
  // 从"改写前的字节"和"改写后的字节"反推：前缀与后缀必须逐字节相同）。
  static string OnlyRangeChanged(byte[] before, byte[] after, int vs, int ve, string newVal) {
    int delta = newVal.Length - (ve - vs);
    if (after.Length != before.Length + delta)
      return "长度不对：改写前 " + before.Length + "，改写后 " + after.Length + "，应为 " + (before.Length + delta);
    for (int i = 0; i < vs; i++) if (before[i] != after[i]) return "值区间之前第 " + i + " 字节被改动";
    for (int i = ve; i < before.Length; i++) {
      if (before[i] != after[i + delta]) return "值区间之后第 " + i + " 字节被改动";
    }
    return null;
  }

  static string SectionValue(string path, string section) {
    string v, e;
    int rc = GameConfig.ReadKey(path, section, "BattleEffect", out v, out e);
    if (rc != GameConfig.RC_OK) return "<读不到: " + e + ">";
    return v;
  }

  // 换行风格指纹：CRLF 数与裸 LF 数。改写前后必须一致（"字节级定点替换"的核心承诺）。
  static string EolFingerprint(byte[] b) {
    int crlf = 0, lf = 0;
    for (int i = 0; i < b.Length; i++) {
      if (b[i] == (byte)'\n') { lf++; if (i > 0 && b[i - 1] == (byte)'\r') crlf++; }
    }
    return "CRLF=" + crlf + " LF=" + lf;
  }

  // -------------------------------------------------------------------------

  static void Main() {
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine("=== 配置文件改写离线自测（不碰真实游戏目录）===");
    Console.WriteLine();

    // ---------------- ① 段落定位 ----------------
    T("section.find", delegate {
      byte[] b = Encoding.ASCII.GetBytes(Body("\n"));
      int bs, be;
      if (!GameConfig.FindSection(b, ParryGem.SECTION, out bs, out be)) return "没找到全招架宝石段";
      string seg = GameConfig.Ascii(b, bs, be);
      if (seg.IndexOf("BattleEffectValue=1") < 0) return "段体不含预期的 BattleEffectValue";
      if (seg.IndexOf("Potion_ParryAll") >= 0) return "段体越界串进了药水段";
      if (seg.IndexOf("PerfectParryGem_1") >= 0) return "段体越界串进了下一段";
      return null;
    });

    T("not.linestart", delegate {
      // `MPParent=GreatParryAllGem` 不得被当成段头
      byte[] b = Encoding.ASCII.GetBytes(Body("\n"));
      int bs, be;
      if (!GameConfig.FindSection(b, ParryGem.SECTION, out bs, out be)) return "没找到段";
      int mp = -1;
      for (int i = 0; i + 4 <= b.Length; i++) {
        if (b[i]=='M'&&b[i+1]=='P'&&b[i+2]=='P'&&b[i+3]=='a') { mp = i; break; }
      }
      if (mp < 0) return "夹具里没有 MPParent 行";
      if (mp >= bs && mp < be) return "MPParent 行被算进了段体（段边界错）";
      return null;
    });

    // ---------------- ② 键定位：不能抓到 BattleEffectValue ----------------
    T("key.not.value", delegate {
      byte[] b = Encoding.ASCII.GetBytes(Body("\n"));
      int bs, be; GameConfig.FindSection(b, ParryGem.SECTION, out bs, out be);
      int vs, ve;
      if (!GameConfig.FindKey(b, bs, be, "BattleEffect", out vs, out ve)) return "没找到 BattleEffect 键";
      string v = GameConfig.Ascii(b, vs, ve);
      return Eq(v, ParryGem.VAL_OFF, "取到的应当是 BattleEffect 的值而非 BattleEffectValue");
    });

    // ---------------- ③ 药水段不得被误伤（★ 核心回归钉） ----------------
    T("potion.untouched.on", delegate {
      string d = NewDir();
      try {
        string p = Write(Path.Combine(d, "g.ini"), Body("\n"));
        byte[] before = File.ReadAllBytes(p);
        byte[] pre = (byte[])before.Clone();
        int bs, be; GameConfig.FindSection(pre, ParryGem.SECTION, out bs, out be);
        int vs, ve; GameConfig.FindKey(pre, bs, be, "BattleEffect", out vs, out ve);

        string err;
        int rc = GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        if (rc != GameConfig.RC_OK) return "写入失败: " + err;

        byte[] after = File.ReadAllBytes(p);
        string bad = OnlyRangeChanged(before, after, vs, ve, ParryGem.VAL_ON);
        if (bad != null) return bad;
        if (EolFingerprint(before) != EolFingerprint(after)) return "换行风格变了：" + EolFingerprint(before) + " → " + EolFingerprint(after);
        return Eq(SectionValue(p, "Potion_ParryAll SwordInventoryItemGem"), ParryGem.VAL_ON, "药水段应保持原样");
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    // 反方向才是真正的陷阱：把 VAL_ON 还原成 VAL_OFF 时，全局替换会把药水段也写成 VAL_OFF
    T("potion.untouched.off", delegate {
      string d = NewDir();
      try {
        string p = Write(Path.Combine(d, "g.ini"), Body("\n"));
        string err;
        GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        int rc = GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_OFF, out err);
        if (rc != GameConfig.RC_OK) return "还原失败: " + err;
        string pot = SectionValue(p, "Potion_ParryAll SwordInventoryItemGem");
        if (pot != ParryGem.VAL_ON) return "★ 药水段被还原动作写坏了：" + pot;
        return Eq(SectionValue(p, ParryGem.SECTION), ParryGem.VAL_OFF, "宝石段应已还原");
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    // ---------------- ④ 幂等 ----------------
    T("idempotent", delegate {
      string d = NewDir();
      try {
        string p = Write(Path.Combine(d, "g.ini"), Body("\n"));
        string err;
        GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        byte[] a = File.ReadAllBytes(p);
        DateTime t1 = File.GetLastWriteTimeUtc(p);
        int rc = GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        if (rc != GameConfig.RC_OK) return "第二次写入返回失败: " + err;
        byte[] b2 = File.ReadAllBytes(p);
        if (a.Length != b2.Length) return "幂等写成改了长度";
        for (int i = 0; i < a.Length; i++) if (a[i] != b2[i]) return "幂等写成改了内容";
        if (File.GetLastWriteTimeUtc(p) != t1) return "幂等路径不应触碰文件（mtime 变了）";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    // ---------------- ⑤ 往返 ----------------
    T("roundtrip", delegate {
      string d = NewDir();
      try {
        string p = Write(Path.Combine(d, "g.ini"), Body("\n"));
        byte[] orig = File.ReadAllBytes(p);
        string err;
        GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_OFF, out err);
        byte[] now = File.ReadAllBytes(p);
        if (now.Length != orig.Length) return "往返后长度不一致";
        for (int i = 0; i < orig.Length; i++) if (orig[i] != now[i]) return "往返后第 " + i + " 字节不一致（应逐字节还原）";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    // ---------------- ⑥ CRLF / 段在文件尾与行首 / 尾随空格 ----------------
    T("crlf", delegate {
      string d = NewDir();
      try {
        string p = Write(Path.Combine(d, "g.ini"), Body("\r\n"));
        byte[] before = File.ReadAllBytes(p);
        string err;
        int vs, ve; byte[] pre = (byte[])before.Clone();
        int bs, be; GameConfig.FindSection(pre, ParryGem.SECTION, out bs, out be);
        GameConfig.FindKey(pre, bs, be, "BattleEffect", out vs, out ve);
        if (GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err) != GameConfig.RC_OK)
          return "写入失败: " + err;
        byte[] after = File.ReadAllBytes(p);
        string bad = OnlyRangeChanged(before, after, vs, ve, ParryGem.VAL_ON);
        if (bad != null) return bad;
        if (EolFingerprint(before) != EolFingerprint(after)) return "CRLF 被改坏：" + EolFingerprint(before) + " → " + EolFingerprint(after);
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("firstline.and.eof", delegate {
      string d = NewDir();
      try {
        // 段头在文件第 0 字节、且是最后一段、且文件末尾没有换行
        string text = "[GreatParryAllGem SwordInventoryItemGem]\r\nBattleEffect=BE_VarAdd_GreatParryAll\r\nBattleEffectValue=1";
        string p = Write(Path.Combine(d, "g.ini"), text);
        string err;
        if (GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err) != GameConfig.RC_OK)
          return "写入失败: " + err;
        if (SectionValue(p, ParryGem.SECTION) != ParryGem.VAL_ON) return "值没改成";
        byte[] now = File.ReadAllBytes(p);
        if (now[now.Length - 1] == (byte)'\n') return "末尾被加上了换行";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("trailing.space", delegate {
      string d = NewDir();
      try {
        string text = "[GreatParryAllGem SwordInventoryItemGem]\nBattleEffect=BE_VarAdd_GreatParryAll   \nBattleEffectValue=1\n";
        string p = Write(Path.Combine(d, "g.ini"), text);
        string err;
        if (GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err) != GameConfig.RC_OK)
          return "写入失败: " + err;
        byte[] now = File.ReadAllBytes(p);
        string s = Encoding.ASCII.GetString(now);
        if (s.IndexOf("BattleEffect=BE_Time_ParryAllAttacks   \n") < 0) return "尾随空格没有保留：" + s.Replace("\n", "\\n");
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    // ---------------- ⑦ 必须被拒绝 ----------------
    T("reject.missingfile", delegate {
      string d = NewDir();
      try {
        string err;
        int rc = GameConfig.WriteKey(Path.Combine(d, "nope.ini"), ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        if (rc != GameConfig.RC_FILE_MISSING) return "应返回 RC_FILE_MISSING，实得 " + rc;
        if (!File.Exists(Path.Combine(d, "nope.ini"))) return null;   // 没被创建 ✓
        return "不该凭空创建文件";
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("reject.missingsection", delegate {
      string d = NewDir();
      try {
        string p = Write(Path.Combine(d, "g.ini"), "[AttackGem SwordInventoryItemGem]\nBattleEffect=BE_PlayerStats\n");
        byte[] before = File.ReadAllBytes(p);
        string err;
        int rc = GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        if (rc != GameConfig.RC_SECTION_MISSING) return "应返回 RC_SECTION_MISSING，实得 " + rc;
        byte[] after = File.ReadAllBytes(p);
        if (before.Length != after.Length) return "拒绝路径改动了文件";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("reject.missingkey", delegate {
      string d = NewDir();
      try {
        string p = Write(Path.Combine(d, "g.ini"), "[GreatParryAllGem SwordInventoryItemGem]\nItemType=SIT_Gem\nCost=1\n");
        byte[] before = File.ReadAllBytes(p);
        string err;
        int rc = GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        if (rc != GameConfig.RC_KEY_MISSING) return "应返回 RC_KEY_MISSING，实得 " + rc;
        byte[] after = File.ReadAllBytes(p);
        if (before.Length != after.Length) return "拒绝路径改动了文件";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("reject.thirdvalue", delegate {
      string d = NewDir();
      try {
        string p = Write(Path.Combine(d, "g.ini"),
          "[GreatParryAllGem SwordInventoryItemGem]\nBattleEffect=BE_BossTakeDamage\nBattleEffectValue=1\n");
        byte[] before = File.ReadAllBytes(p);
        string detail;
        string err = ParryGem.Apply(true, p, null, out detail);
        if (err == null) return "第三种值竟被接受（会把别人的改动抹掉）";
        if (err.IndexOf("BE_BossTakeDamage") < 0) return "拒绝原因没带上现值：" + err;
        byte[] after = File.ReadAllBytes(p);
        if (before.Length != after.Length) return "拒绝路径改动了文件";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("reject.readonly", delegate {
      string d = NewDir();
      try {
        string p = Write(Path.Combine(d, "g.ini"), Body("\n"));
        byte[] before = File.ReadAllBytes(p);
        File.SetAttributes(p, FileAttributes.ReadOnly);
        try {
          string err;
          int rc = GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
          if (rc != GameConfig.RC_IO_ERROR) return "应返回 RC_IO_ERROR，实得 " + rc;
          if (err == null || err.IndexOf("只读") < 0) return "原因里应写明只读：" + err;
          byte[] after = File.ReadAllBytes(p);
          if (before.Length != after.Length) return "拒绝路径改动了文件";
          return null;
        } finally { File.SetAttributes(p, FileAttributes.Normal); }
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("reject.utf16", delegate {
      string d = NewDir();
      try {
        string p = Path.Combine(d, "u.ini");
        File.WriteAllBytes(p, Encoding.Unicode.GetBytes(Body("\r\n")));   // 带 NUL 的 UTF-16
        string err;
        int rc = GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        if (rc != GameConfig.RC_IO_ERROR) return "UTF-16 夹具应被拒绝，实得 " + rc;
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    // ---------------- ⑧ 路径含中文 ----------------
    T("path.chinese", delegate {
      string d = NewDir();
      try {
        string sub = Path.Combine(d, "训练器自测");
        Directory.CreateDirectory(sub);
        string p = Write(Path.Combine(sub, "宝石配置.ini"), Body("\r\n"));
        string err;
        if (GameConfig.WriteKey(p, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err) != GameConfig.RC_OK)
          return "中文路径写入失败: " + err;
        return Eq(SectionValue(p, ParryGem.SECTION), ParryGem.VAL_ON, "中文路径下应能读回");
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    // ---------------- ⑨ 生效态：用户侧优先 ----------------
    T("effective.userwins", delegate {
      string d = NewDir();
      try {
        string u = Write(Path.Combine(d, "SwordGems.ini"), Body("\r\n"));       // 用户侧原版(OFF)
        string g = Write(Path.Combine(d, "DefaultGems.ini"), Body("\n"));
        string err;
        GameConfig.WriteKey(u, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);   // 只改用户侧
        string uv, ue, dv, de; int us, ds;
        int st = ParryGem.EffectiveState(u, g, out uv, out us, out ue, out dv, out ds, out de);
        if (st != ParryGem.ST_ON) return "★ 用户侧 ON + 游戏侧 OFF 必须判为已开启，实得状态 " + st;
        if (us != ParryGem.ST_ON) return "用户侧状态应为 ON";
        if (ds != ParryGem.ST_OFF) return "游戏侧状态应为 OFF";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("effective.fallback", delegate {
      string d = NewDir();
      try {
        string g = Write(Path.Combine(d, "DefaultGems.ini"), Body("\n"));       // 只有游戏侧
        string err;
        GameConfig.WriteKey(g, ParryGem.SECTION, "BattleEffect", ParryGem.VAL_ON, out err);
        string uv, ue, dv, de; int us, ds;
        int st = ParryGem.EffectiveState(Path.Combine(d, "no_such.ini"), g,
                                         out uv, out us, out ue, out dv, out ds, out de);
        if (st != ParryGem.ST_ON) return "用户侧缺失时应回退到游戏侧，实得状态 " + st;
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    // ---------------- ⑩ Apply：用户侧必成功、游戏侧尽力而为 ----------------
    T("apply.both", delegate {
      string d = NewDir();
      try {
        string u = Write(Path.Combine(d, "SwordGems.ini"), Body("\r\n"));
        string g = Write(Path.Combine(d, "DefaultGems.ini"), Body("\n"));
        string detail;
        string err = ParryGem.Apply(true, u, g, out detail);
        if (err != null) return "Apply 失败: " + err;
        if (SectionValue(u, ParryGem.SECTION) != ParryGem.VAL_ON) return "用户侧没写成";
        if (SectionValue(g, ParryGem.SECTION) != ParryGem.VAL_ON) return "游戏侧没写成";
        if (detail == null || detail.IndexOf(u) < 0 || detail.IndexOf(g) < 0)
          return "detail 里应含两条完整路径：" + detail;
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("apply.gameside.blocked", delegate {
      string d = NewDir();
      try {
        string u = Write(Path.Combine(d, "SwordGems.ini"), Body("\r\n"));
        string g = Write(Path.Combine(d, "DefaultGems.ini"), Body("\n"));
        File.SetAttributes(g, FileAttributes.ReadOnly);     // 游戏侧写不了
        try {
          string detail;
          string err = ParryGem.Apply(true, u, g, out detail);
          if (err != null) return "游戏侧失败不应判整体失败，实得: " + err;
          if (SectionValue(u, ParryGem.SECTION) != ParryGem.VAL_ON) return "用户侧必须仍然写成";
          if (detail == null || detail.IndexOf("写入失败") < 0) return "detail 里应记下游戏侧失败：" + detail;
          return null;
        } finally { File.SetAttributes(g, FileAttributes.Normal); }
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    // ---------------- ⑪ 只读探真实文件（绝不写） ----------------
    T("real.probe", delegate {
      string user = GameConfig.UserGemsPath();
      string root = ReadLauncherDir();
      string def = GameConfig.DefaultGemsPath(root);
      string uv, ue, dv, de;
      int us = ParryGem.FileState(user, out uv, out ue);
      Console.WriteLine("       用户侧 " + user + " → " + ParryGem.StateText(us) + (uv == null ? "" : " (" + uv + ")"));
      if (def == null) {
        Console.WriteLine("       游戏侧 <未找到 ib3_paths.ini，跳过>");
      } else {
        int ds = ParryGem.FileState(def, out dv, out de);
        Console.WriteLine("       游戏侧 " + def + " → " + ParryGem.StateText(ds) + (dv == null ? "" : " (" + dv + ")"));
      }
      if (us == ParryGem.ST_FILE_MISSING) return "SKIP 用户侧配置不存在（还没启动过游戏）";
      return null;
    });

    Console.WriteLine();
    if (fail == 0) Console.WriteLine("RESULT cfgtest = " + total + "/" + total + " PASS");
    else Console.WriteLine("RESULT cfgtest = " + (total - fail) + "/" + total + " PASS, " + fail + " FAIL");
    Environment.ExitCode = (fail == 0) ? 0 : 1;
  }

  // 从本程序所在目录的 ib3_paths.ini 取启动器目录 → 上探一级得游戏根。
  // 这里刻意不引用 Launcher.cs（那是产品代码，会拖进 Win32/窗体依赖），
  // 只为让 real.probe 能定位真实文件；取不到就返回 null（探针会记"跳过"）。
  static string ReadLauncherDir() {
    try {
      string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ib3_paths.ini");
      if (!File.Exists(f)) return null;
      foreach (string ln in File.ReadAllLines(f)) {
        string t = ln.Trim();
        if (t.StartsWith("LauncherDir=")) {
          string dir = t.Substring(12).Trim();
          if (dir.Length > 0 && Directory.Exists(dir)) return Path.GetDirectoryName(dir);
        }
      }
    } catch { }
    return null;
  }
}

} // namespace
