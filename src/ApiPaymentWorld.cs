using System;
using System.Collections.Generic;
using System.Linq;
using RunicStorageNetwork.API;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 internal sealed class ApiPaymentWorld:IApiPayment {
  readonly ApiContext context;readonly ApiLedger.Entry entry;readonly List<Need> needs;
  readonly List<ApiSource> sources=new List<ApiSource>();readonly HashSet<string> avoid=new HashSet<string>();
  ApiNetworkView view;List<Debit> plan;string[] keys;int prepare,apply;long creator;bool captured;
  internal ApiPaymentWorld(ApiContext context,ApiLedger.Entry entry){this.context=context;this.entry=entry;needs=entry.Request.Resources.Select(r=>new Need(r.Resource.PrefabName,r.Amount,r.Resource.Quality)).ToList();}
  public ApiStatus Prepare(){
   if(!ApiRuntime.Enabled)return ApiStatus.FeatureDisabled;
   // Existing operations can finish across a consumer owner change; identity and access remain checked.
   var status=ApiWorld.Context(context,ZNet.GetUID(),true,out var consumer,out creator);if(status!=ApiStatus.Ready)return status;
   if(!UnloadedNetworks.Ward(consumer.GetPosition(),creator))return ApiStatus.AccessDenied;
   view=ApiRuntime.View(context,consumer,creator);if(view==null)return ApiStatus.CapacityExceeded;
   if(view.Limited)return ApiStatus.SnapshotLimit;
   if(view.Graph==null||view.ObservedAt==0||view.Structure!=UnloadedNetworks.CatalogRevision||view.Scanning&&ApiRuntime.Now-view.ObservedAt>2)return ApiStatus.Updating;
   if(string.IsNullOrEmpty(view.Network))return ApiStatus.NoNetwork;
   if(keys==null){
    entry.Network=view.Network;
    if(ApiRuntime.InNetwork(entry)>ApiRules.PerNetwork)return ApiStatus.Busy;
    plan=Planner.Plan(needs,view.Stocks(needs).Where(s=>!avoid.Contains(s.Source)),true);
    if(plan==null)return view.Complete?ApiStatus.InsufficientResources:ApiStatus.SourcesUnavailable;
    keys=SourceSelection.Sources(plan);if(keys==null||keys.Length>ApiRules.SourceLimit){keys=null;return ApiStatus.PlanLimitExceeded;}
    if(Transport.Instance.PlayerWaiting(keys)||!Transport.Instance.SharedSourceGate.TryAcquire(entry.Id,keys)){keys=null;return ApiStatus.Busy;}
   }
   if(view.Network!=entry.Network)return ApiStatus.NoNetwork;
   if(prepare<keys.Length){
    var z=RemoteContext.Source(keys[prepare]);if(z==null||!ApiWorld.Eligible(z,creator)){avoid.Add(keys[prepare]);return ApiStatus.SourcesUnavailable;}
    var s=ApiOwnership.Prepare(z,entry.Id,creator,ApiRuntime.Now);if(s==null){avoid.Add(keys[prepare]);return ApiStatus.SourcesUnavailable;}
    sources.Add(s);prepare++;return ApiStatus.Updating;
   }
   foreach(var s in sources){if(s.Denied){avoid.Add(R.Key(s.Data.m_uid));return ApiStatus.SourcesUnavailable;}if(!s.Ready)return ApiStatus.Updating;}
   // Replan with the exact owner-confirmed inventories, without trusting a count from the UI.
   plan=Planner.Plan(needs,sources.SelectMany(s=>GatewayRuntime.Filter(view.Graph,view.Network,s.Data.GetPosition(),consumer.GetPosition(),s.Stock)),true);
   return plan==null?ApiStatus.InsufficientResources:ApiStatus.Ready;
  }
  public void Repair(){
   view?.Repair();view=null;keys=null;plan=null;sources.Clear();prepare=apply=0;captured=false;
  }
  public void Capture(){
   if(!ApiRuntime.Enabled||view.Structure!=UnloadedNetworks.CatalogRevision)throw new InvalidOperationException("Network changed before debit");
   if(ApiWorld.Context(context,ZNet.GetUID(),true,out var consumer,out var who)!=ApiStatus.Ready||who!=creator||!UnloadedNetworks.Ward(consumer.GetPosition(),creator))throw new InvalidOperationException("Consumer access changed");
   foreach(var s in sources){
    Check(s,true);if(!ApiWorld.Eligible(s.Data,creator)||!view.Graph.Covers(entry.Network,Topology.Position(s.Data.GetPosition()),n=>true))throw new InvalidOperationException("Source access changed");
    if(plan.Where(d=>d.Source==R.Key(s.Data.m_uid)).Any(d=>!view.Graph.CanTransfer(entry.Network,Topology.Position(s.Data.GetPosition()),Topology.Position(consumer.GetPosition()),GatewayRuntime.Teleportable(d.Item))))throw new InvalidOperationException("Gateway path changed");
    s.Delta=new InventoryDelta(s.Container.GetInventory(),plan.Where(d=>d.Source==R.Key(s.Data.m_uid)),true);
   }
   captured=true;
  }
  static void Check(ApiSource s,bool before){
   if(s.Data==null||!s.Data.IsValid()||s.Data.GetOwner()!=ZNet.GetUID()||s.Data.GetString("rsn_lease","")!=s.Token||!s.Container)throw new InvalidOperationException("Source ownership or reservation changed");
   byte[] expected=before?s.Before:s.Expected;
   if(expected!=null&&!(s.Data.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>()).SequenceEqual(expected))throw new InvalidOperationException("Source changed outside its reservation");
  }
  public bool ApplyNext(){
   if(!captured)throw new InvalidOperationException("Missing capture");if(apply>=sources.Count)return true;
   if(!ApiRuntime.Enabled||view.Structure!=UnloadedNetworks.CatalogRevision)throw new InvalidOperationException("Network changed before debit");
   var s=sources[apply];Check(s,true);
   var consumer=RemoteContext.Data(context.ConsumerId);
   if(consumer==null||plan.Where(d=>d.Source==R.Key(s.Data.m_uid)).Any(d=>!view.Graph.CanTransfer(entry.Network,Topology.Position(s.Data.GetPosition()),Topology.Position(consumer.GetPosition()),GatewayRuntime.Teleportable(d.Item))))throw new InvalidOperationException("Gateway path changed before debit");
   // Mark entry before mutation: a Save/other-mod exception must roll this delta back too.
   s.Applied=true;Transport.InternalMutation++;
   try{s.Delta.Apply();R.Call(s.Container,"Save");s.Expected=(byte[])(s.Data.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>()).Clone();apply++;}
   catch{s.Expected=(byte[])(s.Data.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>()).Clone();throw;}
   finally{Transport.InternalMutation--;}
   return apply==sources.Count;
  }
  public bool Rollback(){
   for(int i=sources.Count-1;i>=0;i--){var s=sources[i];if(!s.Applied)continue;Check(s,false);Transport.InternalMutation++;
    try{s.Delta.Restore();R.Call(s.Container,"Save");s.Expected=(byte[])(s.Data.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>()).Clone();s.Applied=false;}
    catch{s.Expected=(byte[])(s.Data.GetByteArray(ZDOVars.s_items)??Array.Empty<byte>()).Clone();throw;}
    finally{Transport.InternalMutation--;}return false;
   }return true;
  }
  public bool Release(){
   foreach(var s in sources)if(!s.Released){if(!ApiOwnership.Release(s))return false;return false;}
   Transport.Instance.SharedSourceGate.Cancel(entry.Id);return true;
  }
 }
}
