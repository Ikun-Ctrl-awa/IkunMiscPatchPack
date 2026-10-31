using System.IO.Compression;
using System.Security.Cryptography;
namespace AikunMiscPatchPack;

public sealed record PatchVariant(int Mask,string Resource,int Length,string Hash);
public sealed record PatchTarget(string Name,string OriginalHash,string[] Options,PatchVariant[] Variants,string WorkshopRelative);
public static class PatchData {
 public static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
 public static PatchTarget? SelectSupported(IEnumerable<PatchTarget> targets,string name,string originalHash)=>targets.SingleOrDefault(t=>t.Name==name&&t.OriginalHash==originalHash);
 public static byte[] Apply(byte[] original,Stream compressed,PatchTarget target,PatchVariant variant){
  if(Hash(original)!=target.OriginalHash)throw new InvalidDataException("目标模组版本不匹配");
  if(variant.Length<=0||variant.Length>64*1024*1024)throw new InvalidDataException("补丁长度无效");
  using var brotli=new BrotliStream(compressed,CompressionMode.Decompress);int size=Math.Max(original.Length,variant.Length);
  var delta=new byte[size];brotli.ReadExactly(delta);if(brotli.ReadByte()!=-1)throw new InvalidDataException("补丁数据过长");
  var result=new byte[variant.Length];for(int i=0;i<result.Length;i++)result[i]=(byte)(delta[i]^(i<original.Length?original[i]:0));
  if(Hash(result)!=variant.Hash)throw new InvalidDataException("补丁结果校验不通过");return result;
 }
 public static int Mask(PatchTarget target,Func<string,bool> enabled){int mask=0;for(int i=0;i<target.Options.Length;i++)if(enabled(target.Options[i]))mask|=1<<i;return mask;}
}
