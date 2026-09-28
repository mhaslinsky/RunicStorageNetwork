using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RunicStorageNetwork {
 // Discovery sends bounded ID/revision pages, never a second writable copy of
 // a chest. The game's targeted ZDO replication carries the actual records.
 // Subscriptions exist only while a player is using the network.
 internal static class UnloadedMultiplayer {
  const int PageSize=64,MaxRecords=65536;
  const float Lifetime=6;
  sealed class Record {internal ZDOID Id;internal uint Revision;internal ushort OwnerRevision;}
  sealed class Watch {
   internal ZDOID Actor,Core;internal long Player;internal Vector3 Point;internal int Token,Generation;internal float Until,NextRequest;internal long Revision=-1;internal bool Recheck=true;
   internal readonly HashSet<ZDOID> Ids=new HashSet<ZDOID>(),Queued=new HashSet<ZDOID>();
   internal readonly Queue<ZDOID> Send=new Queue<ZDOID>();internal readonly Queue<ZPackage> Pages=new Queue<ZPackage>();
  }
  static readonly Dictionary<long,Watch> watchers=new Dictionary<long,Watch>();
  static readonly HashSet<ZDOID> accepted=new HashSet<ZDOID>();
  static readonly Dictionary<ZDOID,Record> waiting=new Dictionary<ZDOID,Record>();
  static readonly Queue<ZDOID> pending=new Queue<ZDOID>();
  static readonly Dictionary<int,Record[]> pages=new Dictionary<int,Record[]>();
  static bool serverEnabled=true,hasPoint;static Vector3 point;static ZDOID actor;static long player;
  static int token,generation,pageCount,totalRecords,serial,watchCursor;static float wantedUntil,nextRequest,nextMissing;
  internal static bool ServerEnabled=>serverEnabled;
  internal static bool Preparing=>!UnloadedNetworks.Authority&&waiting.Count>0;
  internal static bool Accepted(ZDOID id)=>accepted.Contains(id);
  internal static void Clear(){watchers.Clear();accepted.Clear();waiting.Clear();pending.Clear();pages.Clear();serverEnabled=true;hasPoint=false;token=generation=pageCount=totalRecords=serial=watchCursor=0;wantedUntil=nextRequest=nextMissing=0;}
  internal static void Register(Action<string,Action<long,ZPackage>> register){register("unloaded_query",Query);register("unloaded_catalog",Catalog);register("unloaded_missing",Missing);}
  internal static void Touch(Vector3 at,long actorId){
   if(!UnloadedNetworks.Requested||UnloadedNetworks.Authority||!Player.m_localPlayer||actorId!=Player.m_localPlayer.GetPlayerID())return;
   var id=Player.m_localPlayer.GetZDOID();
   if(!hasPoint||actor!=id){hasPoint=true;actor=id;player=actorId;token++;generation=0;pages.Clear();nextRequest=0;}point=at;
   Pulse();
  }
  internal static void Pulse(){
   if(!UnloadedNetworks.Requested||UnloadedNetworks.Authority||!hasPoint)return;
   wantedUntil=Time.unscaledTime+.5f;
   if(Time.unscaledTime<nextRequest||Transport.Server==0)return;nextRequest=Time.unscaledTime+1;
   var p=new ZPackage();p.Write(token);p.Write(actor);p.Write(player);p.Write(point);Transport.Send(Transport.Server,"unloaded_query",p);
  }
  static bool ActorAllowed(long peer,Watch w)=>RemoteContext.Actor(w.Actor,peer,w.Player,out var z,out _)&&(z.GetPosition()-w.Point).sqrMagnitude<=144;
  static void Query(long sender,ZPackage p){
   if(!UnloadedNetworks.Authority||sender==ZNet.GetUID())return;
   int requested=p.ReadInt();var id=p.ReadZDOID();long who=p.ReadLong();var at=p.ReadVector3();
   if(requested<1||!Finite(at))return;
   var candidate=new Watch{Actor=id,Player=who,Point=at,Token=requested};if(!ActorAllowed(sender,candidate))return;
   if(!UnloadedNetworks.Enabled){var off=new ZPackage();off.Write(requested);off.Write(false);Transport.Send(sender,"unloaded_catalog",off);return;}
   if(!watchers.TryGetValue(sender,out var w)){
    if(watchers.Count>=128)return;watchers[sender]=w=candidate;
   }else{
    if(Time.unscaledTime<w.NextRequest||(w.Actor==id&&requested<w.Token))return;
    if(w.Token!=requested||w.Actor!=id||w.Player!=who){w.Revision=-1;w.Pages.Clear();}
    w.Actor=id;w.Player=who;w.Point=at;w.Token=requested;w.Recheck=true;
   }
   w.NextRequest=Time.unscaledTime+.5f;w.Until=Time.unscaledTime+Lifetime;
   UnloadedNetworks.Wake();
  }
  static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);
  static void Enqueue(Watch w,ZDOID id){if(w.Queued.Add(id))w.Send.Enqueue(id);}
  internal static void Changed(ZDO z){
   if(!UnloadedNetworks.Authority)return;
   foreach(var w in watchers.Values)if(Time.unscaledTime<w.Until&&w.Ids.Contains(z.m_uid))Enqueue(w,z.m_uid);
  }
  internal static void Received(ZDO z){
   if(UnloadedNetworks.Authority||!accepted.Contains(z.m_uid))return;
   UnloadedNetworks.Import(z.m_uid);TryReady(z.m_uid);
  }
  static void Snapshot(Watch w,Core core){
   var ids=core?UnloadedNetworks.Export(core,w.Player):new List<ZDOID>();
   if(ids.Count>MaxRecords)throw new InvalidOperationException("Experimental network exceeds discovery protocol capacity");
   w.Revision=UnloadedNetworks.CatalogRevision;w.Generation=++serial;w.Pages.Clear();w.Ids.Clear();w.Send.Clear();w.Queued.Clear();
   var records=new List<Record>();
   foreach(var id in ids){var z=RemoteContext.Data(id);if(z==null)continue;w.Ids.Add(id);Enqueue(w,id);records.Add(new Record{Id=id,Revision=z.DataRevision,OwnerRevision=z.OwnerRevision});}
   int count=Math.Max(1,(records.Count+PageSize-1)/PageSize);
   for(int page=0;page<count;page++){
    var packet=new ZPackage();packet.Write(w.Token);packet.Write(true);packet.Write(w.Generation);packet.Write(page);packet.Write(count);packet.Write(records.Count);
    var slice=records.Skip(page*PageSize).Take(PageSize).ToArray();packet.Write(slice.Length);
    foreach(var r in slice){packet.Write(r.Id);packet.Write((long)r.Revision);packet.Write((int)r.OwnerRevision);}w.Pages.Enqueue(packet);
   }
  }
  static void Catalog(long sender,ZPackage p){
   if(!UnloadedNetworks.Requested||UnloadedNetworks.Authority||sender!=Transport.Server||p.ReadInt()!=token)return;
   bool enabled=p.ReadBool();if(!enabled){if(serverEnabled){Replace(Array.Empty<Record>());StorageIndex.Offline?.Suspend();Plugin.Info("Experimental unloaded networks are disabled on the server; using loaded-area networking.");}serverEnabled=false;pages.Clear();generation=0;return;}serverEnabled=true;
   int current=p.ReadInt(),index=p.ReadInt(),count=p.ReadInt(),total=p.ReadInt(),length=p.ReadInt();
   if(current<1||current<generation||count<1||count>(MaxRecords+PageSize-1)/PageSize||index<0||index>=count||total<0||total>MaxRecords||count!=Math.Max(1,(total+PageSize-1)/PageSize)||length!=Math.Min(PageSize,total-index*PageSize))return;
   var records=new Record[length];
   for(int i=0;i<length;i++){var id=p.ReadZDOID();long revision=p.ReadLong();int owner=p.ReadInt();if(id==ZDOID.None||revision<0||revision>uint.MaxValue||owner<0||owner>ushort.MaxValue)return;records[i]=new Record{Id=id,Revision=(uint)revision,OwnerRevision=(ushort)owner};}
   if(current>generation){generation=current;pageCount=count;totalRecords=total;pages.Clear();}
   if(count!=pageCount||total!=totalRecords||pages.ContainsKey(index))return;pages[index]=records;
   if(pages.Count!=pageCount)return;
   var all=pages.OrderBy(e=>e.Key).SelectMany(e=>e.Value).ToArray();if(all.Select(r=>r.Id).Distinct().Count()!=totalRecords){pages.Clear();return;}
   Replace(all);
  }
  static void Replace(IEnumerable<Record> records){
   var all=records.ToArray();var next=new HashSet<ZDOID>(all.Select(r=>r.Id));
   foreach(var id in accepted.Where(id=>!next.Contains(id)).ToArray())UnloadedNetworks.Forget(id);
   accepted.Clear();waiting.Clear();pending.Clear();foreach(var r in all){accepted.Add(r.Id);waiting[r.Id]=r;pending.Enqueue(r.Id);}
   UnloadedNetworks.Wake();Topology.Dirty();
  }
  static bool TryReady(ZDOID id){
   if(!waiting.TryGetValue(id,out var r))return true;var z=RemoteContext.Data(id);
   if(z==null||z.DataRevision<r.Revision||z.OwnerRevision<r.OwnerRevision)return false;
   waiting.Remove(id);UnloadedNetworks.Import(id);if(waiting.Count==0)Topology.Dirty();return true;
  }
  static void Missing(long sender,ZPackage p){
   if(!UnloadedNetworks.Enabled||!UnloadedNetworks.Authority||!watchers.TryGetValue(sender,out var w)||Time.unscaledTime>=w.Until)return;
   int request=p.ReadInt(),version=p.ReadInt(),count=p.ReadInt();if(request!=w.Token||version!=w.Generation||count<1||count>PageSize||!ActorAllowed(sender,w))return;
   for(int i=0;i<count;i++){var id=p.ReadZDOID();if(w.Ids.Contains(id))Enqueue(w,id);}
  }
  internal static void Tick(){
   if(!UnloadedNetworks.Requested)return;
   if(UnloadedNetworks.Authority){
    int sendBudget=64,pageBudget=4;
    var peers=watchers.ToArray();int start=peers.Length==0?0:watchCursor++%peers.Length;if(watchCursor==int.MaxValue)watchCursor=0;
    for(int i=0;i<peers.Length;i++){
     var pair=peers[(start+i)%peers.Length];
     var w=pair.Value;if(Time.unscaledTime>=w.Until||!ActorAllowed(pair.Key,w)){watchers.Remove(pair.Key);continue;}
     UnloadedNetworks.Wake();if(!UnloadedNetworks.Prepare())continue;Topology.Refresh();
     if(w.Recheck||w.Revision!=UnloadedNetworks.CatalogRevision){
      var core=Topology.Choose(w.Point,w.Player);var id=core?R.View(core).GetZDO().m_uid:ZDOID.None;
      if(w.Core!=id||w.Revision!=UnloadedNetworks.CatalogRevision)Snapshot(w,core);
      w.Core=id;w.Recheck=false;
     }
     if(w.Pages.Count>0&&pageBudget>0){pageBudget--;Transport.Send(pair.Key,"unloaded_catalog",w.Pages.Dequeue());}
     while(w.Send.Count>0&&sendBudget>0){var id=w.Send.Dequeue();w.Queued.Remove(id);sendBudget--;if(w.Ids.Contains(id)&&RemoteContext.Data(id)!=null)ZDOMan.instance.ForceSendZDO(pair.Key,id);}
    }
   }else if(Time.unscaledTime<wantedUntil){
    UnloadedNetworks.Wake();int budget=Math.Min(32,pending.Count);
    while(budget-->0){var id=pending.Dequeue();if(!TryReady(id))pending.Enqueue(id);}
    if(waiting.Count>0&&Time.unscaledTime>=nextMissing&&Transport.Server!=0){nextMissing=Time.unscaledTime+2;var packet=new ZPackage();packet.Write(token);packet.Write(generation);var ids=pending.Take(PageSize).ToArray();packet.Write(ids.Length);foreach(var id in ids)packet.Write(id);Transport.Send(Transport.Server,"unloaded_missing",packet);}
   }
  }
 }
}
