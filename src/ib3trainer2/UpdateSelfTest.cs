// ============================================================================
// UpdateSelfTest.cs — 更新器的离线夹具自测（**不进 build.sh 的 SRC 列表**）
//
//   sh build.sh updatetest    → 编译并运行 updatetest.exe
//
// 夹具全部是内联的 C# 逐字字符串，**不读磁盘、不联网、不发 release** —— 所以它可以在
// 任何时候跑，不需要先有一个真实的 release 存在。（照 GemSelfTest.cs / EngineTest2.cs 的先例：
// 两者也都不在 SRC 里，各有自己的 Main。）
//
// 真实的 GitHub 响应形状取自 api.github.com 的实测抓包：
//   release 里 "name"（发布标题）出现在 "assets" **之前**，所以裸找 "name" 会抓到标题 ——
//   本测试的典型夹具故意把这两者写成不同值来钉住这个陷阱。
//
// C# 5：不能用字符串插值、?.、out var、表达式体成员。
// ============================================================================
using System;
using System.IO;
using System.Text;

namespace Ib3Trainer2 {

static class UpdateSelfTest {

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

  // 与真实 /releases/latest 同形：author 对象、release 级 html_url、发布标题与资产名不同、
  // 两个资产且 .exe **排在后面**、label 字段、标签带 null。
  static string JsonTypical(int dataRev) {
    return @"{
  ""url"": ""https://api.github.com/repos/Tongyonyuen/ib3-trainer/releases/1"",
  ""assets_url"": ""https://api.github.com/repos/Tongyonyuen/ib3-trainer/releases/1/assets"",
  ""upload_url"": ""https://uploads.github.com/repos/Tongyonyuen/ib3-trainer/releases/1/assets{?name,label}"",
  ""html_url"": ""https://github.com/Tongyonyuen/ib3-trainer/releases/tag/v1.2.0"",
  ""id"": 1,
  ""author"": {
    ""login"": ""Tongyonyuen"",
    ""id"": 2,
    ""node_id"": ""U_x"",
    ""avatar_url"": ""https://avatars.githubusercontent.com/u/2?v=4"",
    ""html_url"": ""https://github.com/Tongyonyuen"",
    ""type"": ""User""
  },
  ""node_id"": ""RE_x"",
  ""tag_name"": ""v1.2.0"",
  ""target_commitish"": ""main"",
  ""name"": ""v1.2.0 · 发布标题（故意与资产名不同）"",
  ""draft"": false,
  ""prerelease"": false,
  ""created_at"": ""2026-10-08T00:00:00Z"",
  ""published_at"": ""2026-10-08T00:00:00Z"",
  ""assets"": [
    {
      ""url"": ""https://api.github.com/repos/x/y/releases/assets/10"",
      ""id"": 10,
      ""node_id"": ""RA_x"",
      ""name"": ""IB3Trainer2_1.2.0_full.zip"",
      ""label"": """",
      ""uploader"": { ""login"": ""Tongyonyuen"", ""id"": 2, ""type"": ""User"" },
      ""content_type"": ""application/zip"",
      ""state"": ""uploaded"",
      ""size"": 4031721,
      ""digest"": ""sha256:aaaa"",
      ""download_count"": 3,
      ""created_at"": ""2026-10-08T00:00:00Z"",
      ""updated_at"": ""2026-10-08T00:00:00Z"",
      ""browser_download_url"": ""https://github.com/x/y/releases/download/v1.2.0/IB3Trainer2_1.2.0_full.zip""
    },
    {
      ""url"": ""https://api.github.com/repos/x/y/releases/assets/11"",
      ""id"": 11,
      ""node_id"": ""RA_y"",
      ""name"": ""IB3Trainer2.exe"",
      ""label"": null,
      ""uploader"": { ""login"": ""Tongyonyuen"", ""id"": 2, ""type"": ""User"" },
      ""content_type"": ""application/octet-stream"",
      ""state"": ""uploaded"",
      ""size"": 534000,
      ""digest"": ""sha256:bbbb"",
      ""download_count"": 9,
      ""created_at"": ""2026-10-08T00:00:00Z"",
      ""updated_at"": ""2026-10-08T00:00:00Z"",
      ""browser_download_url"": ""https://github.com/x/y/releases/download/v1.2.0/IB3Trainer2.exe""
    }
  ],
  ""tarball_url"": ""https://api.github.com/repos/x/y/tarball/v1.2.0"",
  ""zipball_url"": ""https://api.github.com/repos/x/y/zipball/v1.2.0"",
  ""body"": ""首个版本\n\nDATA_REV: " + dataRev + @"\n改了什么""
}";
  }

  static string JsonNoAssets() {
    return @"{""tag_name"":""v1.3.0"",""name"":""v1.3.0"",""html_url"":""https://github.com/x"",""assets"":[],""body"":null}";
  }

  static string JsonZipOnly() {
    return @"{""tag_name"":""v1.3.0"",""html_url"":""https://github.com/x"",""assets"":[{" +
           @"""name"":""only.zip"",""size"":10,""browser_download_url"":""https://github.com/x/only.zip""}],""body"":""""}";
  }

  static string JsonEscapes() {
    // 目标：JSON 里的字符串值为  a"b\c/d<LF>e<TAB>f中g
    // 对应的 JSON 文本是 "a\"b\\c\/d\ne\tf中g"，写进 C# 逐字字符串时：
    //   \" 要写成 \""   （反斜杠 + 两个引号 → 反斜杠 + 一个引号）
    //   \\ 要写成 \\    （逐字字符串不处理反斜杠，原样保留两个）
    //   \n \t \uXXXX 原样写
    return @"{""tag_name"":""v9.9.9"",""html_url"":""https://github.com/x"",""assets"":[]," +
           @"""body"":""a\""b\\c\/d\ne\tf中g""}";
  }

  static string JsonDigestNull() {
    return @"{""tag_name"":""v2.0.0"",""html_url"":""https://github.com/x"",""assets"":[{" +
           @"""name"":""IB3Trainer2.exe"",""size"":534000,""digest"":null," +
           @"""browser_download_url"":""https://github.com/x/IB3Trainer2.exe""}],""body"":""x""}";
  }

  // MZ 头 + 足够大，供 VerifyPayload 用
  static byte[] FakeExe(int size) {
    byte[] b = new byte[size];
    b[0] = (byte)'M'; b[1] = (byte)'Z';
    for (int i = 2; i < size; i++) b[i] = (byte)(i & 0xFF);
    return b;
  }

  static string TempDir() {
    string d = Path.Combine(Path.GetTempPath(), "ib3_updtest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
    Directory.CreateDirectory(d);
    return d;
  }

  // -------------------------------------------------------------------------

  static void Main() {
    Console.WriteLine("== IB3 训练器2 更新器自测 ==");

    // ---- 版本解析 ----
    T("semver.parse", delegate {
      int a, b, c;
      if (!Updater.TryParseTag("v1.2.3", out a, out b, out c)) return "v1.2.3 应通过";
      if (a != 1 || b != 2 || c != 3) return "v1.2.3 解析错";
      if (!Updater.TryParseTag("1.2.3", out a, out b, out c)) return "无 v 前缀应通过";
      if (!Updater.TryParseTag("v1.2.3-beta.1", out a, out b, out c)) return "预发布后缀应通过";
      if (a != 1 || b != 2 || c != 3) return "预发布版本号应取数字三元组";
      if (!Updater.TryParseTag("v10.20.30", out a, out b, out c)) return "两位数应通过";
      if (a != 10 || b != 20 || c != 30) return "两位数解析错";
      return null;
    });

    T("semver.reject", delegate {
      int a, b, c;
      string[] bad = { "r13", "v01.2.3", "v1.2", "v1.2.3.4", "", "v1.2.x", "1.2.3.4", "v", "release-1.0.0" };
      for (int i = 0; i < bad.Length; i++) {
        if (Updater.TryParseTag(bad[i], out a, out b, out c))
          return "应拒绝却通过了：「" + bad[i] + "」";
      }
      return null;
    });

    // 防止发版时忘了同步 BuildInfo
    T("semver.selfconsistent", delegate {
      int a, b, c;
      if (!Updater.TryParseTag("v" + Updater.LocalSemVer, out a, out b, out c))
        return "BuildInfo.SemVer（" + Updater.LocalSemVer + "）自己都不是合法 semver";
      if (a != BuildInfo.Major || b != BuildInfo.Minor || c != BuildInfo.Patch)
        return "SemVer 与 Major/Minor/Patch 不一致";
      return null;
    });

    T("newer.table", delegate {
      int M = BuildInfo.Major, m = BuildInfo.Minor, p = BuildInfo.Patch;
      if (Updater.IsNewer(M, m, p)) return "同版本不该算更新";
      if (!Updater.IsNewer(M, m, p + 1)) return "补丁 +1 应算更新";
      if (!Updater.IsNewer(M, m + 1, 0)) return "次版本 +1 应算更新";
      if (!Updater.IsNewer(M + 1, 0, 0)) return "主版本 +1 应算更新";
      if (p > 0 && Updater.IsNewer(M, m, p - 1)) return "补丁 -1 不该算更新";
      if (M > 0 && Updater.IsNewer(M - 1, 99, 99)) return "主版本 -1 不该算更新";
      return null;
    });

    // ---- JSON 解析 ----
    T("json.typical", delegate {
      string err;
      UpdateInfo i = Updater.Parse(JsonTypical(BuildInfo.DataRev + 1), out err);
      if (i == null) return "解析失败: " + err;
      string r;
      r = Eq(i.Tag, "v1.2.0", "tag_name"); if (r != null) return r;
      r = Eq(i.HtmlUrl, "https://github.com/Tongyonyuen/ib3-trainer/releases/tag/v1.2.0", "html_url（须是 release 自己的，不是 author.html_url）"); if (r != null) return r;
      r = Eq(i.AssetName, "IB3Trainer2.exe", "资产名（.exe 排在 .zip 之后，必须跳过 zip）"); if (r != null) return r;
      r = EqL(i.AssetSize, 534000, "资产大小"); if (r != null) return r;
      r = Eq(i.AssetSha256, "bbbb", "digest 去掉 sha256: 前缀"); if (r != null) return r;
      r = Eq(i.AssetUrl, "https://github.com/x/y/releases/download/v1.2.0/IB3Trainer2.exe", "下载地址"); if (r != null) return r;
      if (i.Notes == null || i.Notes.IndexOf("改了什么") < 0) return "body 未取到或转义未解";
      return null;
    });

    T("json.namecollision", delegate {
      string err;
      UpdateInfo i = Updater.Parse(JsonTypical(1), out err);
      if (i == null) return "解析失败: " + err;
      if (i.AssetName != null && i.AssetName.IndexOf("发布标题") >= 0)
        return "抓到了发布标题而不是资产名（assets 切片锚定失效）";
      return null;
    });

    T("json.escapes", delegate {
      string err;
      UpdateInfo i = Updater.Parse(JsonEscapes(), out err);
      if (i == null) return "解析失败: " + err;
      return Eq(i.Notes, "a\"b\\c/d\ne\tf中g", "转义序列（\\\" \\\\ \\/ \\n \\t \\uXXXX）");
    });

    T("json.noassets", delegate {
      string err;
      UpdateInfo i = Updater.Parse(JsonNoAssets(), out err);
      if (i == null) return "解析失败: " + err;
      string r = Eq(i.Tag, "v1.3.0", "tag"); if (r != null) return r;
      if (!string.IsNullOrEmpty(i.AssetUrl)) return "空 assets 不该给出下载地址";
      if (i.Notes != null) return "body:null 应保持 null";
      return null;
    });

    T("json.ziponly", delegate {
      string err;
      UpdateInfo i = Updater.Parse(JsonZipOnly(), out err);
      if (i == null) return "解析失败: " + err;
      if (!string.IsNullOrEmpty(i.AssetUrl)) return "只有 .zip 时不该选出资产（应退化为手动下载）";
      return null;
    });

    T("json.digestnull", delegate {
      string err;
      UpdateInfo i = Updater.Parse(JsonDigestNull(), out err);
      if (i == null) return "解析失败: " + err;
      string r = Eq(i.AssetName, "IB3Trainer2.exe", "资产名"); if (r != null) return r;
      if (!string.IsNullOrEmpty(i.AssetSha256)) return "digest:null 应留空（旧资产没有摘要）";
      return null;
    });

    T("json.garbage", delegate {
      string[] bad = { null, "", "{}", "{", "not json at all" };
      for (int k = 0; k < bad.Length; k++) {
        string err;
        try { Updater.Parse(bad[k], out err); }
        catch (Exception ex) { return "输入「" + bad[k] + "」时抛了异常: " + ex.Message; }
      }
      return null;
    });

    T("json.legacytag", delegate {
      string err;
      UpdateInfo i = Updater.Parse(@"{""tag_name"":""r13"",""assets"":[],""html_url"":""https://x""}", out err);
      if (i == null) return "解析失败: " + err;
      int a, b, c;
      if (Updater.TryParseTag(i.Tag, out a, out b, out c)) return "r13 不该被当成合法 semver";
      return null;
    });

    // ---- DATA_REV ----
    T("datarev", delegate {
      int next = BuildInfo.DataRev + 1;
      if (!Updater.HasDataRevBump("DATA_REV: " + next)) return "整行就是 DATA_REV 时应识别";
      if (!Updater.HasDataRevBump("首个版本\nDATA_REV: " + next + "\n改了什么")) return "独立成行的 DATA_REV 应识别";
      if (!Updater.HasDataRevBump("  DATA_REV: " + next)) return "行首允许前导空白";
      if (Updater.HasDataRevBump("DATA_REV: " + BuildInfo.DataRev)) return "相同的 DATA_REV 不该算变更";
      if (Updater.HasDataRevBump("xDATA_REV: 99")) return "前面紧挨着别的字符时不该匹配";
      if (Updater.HasDataRevBump("说明里提到 DATA_REV: 99 这个词")) return "句中提及不该匹配（不是行首）";
      if (Updater.HasDataRevBump("DATA_REV: abc")) return "非数字不该匹配";
      if (Updater.HasDataRevBump("DATA_REV:")) return "缺数字不该匹配";
      if (Updater.HasDataRevBump(null)) return "null 不该崩溃";
      if (Updater.HasDataRevBump("没有这个标记")) return "无标记不该匹配";
      return null;
    });

    // ---- 哈希 / 校验 ----
    T("sha256.vector", delegate {
      string h = Updater.Sha256Hex(Encoding.UTF8.GetBytes("abc"));
      return Eq(h, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA-256(abc)");
    });

    T("verify.ok", delegate {
      byte[] d = FakeExe(200 * 1024);
      UpdateInfo i = new UpdateInfo();
      i.AssetSize = d.Length;
      i.AssetSha256 = Updater.Sha256Hex(d);
      string why; bool skipped;
      if (!Updater.VerifyPayload(d, i, out why, out skipped)) return "应通过，却失败: " + why;
      if (skipped) return "有摘要时不该标记为跳过";
      return null;
    });

    T("verify.nodigest", delegate {
      byte[] d = FakeExe(200 * 1024);
      UpdateInfo i = new UpdateInfo();
      i.AssetSize = d.Length;          // 摘要留空 = 2025-06 之前上传的旧资产
      string why; bool skipped;
      if (!Updater.VerifyPayload(d, i, out why, out skipped)) return "缺摘要时应放行，却失败: " + why;
      if (!skipped) return "缺摘要时 skipped 应为 true（调用方要记警告）";
      return null;
    });

    T("verify.reject", delegate {
      string why; bool skipped;
      byte[] good = FakeExe(200 * 1024);

      UpdateInfo i1 = new UpdateInfo(); i1.AssetSha256 = Updater.Sha256Hex(good); i1.AssetSize = good.Length;
      if (Updater.VerifyPayload(null, i1, out why, out skipped)) return "null 应被拒";
      if (Updater.VerifyPayload(new byte[0], i1, out why, out skipped)) return "空数组应被拒";

      byte[] tiny = FakeExe(1000);
      UpdateInfo it = new UpdateInfo(); it.AssetSize = tiny.Length; it.AssetSha256 = Updater.Sha256Hex(tiny);
      if (Updater.VerifyPayload(tiny, it, out why, out skipped)) return "过小的文件应被拒";
      if (why == null || why.IndexOf("太小") < 0) return "过小应给出「太小」原因，实得: " + why;

      byte[] html = new byte[200 * 1024];      // 不是 MZ：模拟下到一个 HTML 错误页
      UpdateInfo ih = new UpdateInfo(); ih.AssetSha256 = Updater.Sha256Hex(html); ih.AssetSize = html.Length;
      if (Updater.VerifyPayload(html, ih, out why, out skipped)) return "缺 MZ 头应被拒";
      if (why == null || why.IndexOf("MZ") < 0) return "应给出 MZ 原因，实得: " + why;

      UpdateInfo isz = new UpdateInfo(); isz.AssetSize = good.Length + 1; isz.AssetSha256 = Updater.Sha256Hex(good);
      if (Updater.VerifyPayload(good, isz, out why, out skipped)) return "大小不符应被拒";
      if (why == null || why.IndexOf("大小不符") < 0) return "应给出大小不符原因，实得: " + why;

      UpdateInfo ihs = new UpdateInfo(); ihs.AssetSize = good.Length; ihs.AssetSha256 = "deadbeef";
      if (Updater.VerifyPayload(good, ihs, out why, out skipped)) return "sha256 不符应被拒";
      if (why == null || why.IndexOf("sha256") < 0) return "应给出 sha256 原因，实得: " + why;
      return null;
    });

    // ---- URL 白名单 ----
    T("url.safe", delegate {
      if (!Updater.IsSafeUrl("https://github.com/a/b/releases/download/v1/x.exe")) return "https 应放行";
      if (Updater.IsSafeUrl("http://github.com/a/b/x.exe")) return "http 不该放行";
      if (Updater.IsSafeUrl("file:///C:/x.exe")) return "file: 不该放行";
      if (Updater.IsSafeUrl(null) || Updater.IsSafeUrl("")) return "空不该放行";
      return null;
    });

    T("feedurl.loopback", delegate {
      UpdateState st = new UpdateState();
      if (Updater.FeedUrl(st).IndexOf("api.github.com/repos/" + Updater.REPO_DEFAULT) < 0)
        return "默认应打官方 API，实得 " + Updater.FeedUrl(st);
      st.FeedUrl = "http://127.0.0.1:8123/latest.json";
      if (Updater.FeedUrl(st) != st.FeedUrl) return "回环地址应允许覆盖";
      st.FeedUrl = "http://localhost:8123/latest.json";
      if (Updater.FeedUrl(st) != st.FeedUrl) return "localhost 应允许覆盖";
      // ★ 非回环 → **必须忽略**，否则被篡改的 ini 能把更新器指向攻击者的 exe
      st.FeedUrl = "http://evil.example.com/latest.json";
      if (Updater.FeedUrl(st).IndexOf("api.github.com") < 0) return "非回环的 feedurl 必须被忽略！";
      st.FeedUrl = "https://evil.example.com/latest.json";
      if (Updater.FeedUrl(st).IndexOf("api.github.com") < 0) return "非回环的 https feedurl 也必须被忽略";
      st.FeedUrl = null; st.Repo = "someone/other";
      if (Updater.FeedUrl(st).IndexOf("repos/someone/other/") < 0) return "repo 覆盖没生效";
      return null;
    });

    // ---- 状态文件往返 ----
    T("state.roundtrip", delegate {
      string d = TempDir();
      try {
        UpdateState a = new UpdateState();
        a.LastCheck = "2026-10-08T01:02:03Z";
        a.ETag = "W/\"abc\"";
        a.Skip = "v9.9.9";
        a.Repo = "someone/other";
        a.Ok = 0;
        a.DevDump = 1;
        Updater.SaveState(d, a);

        UpdateState b = Updater.LoadState(d);
        string r;
        r = Eq(b.LastCheck, a.LastCheck, "lastcheck"); if (r != null) return r;
        r = Eq(b.ETag, a.ETag, "etag"); if (r != null) return r;
        r = Eq(b.Skip, a.Skip, "skip"); if (r != null) return r;
        r = Eq(b.Repo, a.Repo, "repo"); if (r != null) return r;
        r = EqL(b.Ok, a.Ok, "ok"); if (r != null) return r;
        r = EqL(b.DevDump, a.DevDump, "devdump"); if (r != null) return r;

        UpdateState c = Updater.LoadState(Path.Combine(d, "nonexistent"));
        r = Eq(c.Repo, Updater.REPO_DEFAULT, "缺文件时 repo 默认值"); if (r != null) return r;
        r = EqL(c.Ok, 1, "缺文件时 ok 默认值"); if (r != null) return r;
        if (c.LastCheck != null) return "缺文件时 lastcheck 应为 null";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("state.throttle", delegate {
      UpdateState st = new UpdateState();
      if (!Updater.ShouldAutoCheck(st)) return "没有 lastcheck 时应允许检查";
      st.LastCheck = Updater.NowIso(); st.Ok = 1;
      if (Updater.ShouldAutoCheck(st)) return "刚查过（ok=1）不该立刻再查";
      st.LastCheck = DateTime.UtcNow.AddHours(-7).ToString("yyyy-MM-ddTHH:mm:ssZ");
      if (!Updater.ShouldAutoCheck(st)) return "7 小时前应允许再查";
      st.LastCheck = DateTime.UtcNow.AddHours(-1.5).ToString("yyyy-MM-ddTHH:mm:ssZ"); st.Ok = 0;
      if (!Updater.ShouldAutoCheck(st)) return "上次失败（ok=0）时 1.5 小时后就该重试";
      st.LastCheck = DateTime.UtcNow.AddHours(-0.5).ToString("yyyy-MM-ddTHH:mm:ssZ"); st.Ok = 0;
      if (Updater.ShouldAutoCheck(st)) return "上次失败后 0.5 小时还不该重试";
      st.LastCheck = DateTime.UtcNow.AddHours(5).ToString("yyyy-MM-ddTHH:mm:ssZ");
      if (Updater.ShouldAutoCheck(st)) return "时钟回拨（lastcheck 在未来）不该再查";
      return null;
    });

    T("state.skip", delegate {
      UpdateState st = new UpdateState();
      if (Updater.IsSkipped(st, "v1.0.0")) return "空的 skip 不该匹配任何版本";
      st.Skip = "v1.0.0";
      if (!Updater.IsSkipped(st, "v1.0.0")) return "相同 tag 应匹配";
      if (Updater.IsSkipped(st, "v1.0.1")) return "不同 tag 不该匹配";
      if (Updater.IsSkipped(st, null)) return "null tag 不该匹配";
      return null;
    });

    T("applymode", delegate {
      if (!Updater.IsApplyMode(new string[] { "/apply", "123", "456", "C:\\x.exe" })) return "/apply 应识别";
      if (!Updater.IsApplyMode(new string[] { "/APPLY", "1", "2", "c" })) return "大小写不敏感";
      if (Updater.IsApplyMode(new string[] { "x" })) return "普通参数不该识别";
      if (Updater.IsApplyMode(new string[0])) return "空参数不该识别";
      if (Updater.IsApplyMode(null)) return "null 不该识别";
      return null;
    });

    T("writable", delegate {
      string d = TempDir();
      try {
        if (!Updater.CanWriteDir(d)) return "临时目录应可写";
        if (File.Exists(Path.Combine(d, Updater.TMP_NAME))) return "可写探针留下了 " + Updater.TMP_NAME;
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("cleanup.nothing", delegate {
      string d = TempDir();
      try {
        string msg = Updater.CleanupLeftovers(d);
        if (msg != null) return "空目录不该报已更新，实得: " + msg;
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("cleanup.staleNew", delegate {
      string d = TempDir();
      try {
        string p = Path.Combine(d, Updater.NEW_NAME);
        File.WriteAllBytes(p, new byte[10]);
        File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-25));
        Updater.CleanupLeftovers(d);
        if (File.Exists(p)) return "超过 24 小时的 .new 应被清掉";

        File.WriteAllBytes(p, new byte[10]);          // 新鲜的要留着
        Updater.CleanupLeftovers(d);
        if (!File.Exists(p)) return "新鲜的 .new 不该被删（那是一次成功的下载）";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("cleanup.doneMarker", delegate {
      string d = TempDir();
      try {
        string done = Path.Combine(d, Updater.DONE_FILE);
        File.WriteAllLines(done, new string[] { "tag=v1.2.0", "utc=" + Updater.NowIso(), "mode=move" });
        string msg = Updater.CleanupLeftovers(d);
        if (msg == null || msg.IndexOf("v1.2.0") < 0) return "刚更新完应报出目标版本，实得: " + (msg == null ? "<null>" : msg);
        if (!File.Exists(done)) return "5 分钟内不该删除标记（回滚窗口）";

        File.WriteAllLines(done, new string[] { "tag=v1.2.0", "utc=2026-01-01T00:00:00Z", "mode=move" });
        Updater.CleanupLeftovers(d);
        if (File.Exists(done)) return "过期标记应被清理";
        return null;
      } finally { try { Directory.Delete(d, true); } catch { } }
    });

    T("paths.constants", delegate {
      // 载荷名必须以 .exe 结尾 —— 不然助手的 ShellExecute 起不来
      if (!Updater.NEW_NAME.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        return "NEW_NAME 不以 .exe 结尾: " + Updater.NEW_NAME;
      // .old 必须是"后缀"而不是完整文件名，否则会和真 exe 撞名
      // （不要写成 `OLD_SUFFIX != ".old"` —— 那是 const 对字面量，编译期恒假，等于什么都没测）
      if (Updater.OLD_SUFFIX.Length == 0 || Updater.OLD_SUFFIX[0] != '.')
        return "OLD_SUFFIX 应是以点开头的后缀，实得: " + Updater.OLD_SUFFIX;
      if (Updater.NEW_NAME == Updater.OLD_SUFFIX) return "载荷名与备份后缀不该相同";
      if (Updater.REPO_DEFAULT.IndexOf("/") < 0) return "REPO_DEFAULT 应是 owner/name";
      if (!Updater.REPO_URL.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        return "REPO_URL 必须是 https";
      return null;
    });

    Console.WriteLine();
    if (fail == 0) Console.WriteLine("RESULT updatetest = " + total + "/" + total + " PASS");
    else Console.WriteLine("RESULT updatetest = " + (total - fail) + "/" + total + " PASS, " + fail + " FAIL");
    Environment.ExitCode = (fail == 0) ? 0 : 1;
  }
}

} // namespace
