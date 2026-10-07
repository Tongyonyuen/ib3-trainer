// pkgmerge4.cs - transplant INT loc package's EngineFonts objects into CHN loc package (v868)
// Final fix for IB3 console invisible in non-English languages.
// usage: pkgmerge4.exe <chnIn.upk> <int.upk> <out.upk>
using System; using System.IO; using System.Text; using System.Collections.Generic;

class PM4 {
  static int R(byte[] d, int p){ return BitConverter.ToInt32(d,p); }
  static void W(byte[] d, int p, int v){ Buffer.BlockCopy(BitConverter.GetBytes(v),0,d,p,4); }

  static List<string> ReadNames(byte[] d, out int nameOff, out int expOff, out int impOff, out int dependsOff, out int firstData) {
    var names = new List<string>();
    int flen = R(d,12); int sp = 12+4+flen+4;
    int nc = R(d,sp), no = R(d,sp+4);
    expOff = R(d,sp+12); impOff = R(d,sp+20); dependsOff = R(d,sp+24);
    firstData = R(d,8);
    nameOff = no;
    int q = no;
    for (int i=0;i<nc;i++) { int l = R(d,q); q+=4;
      if (l>0) { names.Add(Encoding.ASCII.GetString(d,q,Math.Max(0,l-1))); q+=l; }
      else if (l<0) { int c=-l; names.Add(Encoding.Unicode.GetString(d,q,Math.Max(0,c*2-2))); q+=c*2; }
      else names.Add("");
      q+=8; }
    return names;
  }

  static int idxNone, tagStructProperty, tagByteProperty, tagBoolProperty, tagIntProperty, tagFloatProperty, tagObjectProperty, tagNameProperty, tagStrProperty, tagArrayProperty, idxTextures;

  // property stream walker: remap name tags via map; remap Textures[] export refs
  static byte[] RemapStream(byte[] src, int off, int size, Func<int,int> mapName, int texRefA, int texRefB, int newA, int newB, string dbg) {
    var ms = new MemoryStream(); var w = new BinaryWriter(ms);
    // 4-byte object preamble (verbatim, value varies per object)
    w.Write(src, off, 4);
    int p = off+4, end = off+size;
    while (true) {
      if (p+8 > end) throw new Exception(string.Format("[{0}] stream overrun (tag) @0x{1:X}", dbg, p));
      int nIdx = R(src,p);
      if (nIdx == idxNone) { // 8-byte terminator: nameIdx + ext
        w.Write(mapName(nIdx)); w.Write(R(src,p+4));
        p += 8;
        break;
      }
      if (p+24 > end) throw new Exception(string.Format("[{0}] stream overrun (tag) @0x{1:X}", dbg, p));
      int nExt = R(src,p+4), tIdx = R(src,p+8), tExt = R(src,p+12), sz = R(src,p+16), arr = R(src,p+20);
      Console.WriteLine("[{0}] tag @0x{1:X}: name#{2}->{3} type#{4} size={5} arr={6}", dbg, p, nIdx, mapName(nIdx), tIdx, sz, arr);
      w.Write(mapName(nIdx)); w.Write(nExt); w.Write(mapName(tIdx)); w.Write(tExt); w.Write(sz); w.Write(arr);
      p += 24;
      if (tIdx == tagStructProperty) { w.Write(mapName(R(src,p))); w.Write(R(src,p+4)); w.Write(src,p+8,16); p += 24; }
      if (tIdx == tagByteProperty) { w.Write(mapName(R(src,p))); w.Write(R(src,p+4)); p += 8; }
      if (p > end) throw new Exception(string.Format("[{0}] stream overrun (tag ext) @0x{1:X}", dbg, p));
      if (tIdx == tagBoolProperty) { /* value in arr, no payload */ }
      else if (nIdx == idxTextures) {
        if (sz % 4 != 0) throw new Exception("Textures size %4 != 0");
        int cnt = R(src,p); w.Write(cnt);
        for (int k=0;k<cnt;k++) { int r = R(src,p+4+k*4);
          if (r==texRefA) r=newA; else if (r==texRefB) r=newB;
          w.Write(r); }
        if (4 + 4*cnt != sz) Console.WriteLine("[{0}] WARN: Textures consumed {1} != sz {2}", dbg, 4+4*cnt, sz);
        p += sz;
        continue;
      }
      else if (tIdx == tagNameProperty || (tIdx == tagByteProperty && sz == 8)) {
        // payload = FName(idx+num): remap the name index, keep number
        int fnIdx = R(src,p), fnNum = R(src,p+4);
        w.Write(mapName(fnIdx)); w.Write(fnNum);
        Console.WriteLine("[{0}] FName payload: {1}->{2} num={3}", dbg, fnIdx, mapName(fnIdx), fnNum);
      }
      else {
        w.Write(src,p,sz);
      }
      p += sz;
      if (p > end) throw new Exception(string.Format("[{0}] stream overrun (payload) @0x{1:X}", dbg, p));
    }
    if (p < end) { Console.WriteLine("[{0}] trailing {1} bytes @0x{2:X}", dbg, end-p, p); w.Write(src,p,end-p); }
    return ms.ToArray();
  }

