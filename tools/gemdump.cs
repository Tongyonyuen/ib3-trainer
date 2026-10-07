// gemdump.cs - extract each GemName block's template + tier + cookedvar + boost from the save
using System; using System.IO; using System.Text;
class GD {
  static void Main(string[] a) {
    var d = File.ReadAllBytes(a[0]);
    // the save's own name table: find "GlobalScaleXPGem_1" etc — resolve FName indices via the names section
    // simpler: the GemName payload = [strLen][chars] (a STRING, not FName!) per the dump — read as string
    int pos = 0; int idx = 0;
    while (true) {
      int g = IndexOf(d, Encoding.ASCII.GetBytes("GemName\0"), pos);
      if (g < 0) break;
      idx++;
      // GemName\0 then typeLen+type "NameProperty\0" then sz,arr,payload
      int p = g + 8;
      int tl = BitConverter.ToInt32(d, p);
      string type = Encoding.ASCII.GetString(d, p+4, Math.Max(0,Math.Min(tl-1, 40)));
      int r = p + 4 + tl;
      int sz = BitConverter.ToInt32(d, r);
      int arr = BitConverter.ToInt32(d, r+4);
      string nameVal = "";
      if (sz > 0 && r+12+sz <= d.Length) {
        int sl = BitConverter.ToInt32(d, r+8);
        if (sl > 0 && sl < 100) nameVal = Encoding.ASCII.GetString(d, r+12, sl-1);
        else if (sl < 0) nameVal = "[utf16 " + (-sl) + "]";
        else nameVal = "[" + sl + "]";
      }
      // scan forward for the GemTier/CookedGemVar/Boost values (the next 400 bytes)
      int tier = -999, cooked = -999, boost = -999;
      int lim = Math.Min(g + 500, d.Length - 4);
      for (int k = g; k < lim; k++) {
        if (d[k]=='G'&&d[k+1]=='e'&&d[k+2]=='m'&&d[k+3]=='T'&&d[k+4]=='i'&&d[k+5]=='e'&&d[k+6]=='r'&&d[k+7]==0) {
          // ByteProperty: sz=1,arr=0,val byte — the val = 9 bytes after the name len... find the pattern: after "GemTier\0" the next bytes = typeLen(4) "ByteProperty\0"(13) sz(4) arr(4) val(1)
          int q = k + 8;
          int t2 = BitConverter.ToInt32(d, q);
          if (t2 == 13) { tier = d[q + 4 + 13 + 4 + 4 + 4 + 0]; } // sz? no: typeLen+type(13) then sz(4) arr(4) val — hmm compute: q+4=typeLen field end... 
        }
      }
      Console.WriteLine("gem#{0}: nameVal=\"{1}\" type={2} sz={3}", idx, nameVal, type, sz);
      pos = g + 8;
    }
  }
  static int IndexOf(byte[] d, byte[] pat, int start) {
    for (int i = start; i <= d.Length - pat.Length; i++) {
      bool m = true;
      for (int j = 0; j < pat.Length; j++) if (d[i+j] != pat[j]) { m = false; break; }
      if (m) return i;
    }
    return -1;
  }
}
