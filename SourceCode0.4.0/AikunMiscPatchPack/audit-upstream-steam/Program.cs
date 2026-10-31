using Steamworks;
using System.Text.Json;
Environment.SetEnvironmentVariable("SteamAppId","2868840");
Environment.SetEnvironmentVariable("SteamGameId","2868840");
if(!SteamAPI.Init())throw new Exception("SteamAPI init failed");
try{
 var input=JsonDocument.Parse(File.ReadAllText(args[0]));var rows=new List<object>();
 foreach(var item in input.RootElement.EnumerateArray()){
  var id=new PublishedFileId_t(ulong.Parse(item.GetProperty("workshop_id").GetString()!));
  var state=(EItemState)SteamUGC.GetItemState(id);
  bool installed=SteamUGC.GetItemInstallInfo(id,out var size,out var folder,4096,out var timestamp);
  rows.Add(new{Id=item.GetProperty("id").GetString(),WorkshopId=id.m_PublishedFileId,State=state.ToString(),Installed=installed,Size=size,Timestamp=timestamp,Folder=folder,NeedsUpdate=state.HasFlag(EItemState.k_EItemStateNeedsUpdate)});
 }
 File.WriteAllText(args[1],JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
 Console.WriteLine("Steam read-only states="+rows.Count);
}finally{SteamAPI.Shutdown();}