  static void Main(string[] a) {
    var chn = File.ReadAllBytes(a[0]);
    var intl = File.ReadAllBytes(a[1]);
    int chnNameOff, chnExpOff, chnImpOff, chnDepOff, chnData;
    var cn = ReadNames(chn, out chnNameOff, out chnExpOff, out chnImpOff, out chnDepOff, out chnData);
    int intNameOff, intExpOff, intImpOff, intDepOff, intData;
    var inn = ReadNames(intl, out intNameOff, out intExpOff, out intImpOff, out intDepOff, out intData);
    Console.WriteLine("CHN: names={0} imps@0x{1:X} exps@0x{2:X} dep=0x{3:X} data=0x{4:X} len={5}", cn.Count, chnImpOff, chnExpOff, chnDepOff, chnData, chn.Length);
    Console.WriteLine("INT: names={0} imps@0x{1:X} exps@0x{2:X} dep=0x{3:X} data=0x{4:X} len={5}", inn.Count, intImpOff, intExpOff, intDepOff, intData, intl.Length);
    if (chnImpOff != chnNameOff + (0)) {} // names end == impOff verified below
    // sanity: names region ends exactly at import table
    // (computed during name read; assert via expOff == impOff + 6*28)
    if (chnExpOff != chnImpOff + 6*28) throw new Exception("CHN layout assumption broken");
    if (intExpOff != intImpOff + 6*28) throw new Exception("INT layout assumption broken");

    idxNone = inn.IndexOf("None"); tagStructProperty = inn.IndexOf("StructProperty"); tagByteProperty = inn.IndexOf("ByteProperty");
    tagBoolProperty = inn.IndexOf("BoolProperty"); idxTextures = inn.IndexOf("Textures");
    tagNameProperty = inn.IndexOf("NameProperty"); tagIntProperty = inn.IndexOf("IntProperty"); tagFloatProperty = inn.IndexOf("FloatProperty");
    tagObjectProperty = inn.IndexOf("ObjectProperty"); tagStrProperty = inn.IndexOf("StrProperty"); tagArrayProperty = inn.IndexOf("ArrayProperty");

    // ---- name merge ----
    var newNames = new List<string>();
    var mapIntName = new int[inn.Count];
    for (int i=0;i<inn.Count;i++) {
      int j = cn.IndexOf(inn[i]);
      if (j >= 0) mapIntName[i] = j;
      else { mapIntName[i] = cn.Count + newNames.Count; newNames.Add(inn[i]); }
    }
    Console.WriteLine("new names ({0}): {1}", newNames.Count, string.Join(" ", newNames));
    Func<int,int> mapName = delegate(int idx){ if (idx<0||idx>=inn.Count) throw new Exception("bad name idx "+idx); return mapIntName[idx]; };

    // raw bytes of new name entries (from INT table, keeps flags)
    var newNameBytes = new List<byte[]>();
    int qn = intNameOff;
    for (int i=0;i<inn.Count;i++) {
      int l = R(intl,qn); int entryLen = 4 + Math.Abs(l) + 8;
      if (mapIntName[i] >= cn.Count) {
        var eb = new byte[entryLen]; Buffer.BlockCopy(intl,qn,eb,0,entryLen);
        if (l < 0) throw new Exception("UTF16 name entry not handled: "+inn[i]);
        newNameBytes.Add(eb);
      }
      qn += entryLen;
    }
    int nameGrow = 0; foreach (var b in newNameBytes) nameGrow += b.Length;

    // ---- INT entries + data ----
    // true entry starts (chain-parsed): exp2, exp3, exp6(76B!), exp14, exp15
    int[] srcStart = {0x822, 0x866, 0x932, 0xB5A, 0xB9E};
    int[] srcLen   = {68, 68, 76, 68, 68};
    string[] what = {"SmallFont","TinyFont","PackageEngineFonts","Texture2D_28","Texture2D_29"};
    int[][] srcData = { new[]{0x487F,0x154C}, new[]{0x5DCB,0x154C}, new[]{0x735B,0xC}, new[]{0x7E3B,0x18C}, new[]{0x7FC7,0x18C} };
    var intEntries = new byte[5][];
    var newBlocks = new byte[5][];
    for (int i=0;i<5;i++) {
      intEntries[i] = new byte[srcLen[i]];
      Buffer.BlockCopy(intl, srcStart[i], intEntries[i], 0, srcLen[i]);
      if (i==2) { newBlocks[i] = new byte[srcData[i][1]]; Buffer.BlockCopy(intl, srcData[i][0], newBlocks[i], 0, srcData[i][1]); }
      else newBlocks[i] = RemapStream(intl, srcData[i][0], srcData[i][1], mapName, 15, 16, 93, 94, what[i]);
      Console.WriteLine("[{0}] entry copied, data {1} bytes (orig {2})", what[i], newBlocks[i].Length, srcData[i][1]);
    }

    // ---- layout ----
    int oldExpBytes = 89*68;
    int newExpBytes = 0; foreach (var e in intEntries) newExpBytes += e.Length;  // 68+68+76+68+68 = 348
    int dependsBytes = chnData - chnDepOff;
    int delta = nameGrow + newExpBytes;
    int newFirstData = chnData + delta;
    Console.WriteLine("nameGrow={0} delta={1} newDependsOff=0x{2:X} newFirstData=0x{3:X}",
      nameGrow, delta, chnDepOff+nameGrow, newFirstData);

    // data block offsets (after old data)
    int[] dataOff = new int[5];
    int cur = newFirstData + (int)(chn.Length - chnData);
    for (int i=0;i<5;i++) { dataOff[i] = cur; cur += newBlocks[i].Length; if (cur%4!=0){cur+=4-cur%4;} }

    // patch new export entries: outer refs in NEW 1-based numbering:
    //   new exports 0-based 89..93 -> 1-based 90..93... wait: 90=SmallFont, 91=TinyFont, 92=PackageEF, 93=Tex28, 94=Tex29
    int[] newOuter = {92, 92, 0, 90, 91};   // SmallFont->PackageEF, TinyFont->PackageEF, PackageEF->root, Tex28->SmallFont, Tex29->TinyFont
    for (int i=0;i<5;i++) {
      W(intEntries[i], 8, newOuter[i]);                  // PackageIndex (outer)
      W(intEntries[i], 12, mapName(R(intEntries[i],12))); // nameIdx remap (entry's own name; Tex: "Texture2D" + num kept)
      W(intEntries[i], 32, newBlocks[i].Length);         // SerialSize
      W(intEntries[i], 36, dataOff[i]);                  // SerialOffset
    }

    // ---- assemble ----
    var outb = new MemoryStream(); var ow = new BinaryWriter(outb);
    // bisect mode: SKIPADDEXP=1 → only name table + offset shifts (no new exports/data)
    bool skipAdd = Environment.GetEnvironmentVariable("SKIPADDEXP") == "1";
    int effExpBytes = skipAdd ? 0 : newExpBytes;
    int effDelta = nameGrow + effExpBytes;
    // summary (patch NameCount, HeaderSize@8, ExportCount, generation)
    var sum = new byte[chnNameOff]; Buffer.BlockCopy(chn,0,sum,0,chnNameOff);
    int flen = R(chn,12); int sp = 12+4+flen+4;
    W(sum, sp+0, cn.Count + newNames.Count);
    W(sum, 8, chnData + effDelta);
    W(sum, sp+8, skipAdd ? 89 : 94);
    W(sum, sp+20, chnImpOff + nameGrow);        // ImportOffset shifted
    W(sum, sp+12, chnExpOff + nameGrow);        // ExportOffset shifted
    W(sum, sp+24, chnDepOff + effDelta);        // DependsOffset shifted
    int genPos = -1;
    for (int probe = sp+28; probe < chnNameOff-12; probe += 4)
      if (R(chn,probe)==1 && R(chn,probe+4)==89 && R(chn,probe+8)==136) { genPos = probe; break; }
    if (genPos < 0) throw new Exception("generation table not found");
    W(sum, genPos+4, skipAdd ? 89 : 94); W(sum, genPos+8, cn.Count + newNames.Count);
    ow.Write(sum);
    // names
    ow.Write(chn, chnNameOff, chnImpOff - chnNameOff);
    foreach (var b in newNameBytes) ow.Write(b);
    // imports (identical tables verified)
    ow.Write(chn, chnImpOff, 6*28);
    // old export entries (patch SerialOffset += effDelta)
    var oldExp = new byte[oldExpBytes]; Buffer.BlockCopy(chn, chnExpOff, oldExp, 0, oldExpBytes);
    for (int i=0;i<89;i++) {
      int e = 68*i;
      W(oldExp, e+36, R(oldExp, e+36) + effDelta);
    }
    ow.Write(oldExp);
    // new export entries
    if (!skipAdd) for (int i=0;i<5;i++) ow.Write(intEntries[i]);
    // depends region verbatim
    ow.Write(chn, chnDepOff, dependsBytes);
    // old data verbatim
    ow.Write(chn, chnData, chn.Length - chnData);
    // new data blocks (4-aligned already: sizes 5452/5452/12/396/396 — pad to 4 anyway)
    if (!skipAdd) for (int i=0;i<5;i++) { ow.Write(newBlocks[i]); while (outb.Length % 4 != 0) ow.Write((byte)0); }

    File.WriteAllBytes(a[2], outb.ToArray());
    Console.WriteLine("written {0} ({1} bytes, orig {2}) skipAdd={3}", a[2], outb.Length, chn.Length, skipAdd);
    if (!skipAdd) Console.WriteLine("new export refs: SmallFont=#90 TinyFont=#91 PackageEF=#92 Tex28=#93 Tex29=#94 (1-based)");
  }
}
