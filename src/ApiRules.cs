using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RunicStorageNetwork.API;

namespace RunicStorageNetwork.Logic {
 internal static class ApiRules {
  internal const int Consumers=4096,Operations=64,PerPeer=16,PerNetwork=8,SourceLimit=32,MaxInventoryBytes=1024*1024,MaxPacketBytes=32768;
  internal static bool Text(string s,int max=128)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=max&&!s.Any(char.IsControl)&&System.Text.Encoding.UTF8.GetByteCount(s)<=max;
  internal static bool Normalize(ConsumeRequest request,out ConsumeRequest normalized){
   normalized=null;
   if(request==null||!Text(request.SessionId,64)||request.Sequence<=0||request.Sequence==long.MaxValue||!Text(request.CycleRevision)||request.MatchPolicy!=ResourceMatchPolicy.AnyMatchingInstance||double.IsNaN(request.StartWithin)||request.StartWithin<=0||request.StartWithin>10||request.Resources.Count==0||request.Resources.Count>32)return false;
   var items=new Dictionary<ResourceKey,int>();
   foreach(var row in request.Resources){
    if(row==null||!Text(row.Resource.PrefabName)||row.Resource.Quality<1||row.Resource.Quality>10000||row.Amount<=0||row.Amount>100000)return false;
    items.TryGetValue(row.Resource,out int amount);if((long)amount+row.Amount>100000)return false;items[row.Resource]=amount+row.Amount;
   }
   normalized=new ConsumeRequest(request.SessionId,request.Sequence,request.CycleRevision,items.OrderBy(x=>x.Key.PrefabName,StringComparer.Ordinal).ThenBy(x=>x.Key.Quality).Select(x=>new ResourceRequirement(x.Key,x.Value)),request.MatchPolicy,request.StartWithin);return true;
  }
  internal static bool Same(ConsumeRequest a,ConsumeRequest b)=>a.SessionId==b.SessionId&&a.Sequence==b.Sequence&&a.CycleRevision==b.CycleRevision&&a.MatchPolicy==b.MatchPolicy&&a.StartWithin==b.StartWithin&&a.Resources.Count==b.Resources.Count&&a.Resources.Zip(b.Resources,(x,y)=>x.Resource.Equals(y.Resource)&&x.Amount==y.Amount).All(x=>x);
  internal static string RequestId(string consumer,ConsumeRequest request)=>request.SessionId+"/"+consumer+"/"+request.Sequence.ToString(CultureInfo.InvariantCulture);
  // A higher data revision alone never authorizes an older owner to overwrite a fenced source.
  internal static bool AcceptSourceFrame(bool held,long currentOwner,ushort currentRevision,long sender,ushort incomingRevision)=>!held&&incomingRevision>=currentRevision&&sender==currentOwner;
 }
 internal sealed class ApiBucket {
  readonly double rate,capacity;double tokens,last;
  internal ApiBucket(double rate,double capacity,double now){this.rate=rate;this.capacity=capacity;tokens=capacity;last=now;}
  internal bool Take(double now){tokens=Math.Min(capacity,tokens+Math.Max(0,now-last)*rate);last=now;if(tokens<1)return false;tokens--;return true;}
 }
 internal sealed class ApiLedger {
  internal sealed class Entry {
   internal string Consumer,Id,Network="";internal ConsumeRequest Request;internal ConsumeResult Result;internal ConsumptionState State;
   internal long Peer,Order;internal double Started;internal bool Released;internal object Work;
  }
  readonly Dictionary<string,Entry> records=new Dictionary<string,Entry>(StringComparer.Ordinal);
  long order;
  internal IEnumerable<Entry> Entries=>records.Values;
  internal int Count=>records.Count;
  internal Entry Find(string consumer)=>records.TryGetValue(consumer,out var entry)?entry:null;
  internal ApiStatus Lookup(string consumer,ConsumeRequest request,out Entry existing){
   existing=Find(consumer);if(existing==null)return ApiStatus.NotReady;
   if(request.Sequence<existing.Request.Sequence)return ApiStatus.ExpiredRequest;
   if(request.Sequence==existing.Request.Sequence)return ApiRules.Same(request,existing.Request)?ApiStatus.Ready:ApiStatus.RequestConflict;
   return existing.Result==null||!existing.Released?ApiStatus.Busy:ApiStatus.NotReady;
  }
  internal ApiStatus Admit(string consumer,long peer,ConsumeRequest request,double now,out Entry entry){
   var found=Lookup(consumer,request,out entry);if(found!=ApiStatus.NotReady)return found;
   if(entry==null&&records.Count>=ApiRules.Consumers)return ApiStatus.CapacityExceeded;
   int active=0,owned=0,recovery=0;foreach(var e in records.Values)if(!e.Released){active++;if(e.Peer==peer)owned++;if(e.State==ConsumptionState.Recovering)recovery++;}
   if(recovery>=16)return ApiStatus.RecoveryCapacityExceeded;
   if(active>=ApiRules.Operations||owned>=ApiRules.PerPeer)return ApiStatus.Busy;
   entry=new Entry{Consumer=consumer,Id=ApiRules.RequestId(consumer,request),Peer=peer,Request=request,Started=now,Order=++order,State=ConsumptionState.Pending};
   records[consumer]=entry;return ApiStatus.Ready;
  }
  internal void Finish(Entry entry,ConsumptionOutcome outcome,ApiStatus reason,bool released){
   if(entry.Result!=null){if(entry.Result.Outcome!=outcome)throw new InvalidOperationException("Conflicting API receipt");entry.Released|=released;return;}
   entry.Result=new ConsumeResult(entry.Id,entry.Network,outcome,reason,outcome==ConsumptionOutcome.Success?entry.Request.Resources:null);entry.State=ConsumptionState.Completed;entry.Released=released;
  }
  internal void Clear()=>records.Clear();
 }
}
