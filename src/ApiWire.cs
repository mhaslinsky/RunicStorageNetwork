using System;
using System.Collections.Generic;
using RunicStorageNetwork.API;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 internal static class ApiWire {
  internal static string Text(ZPackage p,int limit=128){string s=p.ReadString();if(!ApiRules.Text(s,limit))throw new InvalidOperationException("Invalid API text");return s;}
  internal static ApiContext Context(ZPackage p)=>new ApiContext(p.ReadZDOID(),Text(p));
  internal static void Context(ZPackage p,ApiContext c){p.Write(c.ConsumerId);p.Write(c.ModId);}
  internal static ResourceKey Key(ZPackage p)=>new ResourceKey(Text(p),p.ReadInt());
  internal static void Key(ZPackage p,ResourceKey k){p.Write(k.PrefabName);p.Write(k.Quality);}
  internal static ConsumeRequest Request(ZPackage p){
   string session=Text(p,64);long seq=p.ReadLong();string cycle=Text(p);var policy=(ResourceMatchPolicy)p.ReadInt();double seconds=p.ReadDouble();int n=p.ReadInt();
   if(n<1||n>32)throw new InvalidOperationException("API requirement limit");var needs=new List<ResourceRequirement>();for(int i=0;i<n;i++)needs.Add(new ResourceRequirement(Key(p),p.ReadInt()));
   var request=new ConsumeRequest(session,seq,cycle,needs,policy,seconds);if(!ApiRules.Normalize(request,out var result))throw new InvalidOperationException("Invalid API request");return result;
  }
  internal static void Request(ZPackage p,ConsumeRequest r){p.Write(r.SessionId);p.Write(r.Sequence);p.Write(r.CycleRevision);p.Write((int)r.MatchPolicy);p.Write(r.StartWithin);p.Write(r.Resources.Count);foreach(var row in r.Resources){Key(p,row.Resource);p.Write(row.Amount);}}
  internal static void Result(ZPackage p,ConsumeResult r){
   p.Write(r!=null);if(r==null)return;p.Write(r.RequestId??"");p.Write(r.NetworkId??"");p.Write(r.Outcome.HasValue?(int)r.Outcome.Value:-1);p.Write((int)r.Reason);p.Write(r.Resources.Count);foreach(var row in r.Resources){Key(p,row.Resource);p.Write(row.Amount);}
  }
  internal static ConsumeResult Result(ZPackage p){
   if(!p.ReadBool())return null;string id=p.ReadString(),network=p.ReadString();if(id.Length>512||network.Length>256)throw new InvalidOperationException("API receipt limit");int outcome=p.ReadInt();var status=(ApiStatus)p.ReadInt();int count=p.ReadInt();
   if(outcome< -1||outcome>2||count<0||count>32)throw new InvalidOperationException("Invalid API receipt");var rows=new List<ResourceRequirement>();for(int i=0;i<count;i++)rows.Add(new ResourceRequirement(Key(p),p.ReadInt()));return new ConsumeResult(id,network,outcome<0?(ConsumptionOutcome?)null:(ConsumptionOutcome)outcome,status,rows);
  }
  internal static void Recovery(ZPackage p,ConsumerRecovery r){p.Write(r!=null);if(r==null)return;p.Write(r.OwnerEpoch);p.Write(r.Request!=null);if(r.Request!=null)Request(p,r.Request);Result(p,r.Result);p.Write((int)r.State);}
  internal static ConsumerRecovery Recovery(ZPackage p){if(!p.ReadBool())return null;long epoch=p.ReadLong();var request=p.ReadBool()?Request(p):null;var result=Result(p);return new ConsumerRecovery(epoch,request,result,(ConsumptionState)p.ReadInt());}
 }
}
