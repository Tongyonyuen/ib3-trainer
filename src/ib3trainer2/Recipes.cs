// ============================================================================
// Recipes.cs — 地址簿（ib3_addrs.ini）持久化：三层地址复用之最底层。
// 行格式: key=0x地址|类型|值|已验证|锚点|备注      （; 或 # 开头为注释）
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ib3Trainer2 {

class AddrEntry {
  public string Key = "";
  public string Desc = "";
  public long Addr;
  public ScanType Type = ScanType.I32;
  public string Val = "0";
  public bool Verified;
  public string Anchor = "";
}

static class AddrBook {
  static readonly Dictionary<string, AddrEntry> map = new Dictionary<string, AddrEntry>();
  // Dictionary 非线程安全。本表会被后台注入线程（自动绑定金币/筹码 → BookPut）与 UI 线程
  // （读取、保存、上屏同步）同时触碰，必须加锁。
  static readonly object gate = new object();

  public static int Count { get { lock (gate) { return map.Count; } } }

  // 取一份快照（浅拷贝）供调用方在锁外安全遍历 —— 直接暴露 map.Values 会在遍历途中被
  // 后台线程 Put 改掉而抛 InvalidOperationException。
  public static List<AddrEntry> Snapshot() {
    lock (gate) {
      List<AddrEntry> list = new List<AddrEntry>(map.Count);
      foreach (AddrEntry e in map.Values) {
        AddrEntry c = new AddrEntry();
        c.Key = e.Key; c.Desc = e.Desc; c.Addr = e.Addr; c.Type = e.Type;
        c.Val = e.Val; c.Verified = e.Verified; c.Anchor = e.Anchor;
        list.Add(c);
      }
      return list;
    }
  }

  public static AddrEntry Get(string key) {
    lock (gate) {
      AddrEntry e;
      if (map.TryGetValue(key, out e)) return e;
      return null;
    }
  }
  public static void Put(AddrEntry e) { lock (gate) { map[e.Key] = e; } }
  public static void Remove(string key) { lock (gate) { map.Remove(key); } }

  public static int Load(string path) { lock (gate) { return LoadCore(path); } }
  static int LoadCore(string path) {
    map.Clear();
    if (!File.Exists(path)) return 0;
    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
    foreach (string raw in lines) {
      string line = raw.Trim();
      if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
      int eq = line.IndexOf('=');
      if (eq <= 0) continue;
      string key = line.Substring(0, eq).Trim();
      string[] f = line.Substring(eq + 1).Split('|');
      if (f.Length < 3) continue;
      AddrEntry e = new AddrEntry();
      e.Key = key;
      try { e.Addr = TypeUtil.ParseInt(f[0].Trim()); } catch { continue; }
      try { e.Type = (ScanType)Enum.Parse(typeof(ScanType), f[1].Trim()); } catch { e.Type = ScanType.I32; }
      e.Val = f[2].Trim();
      if (f.Length > 3) e.Verified = f[3].Trim() == "1";
      if (f.Length > 4) e.Anchor = f[4].Trim();
      if (f.Length > 5) e.Desc = f[5].Trim().Replace('|', '/');
      map[key] = e;
    }
    return map.Count;
  }

  public static void Save(string path) { lock (gate) { SaveCore(path); } }
  static void SaveCore(string path) {
    StringBuilder sb = new StringBuilder();
    sb.AppendLine("; IB3 训练器2 地址簿（自动生成，可手编。格式: key=0x地址|类型|值|已验证|锚点|备注）");
    foreach (AddrEntry e in map.Values) {
      sb.AppendLine(e.Key + "=0x" + e.Addr.ToString("X") + "|" + e.Type + "|" + e.Val + "|" +
                    (e.Verified ? "1" : "0") + "|" + e.Anchor.Replace('|', '/') + "|" + e.Desc.Replace('|', '/'));
    }
    File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
  }
}

} // namespace
