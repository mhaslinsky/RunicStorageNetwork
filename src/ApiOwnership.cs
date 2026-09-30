using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using HarmonyLib;
using UnityEngine;
using RunicStorageNetwork.API;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 internal sealed class ApiSource {
  internal string Token;internal ZDO Data;internal Container Container;internal InventoryDelta Delta;
  internal long PreviousOwner,Creator;internal ushort OwnerRevision;internal uint DataRevision;internal bool Ready,Created,Denied,Released,Applied;
  internal byte[] Before,Buffer,Expected;internal int Received,ReservedBytes;internal double LastSend;internal List<Stock> Stock;
 }
 internal static class ApiOwnership {
  const string Fence="rsn_api_fence";
  static readonly Dictionary<ZDOID,ApiSource> sources=new Dictionary<ZDOID,ApiSource>();
  static readonly Dictionary<ZDOID,ApiSource> offers=new Dictionary<ZDOID,ApiSource>();
  static readonly HashSet<Inventory> blocked=new HashSet<Inventory>();
  static ZDOID ownerTransition;static bool transition;static long bytes;
  internal static int Count=>sources.Count;
  internal static bool Locked(Inventory inv)=>blocked.Contains(inv);
  internal static bool LockedItem(ItemDrop.ItemData item)=>blocked.Any(inv=>inv.ContainsItem(item));
  internal static bool? OwnerRule(ZDO z,long uid){
   if(transition&&ownerTransition==z.m_uid&&uid==ZNet.GetUID())return true;
   if(sources.TryGetValue(z.m_uid,out var source)&&source.Ready)return uid==z.GetOwner();
   return null;
  }
  internal static void Install(Harmony h){
   var method=AccessTools.Method(typeof(ZDOMan),"RPC_ZDOData",new[]{typeof(ZRpc),typeof(ZPackage)})??throw new MissingMethodException("ZDOMan.RPC_ZDOData");
   h.Patch(method,prefix:new HarmonyMethod(typeof(ApiOwnership),nameof(FilterReplication)){priority=Priority.First});
   h.Patch(AccessTools.Method(typeof(ZNetScene),"RemoveObjects",new[]{typeof(List<ZDO>),typeof(List<ZDO>)}),prefix:new HarmonyMethod(typeof(ApiOwnership),nameof(Pin)));
   Transport.ExtraInventoryLock=Locked;Transport.ExtraItemLock=LockedItem;Transport.ExtraOwnerRule=OwnerRule;
  }
  // Parse the exact installed game frame. Preserve vanilla processing for every other object.
  static bool FilterReplication(ZDOMan __instance,ZRpc __0,ref ZPackage __1){
   if(!ZNet.instance||!ZNet.instance.IsServer())return true;
   var peer=R.Call(__instance,"FindPeer",new[]{typeof(ZRpc)},__0);if(peer==null)return true;
   long sender=R.Get<ZNetPeer>(peer,"m_peer").m_uid;
   byte[] original=null;var read=__1;int initial=read.GetPos();read.SetPos(0);MemoryStream output=null;
   try{
    int invalid=read.ReadInt();if(invalid<0||invalid>65536)return false;
    for(int i=0;i<invalid;i++)read.ReadZDOID();
    for(int records=0;records<65536;records++){
     int start=read.GetPos();var id=read.ReadZDOID();
     if(id==ZDOID.None){if(output!=null){output.Write(original,start,original.Length-start);__1=new ZPackage(output.ToArray());}return true;}
     ushort ownerRev=read.ReadUShort();read.ReadUInt();read.ReadLong();read.ReadVector3();
     int length=read.ReadInt(),position=read.GetPos();if(length<0||length>read.Size()-position)return false;read.SetPos(position+length);
     var z=__instance.GetZDO(id);bool held=sources.TryGetValue(id,out var source)&&source.Ready;
     bool reject=z!=null&&(z.GetInt(Fence,0)==1||held)&&!ApiRules.AcceptSourceFrame(held,z.GetOwner(),z.OwnerRevision,sender,ownerRev);
     if(reject){if(output==null){original=read.GetArray();output=new MemoryStream(original.Length);output.Write(original,0,start);}}
     else if(output!=null)output.Write(original,start,read.GetPos()-start);
    }return false;
   }catch(Exception e){ApiRuntime.Diagnostic("replication",e.Message);return false;}finally{read.SetPos(initial);output?.Dispose();}
  }
  static void Pin(List<ZDO> currentNearObjects){
   foreach(var s in sources.Values.Concat(offers.Values))if(s.Container&&!s.Container.GetComponent<UnloadedReplica>()&&R.Valid(R.View(s.Container))&&!currentNearObjects.Contains(s.Data))currentNearObjects.Add(s.Data);
  }
  static void Block(ApiSource s,bool value){
   if(!s.Container)return;var inv=s.Container.GetInventory();if(value)blocked.Add(inv);else blocked.Remove(inv);
   Integrations.Block(inv,value);R.Set(s.Container,"m_inUse",value);
  }
  internal static ApiSource Prepare(ZDO z,string token,long creator,double now){
   if(sources.TryGetValue(z.m_uid,out var previous))return previous.Token==token?previous:null;
   if(sources.Count>=256||z.GetString("rsn_lease","")!=""||z.OwnerRevision==ushort.MaxValue||z.DataRevision>=uint.MaxValue-1024)return null;
   var source=new ApiSource{Data=z,Token=token,Creator=creator,PreviousOwner=z.GetOwner(),OwnerRevision=z.OwnerRevision,LastSend=now};
   sources[z.m_uid]=source;
   if(z.GetOwner()==ZNet.GetUID()||z.GetOwner()==0||ZNet.instance.GetPeer(z.GetOwner())==null){
    try{
     var live=ZNetScene.instance.FindInstance(z.m_uid)?.GetComponent<Container>();
     if(live&&R.View(live).IsOwner()&&!live.IsInUse()&&!Transport.Locked(live.GetInventory())&&!Integrations.IsBusy(live.GetInventory()))R.Call(live,"Save");
     Claim(source,z.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>(),z.DataRevision);
    }catch{source.Denied=true;}
   }else if(ApiRuntime.HasPeer(z.GetOwner()))SendPrepare(source);else source.Denied=true;
   return source;
  }
  static bool ReserveBytes(ApiSource s,int size){
   int needed=checked(size*4+4096);if(size<0||size>ApiRules.MaxInventoryBytes||bytes+needed>64L*1024*1024)return false;
   bytes+=needed;s.ReservedBytes+=needed;return true;
  }
  static void Claim(ApiSource s,byte[] content,uint revision){
   var z=s.Data;if(z==null||!z.IsValid()||z.GetOwner()!=s.PreviousOwner||z.OwnerRevision!=s.OwnerRevision||!ApiWorld.Eligible(z,s.Creator))throw new InvalidOperationException("Source changed before handoff");
   if(s.ReservedBytes==0&&!ReserveBytes(s,content.Length))throw new InvalidOperationException("API recovery budget exhausted");
   ApiWorld.Decode(z,content); // Compatibility and lossless load before ownership changes.
   var live=ZNetScene.instance.FindInstance(z.m_uid)?.GetComponent<Container>();
   if(live&&R.View(live).IsOwner()&&(live.IsInUse()||Integrations.IsBusy(live.GetInventory())||Transport.Locked(live.GetInventory())))throw new InvalidOperationException("Source busy");
   ownerTransition=z.m_uid;transition=true;
   try{z.SetOwner(ZNet.GetUID());}finally{transition=false;}
   if(z.GetOwner()!=ZNet.GetUID())throw new InvalidOperationException("Ownership handoff refused");
   // Fence BEFORE publishing a higher content revision. Delayed vanilla frames are filtered.
   s.Ready=true;z.Set(Fence,1);z.DataRevision=Math.Max(z.DataRevision,revision);z.Set(ZDOVars.s_items,content);
   z.Set("rsn_lease",s.Token);z.Set(ZDOVars.s_inUse,1);s.Before=(byte[])content.Clone();
   s.Container=UnloadedNetworks.ApiContainer(z,out s.Created);if(!s.Container)throw new InvalidOperationException("Source adapter unavailable");
   Transport.InternalMutation++;
   try{R.Set(s.Container,"m_inUse",false);R.Set(s.Container,"m_lastRevision",uint.MaxValue);R.Call(s.Container,"Load");}
   finally{Transport.InternalMutation--;}
   s.Stock=Stockroom.Snapshot(s.Container.GetInventory(),R.Key(z.m_uid),null,true);Block(s,true);
   ZDOMan.instance.ForceSendZDO(z.m_uid);ApiWorld.Invalidate(z.m_uid);
  }
  static void SendPrepare(ApiSource s){
   var p=new ZPackage();p.Write(s.Token);p.Write(s.Data.m_uid);p.Write((int)s.OwnerRevision);p.Write(s.Creator);ApiRuntime.Send(s.PreviousOwner,"prepare",p);
  }
  internal static void ReceivePrepare(long sender,ZPackage p){
   if(sender!=Transport.Server)return;string token=ApiWire.Text(p,512);var id=p.ReadZDOID();int epoch=p.ReadInt();long creator=p.ReadLong();
   if(!ApiRuntime.Enabled){Deny(sender,token,id);return;}
   if(offers.TryGetValue(id,out var prior)){if(prior.Token==token)SendOffer(prior);else Deny(sender,token,id);return;}
   var z=RemoteContext.Data(id);var c=z!=null?ZNetScene.instance.FindInstance(id)?.GetComponent<Container>():null;
   if(offers.Count>=256||!c||!R.Valid(R.View(c))||!R.View(c).IsOwner()||z.OwnerRevision!=epoch||!ApiWorld.Eligible(z,creator)||!(bool)R.Call(c,"CheckAccess",new[]{typeof(long)},creator)||Transport.Reserved(z)||c.IsInUse()||Integrations.IsBusy(c.GetInventory())||Transport.Locked(c.GetInventory())){Deny(sender,token,id);return;}
   var s=new ApiSource{Token=token,Data=z,Container=c,Creator=creator,OwnerRevision=z.OwnerRevision,PreviousOwner=z.GetOwner()};
   try{
    R.Call(c,"Load");R.Call(c,"Save");var content=z.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>();
    if(!ReserveBytes(s,content.Length))throw new InvalidOperationException("Offer budget");
    s.Before=(byte[])content.Clone();s.DataRevision=z.DataRevision;Block(s,true);z.Set("rsn_lease",token);z.Set(ZDOVars.s_inUse,1);offers[id]=s;SendOffer(s);
   }catch{Block(s,false);if(z.GetString("rsn_lease","")==token){z.Set("rsn_lease","");z.Set(ZDOVars.s_inUse,0);}offers.Remove(id);bytes-=s.ReservedBytes;Deny(sender,token,id);}
  }
  static void Deny(long peer,string token,ZDOID id){var p=new ZPackage();p.Write(token);p.Write(id);p.Write(false);ApiRuntime.Send(peer,"grant",p);}
  static void SendOffer(ApiSource s){
   int size=s.Before.Length;for(int offset=0;offset<size||offset==0;offset+=28000){
    int count=Math.Min(28000,size-offset);var data=new byte[count];Array.Copy(s.Before,offset,data,0,count);
    var p=new ZPackage();p.Write(s.Token);p.Write(s.Data.m_uid);p.Write(true);p.Write((int)s.OwnerRevision);p.Write(s.DataRevision);p.Write(size);p.Write(offset);p.Write(data);ApiRuntime.Send(Transport.Server,"grant",p);
   }
  }
  internal static void ReceiveGrant(long sender,ZPackage p){
   if(!ZNet.instance.IsServer())return;string token=ApiWire.Text(p,512);var id=p.ReadZDOID();bool allowed=p.ReadBool();
   if(!sources.TryGetValue(id,out var s)||s.Token!=token||s.PreviousOwner!=sender||s.Ready)return;
   if(!allowed){s.Denied=true;return;}
   int ownerRev=p.ReadInt();uint rev=p.ReadUInt();int total=p.ReadInt(),offset=p.ReadInt();byte[] part=p.ReadByteArray();
   if(ownerRev!=s.OwnerRevision||total<0||total>ApiRules.MaxInventoryBytes||offset<0||part.Length>28000||offset+part.Length>total){s.Denied=true;return;}
   if(s.Buffer==null){if(offset!=0||!ReserveBytes(s,total)){s.Denied=true;return;}s.Buffer=new byte[total];s.DataRevision=rev;}
   if(s.Buffer.Length!=total||s.DataRevision!=rev){s.Denied=true;return;}
   if(offset<s.Received)return;if(offset!=s.Received)return;Array.Copy(part,0,s.Buffer,offset,part.Length);s.Received+=part.Length;
   if(s.Received==total)try{Claim(s,s.Buffer,rev);}catch(Exception e){s.Denied=true;ApiRuntime.Diagnostic("handoff",e.Message);}
  }
  internal static void Tick(double now){
   foreach(var s in sources.Values.ToArray())if(!s.Ready&&!s.Denied&&now-s.LastSend>1){s.LastSend=now;SendPrepare(s);}
   // Never time out and blindly unlock an offered inventory. Query server's live ticket.
   foreach(var s in offers.Values.ToArray())if(now-s.LastSend>2){s.LastSend=now;var p=new ZPackage();p.Write(s.Token);p.Write(s.Data.m_uid);ApiRuntime.Send(Transport.Server,"ticket",p);}
  }
  internal static void ReceiveTicket(long sender,ZPackage p){
   if(!ZNet.instance.IsServer())return;string token=ApiWire.Text(p,512);var id=p.ReadZDOID();
   if(sources.TryGetValue(id,out var s)&&s.Token==token&&!s.Ready)return;
   var reply=new ZPackage();reply.Write(token);reply.Write(id);ApiRuntime.Send(sender,"release",reply);
  }
  internal static void ReceiveRelease(long sender,ZPackage p){
   if(sender!=Transport.Server)return;string token=ApiWire.Text(p,512);var id=p.ReadZDOID();
   if(!offers.TryGetValue(id,out var s)||s.Token!=token)return;
   Block(s,false);
   if(s.Data!=null&&s.Data.IsValid()&&s.Data.GetOwner()==ZNet.GetUID()&&s.Data.GetString("rsn_lease","")==token){s.Data.Set("rsn_lease","");s.Data.Set(ZDOVars.s_inUse,0);}
   bytes-=s.ReservedBytes;offers.Remove(id);
  }
  internal static bool Release(ApiSource s){
   if(s.Released)return true;
   if(s.Ready){
    if(s.Data==null||!s.Data.IsValid()||s.Data.GetOwner()!=ZNet.GetUID())return false;
    string lease=s.Data.GetString("rsn_lease","");if(lease!=""&&lease!=s.Token)return false;
    if(s.Container){Block(s,false);s.Data.Set("rsn_lease","");s.Data.Set(ZDOVars.s_inUse,0);}
    else {s.Data.Set("rsn_lease","");s.Data.Set(ZDOVars.s_inUse,0);}
    ZDOMan.instance.ForceSendZDO(s.Data.m_uid);ApiWorld.Invalidate(s.Data.m_uid);StorageIndex.Changed(R.Key(s.Data.m_uid));
   }
   sources.Remove(s.Data.m_uid);bytes-=s.ReservedBytes;s.Released=true;
   Transport.Instance.SharedSourceGate.Released(s.Token,R.Key(s.Data.m_uid));
   if(s.PreviousOwner!=0&&s.PreviousOwner!=ZNet.GetUID()){var p=new ZPackage();p.Write(s.Token);p.Write(s.Data.m_uid);ApiRuntime.Send(s.PreviousOwner,"release",p);}
   if(s.Created)UnloadedNetworks.ApiReleaseAdapter(s.Data.m_uid);
   s.Before=null;s.Buffer=null;s.Expected=null;s.Delta=null;s.Stock=null;s.Container=null;return true;
  }
  internal static void Clear(){foreach(var s in offers.Values.Concat(sources.Values))if(s.Container){blocked.Remove(s.Container.GetInventory());Integrations.Block(s.Container.GetInventory(),false);}sources.Clear();offers.Clear();blocked.Clear();bytes=0;}
 }
}
