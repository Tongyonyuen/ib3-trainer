// decsave <in.bin> <out.bin> — 云存档 "yeK " + AES-256-ECB → 明文
using System; using System.IO; using System.Security.Cryptography;
class DS {
  static void Main(string[] a) {
    byte[] key = new byte[32];
    string ks = "366e486d6a643a6862574e663d397c554f323a3f3b4b30792b675a4c2d6a5035";
    for (int i=0;i<32;i++) key[i]=Convert.ToByte(ks.Substring(i*2,2),16);
    byte[] d = File.ReadAllBytes(a[0]);
    if (d[0]!=(byte)'y'||d[1]!=(byte)'e'||d[2]!=(byte)'K'||d[3]!=(byte)' ') { Console.WriteLine("no magic"); return; }
    var aes = Aes.Create(); aes.Mode=CipherMode.ECB; aes.Padding=PaddingMode.None; aes.Key=key;
    byte[] body = new byte[d.Length-4]; Array.Copy(d,4,body,0,body.Length);
    byte[] plain = aes.CreateDecryptor().TransformFinalBlock(body,0,body.Length);
    File.WriteAllBytes(a[1], plain);
    Console.WriteLine("decrypted " + a[1] + " len=" + plain.Length);
  }
}
