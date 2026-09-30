using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using RunicStorageNetwork.API;
using RunicStorageNetwork.Logic;

namespace RunicStorageNetwork {
 internal sealed class ApiRuntime:MonoBehaviour {
  const string Prefix="RSN_API1_";
  internal static ApiRuntime Instance;internal static double Now=>Time.realtimeSinceStartupAsDouble;
  internal static bool Enabled=>Instance&&Plugin.Enabled&&UnloadedNetworks.Requested&&(Server?UnloadedNetworks.Enabled:Instance.serverVersion==1&&Instance.serverEnabled);
  readonly ApiLedger ledger=new ApiLedger();readonly List<ApiNetworkView> views=new List<ApiNetworkView>();
  readonly Dictionary<string,ReadInterest> reads=new Dictionary<string,ReadInterest>();
  readonly Dictionary<string,LocalRead> localReads=new Dictionary<string,LocalRead>();
  readonly Dictionary<string,LocalOperation> localOperations=new Dictionary<string,LocalOperation>();
  readonly Dictionary<long,ApiBucket> writes=new Dictionary<long,ApiBucket>(),queries=new Dictionary<long,ApiBucket>(),controls=new Dictionary<long,ApiBucket>();
  readonly Dictionary<long,double> peers=new Dictionary<long,double>();
  readonly Queue<ConsumeOperation> callbacks=new Queue<ConsumeOperation>();
  readonly Queue<Outgoing> outgoing=new Queue<Outgoing>();long outgoingBytes;
  readonly Dictionary<string,double> warnings=new Dictionary<string,double>();
  ApiBucket writeRate;ZRoutedRpc rpc;ZDOMan world;Harmony harmony;int thread,scanCursor,jobCursor;double helloAt,cleanupAt;
  string session="";bool serverEnabled;int serverVersion;
  sealed class Outgoing {internal long Peer;internal string Name;internal ZPackage Data;internal int Bytes;}
  sealed class ReadInterest {internal ApiContext Context;internal long Peer;internal double Used;}
  sealed class LocalRead {internal ApiContext Context;internal ResourceKey? Filter;internal ResourceSnapshot Snapshot;internal double Used,Next,ReceivedAt;internal long Serial,ReceivedSerial;internal int Expected,NextPage,Bytes;internal readonly List<ResourceAmount> Rows=new List<ResourceAmount>();}
  sealed class LocalOperation {internal ApiContext Context;internal ConsumeRequest Request;internal ConsumeOperation Handle;internal double Next;}
  void Awake(){
   Instance=this;thread=Thread.CurrentThread.ManagedThreadId;writeRate=new ApiBucket(16,16,Now);
   harmony=new Harmony(Plugin.Guid+".api");try{ApiOwnership.Install(harmony);Transport.ExtraOperations=()=>ledger.Entries.Count(e=>!e.Released);}catch(Exception e){harmony.UnpatchSelf();Transport.ExtraInventoryLock=null;Transport.ExtraItemLock=null;Transport.ExtraOwnerRule=null;Transport.ExtraOperations=null;Instance=null;enabled=false;Plugin.Info("API unavailable: "+e.Message);}
  }
  void OnDestroy(){harmony?.UnpatchSelf();Reset();Transport.ExtraInventoryLock=null;Transport.ExtraItemLock=null;Transport.ExtraOwnerRule=null;Transport.ExtraOperations=null;if(Instance==this)Instance=null;}
  static string Consumer(ApiContext c)=>R.Key(c.ConsumerId)+"/"+c.ModId;
  static bool Valid(ApiContext c)=>c!=null&&c.ConsumerId!=ZDOID.None&&ApiRules.Text(c.ModId);
  static bool Main=>!ReferenceEquals(Instance,null)&&Instance.thread==Thread.CurrentThread.ManagedThreadId;
  static bool Server=>ZNet.instance&&ZNet.instance.IsServer();
  internal static bool HasPeer(long peer)=>peer==ZNet.GetUID()||Instance&&Instance.peers.TryGetValue(peer,out var at)&&Now-at<15;
  internal static void Diagnostic(string category,string message){
   var self=Instance;if(!self)return;double now=Now;if(self.warnings.TryGetValue(category,out var last)&&now-last<15)return;
   if(self.warnings.Count>=32)self.warnings.Clear();self.warnings[category]=now;Plugin.Info("API "+category+": "+message);
  }
  void Reset(){
   foreach(var e in localOperations.Values)if(!e.Handle.IsFinal){e.Handle.Complete(new ConsumeResult(e.Handle.RequestId,"",ConsumptionOutcome.OutcomeUnknown,ApiStatus.Unavailable));QueueCallback(e.Handle);}
   if(Transport.Instance!=null)foreach(var entry in ledger.Entries)Transport.Instance.SharedSourceGate.Cancel(entry.Id);
   ApiOwnership.Clear();ApiWorld.Clear();ledger.Clear();views.Clear();reads.Clear();localReads.Clear();localOperations.Clear();writes.Clear();queries.Clear();controls.Clear();peers.Clear();outgoing.Clear();outgoingBytes=0;rpc=null;session="";serverVersion=0;serverEnabled=false;
  }
  void Update(){
   try {
    if(world!=ZDOMan.instance){Reset();world=ZDOMan.instance;if(world!=null&&Server){session=Guid.NewGuid().ToString("N");serverVersion=1;serverEnabled=Enabled;}}
    if(world==null||ZRoutedRpc.instance==null||!ZNet.instance){DeliverCallbacks();return;}
    if(rpc!=ZRoutedRpc.instance){rpc=ZRoutedRpc.instance;Register();}
    double now=Now;
    if(!Server&&now>=helloAt&&Transport.Server!=0){helloAt=now+3;var p=new ZPackage();p.Write(1);Send(Transport.Server,"hello",p);}
    if(Server){
     var active=ledger.Entries.Where(e=>!e.Released).ToArray();int limit=Math.Min(8,active.Length);
     for(int i=0;i<limit;i++){
      var entry=active[(jobCursor+i)%active.Length];var job=(ApiPayment)entry.Work;job.Tick(now);entry.State=job.State;
      if(job.Outcome.HasValue){ledger.Finish(entry,job.Outcome.Value,job.Reason,job.Released);NotifyLocal(entry);if(job.Released)entry.Work=null;}
      if(job.State==ConsumptionState.Recovering&&now-entry.Started>10)Diagnostic("recovery","Operation "+entry.Id+" is retaining its reservation until its original debit is resolved.");
     }if(active.Length>0)jobCursor=(jobCursor+limit)%active.Length;
     // Cooperative catalogue refresh, only for recently requested scopes. No per-machine Update.
     var watch=System.Diagnostics.Stopwatch.StartNew();int steps=0;
     while(views.Count>0&&steps++<128&&watch.Elapsed.TotalMilliseconds<.5){if(scanCursor>=views.Count)scanCursor=0;var view=views[scanCursor++];if(now-view.Used<30&&Enabled&&UnloadedNetworks.ApiCatalogReady)view.Tick(now);}
    }
    foreach(var op in localOperations.Values.ToArray())if(!op.Handle.IsFinal&&now>=op.Next){op.Next=now+1;if(Server)ProcessLocal(op);else if(Transport.Server!=0){var p=new ZPackage();ApiWire.Context(p,op.Context);ApiWire.Request(p,op.Request);Send(Transport.Server,"consume",p);}}
    ApiOwnership.Tick(now);
    for(int i=0;i<16&&outgoing.Count>0;i++){var item=outgoing.Dequeue();outgoingBytes-=item.Bytes;if(item.Peer==ZNet.GetUID())Dispatch(item.Name,item.Peer,item.Data);else if(item.Peer!=0)rpc.InvokeRoutedRPC(item.Peer,Prefix+item.Name,item.Data);}
    if(now>=cleanupAt){cleanupAt=now+5;Clean(now);}
    DeliverCallbacks();
   }catch(Exception e){Diagnostic("tick",e.Message);}
  }
  void Clean(double now){
   foreach(var key in reads.Where(x=>now-x.Value.Used>30).Select(x=>x.Key).ToArray())reads.Remove(key);
   foreach(var key in localReads.Where(x=>now-x.Value.Used>30).Select(x=>x.Key).ToArray())localReads.Remove(key);
   views.RemoveAll(v=>now-v.Used>30&&!ledger.Entries.Any(e=>!e.Released&&e.Network==v.Network));ApiWorld.Trim(now);
   foreach(long peer in peers.Keys.Where(p=>now-peers[p]>30).ToArray()){peers.Remove(peer);writes.Remove(peer);queries.Remove(peer);controls.Remove(peer);}
  }
  internal static int InNetwork(ApiLedger.Entry current)=>Instance.ledger.Entries.Count(e=>!e.Released&&e.Network==current.Network&&e.Order<=current.Order);
  internal static bool AllowViewBytes(ApiNetworkView current,int bytes)=>Instance.views.Sum(v=>ReferenceEquals(v,current)?bytes:v.Bytes)<=4*1024*1024;
  internal static ApiNetworkView View(ApiContext context,ZDO consumer,long creator){
   var self=Instance;var point=Topology.Position(consumer.GetPosition());
   var view=self.views.FirstOrDefault(v=>v.Creator==creator&&(v.Point.Distance2(point)<.001||v.Graph!=null&&v.Structure==UnloadedNetworks.CatalogRevision&&!string.IsNullOrEmpty(v.Network)&&v.Graph.Choose(point,n=>true)==v.Network));
   if(view==null){if(self.views.Count>=128)return null;self.views.Add(view=new ApiNetworkView{Creator=creator,Point=point});}
   view.Used=Now;return view;
  }
  ConsumerRecovery Recovery(ApiContext context){var z=RemoteContext.Data(context.ConsumerId);var entry=ledger.Find(Consumer(context));return new ConsumerRecovery(z?.OwnerRevision??0,entry?.Request,entry?.Result,entry?.State??ConsumptionState.Pending);}
  ResourceSnapshot Snapshot(ApiContext c,long peer,ResourceKey? filter){
   var status=ApiWorld.Context(c,peer,false,out var z,out long creator);if(status!=ApiStatus.Ready)return new ResourceSnapshot(status,session);
   var recovery=Recovery(c);if(!Enabled)return new ResourceSnapshot(ApiStatus.FeatureDisabled,session,recovery:recovery);
   if(!UnloadedNetworks.ApiCatalogReady)return new ResourceSnapshot(ApiStatus.NotReady,session,recovery:recovery);
   if(!UnloadedNetworks.Ward(z.GetPosition(),creator))return new ResourceSnapshot(ApiStatus.AccessDenied,session,recovery:recovery);
   if(filter.HasValue&&!Known(filter.Value))return new ResourceSnapshot(ApiStatus.UnknownItem,session,recovery:recovery);
   var view=View(c,z,creator);if(view==null)return new ResourceSnapshot(ApiStatus.SnapshotLimit,session,recovery:recovery);
   if(view.Graph==null||view.ObservedAt==0)return new ResourceSnapshot(ApiStatus.Updating,session,recovery:recovery);
   if(view.Limited)return new ResourceSnapshot(ApiStatus.SnapshotLimit,session,recovery:recovery);
   if(view.Structure!=UnloadedNetworks.CatalogRevision)return new ResourceSnapshot(ApiStatus.Updating,session,recovery:recovery);
   bool stale=view.Structure!=UnloadedNetworks.CatalogRevision||Now-view.ObservedAt>2;
   status=stale?ApiStatus.Updating:string.IsNullOrEmpty(view.Network)?ApiStatus.NoNetwork:view.Complete?ApiStatus.Ready:ApiStatus.Partial;
   IReadOnlyList<ResourceAmount> rows=view.Resources;
   if(filter.HasValue){view.Amounts.TryGetValue(filter.Value,out long amount);rows=Array.AsReadOnly(new[]{new ResourceAmount(filter.Value,amount)});}
   return new ResourceSnapshot(status,session,view.Network,view.Revision,view.ObservedAt,Math.Max(0,Now-view.ObservedAt),stale,view.Complete&&!stale&&status==ApiStatus.Ready,view.Unknown,rows,recovery);
  }
  static bool Known(ResourceKey k)=>ApiRules.Text(k.PrefabName)&&k.Quality>0&&k.Quality<=10000&&ObjectDB.instance&&ObjectDB.instance.GetItemPrefab(k.PrefabName);
  internal static ResourceSnapshot Read(ApiContext context,ResourceKey? filter){
   if(ReferenceEquals(Instance,null))return new ResourceSnapshot(ApiStatus.FeatureDisabled);
   if(!Main)return new ResourceSnapshot(ApiStatus.WrongThread);
   if(!Valid(context))return new ResourceSnapshot(ApiStatus.InvalidRequest);
   var self=Instance;if(self.world==null||!ZNet.instance)return new ResourceSnapshot(ApiStatus.Unavailable);
   var access=ApiWorld.Context(context,ZNet.GetUID(),false,out _,out _);if(access!=ApiStatus.Ready)return new ResourceSnapshot(access,self.session);
   if(filter.HasValue&&!Known(filter.Value))return new ResourceSnapshot(ApiStatus.UnknownItem,self.session);
   string key=Consumer(context)+(filter.HasValue?"/"+filter.Value.ToString():"/*");
   if(!self.localReads.TryGetValue(key,out var local)){if(self.localReads.Count>=128)return new ResourceSnapshot(ApiStatus.SnapshotLimit,self.session);self.localReads[key]=local=new LocalRead{Context=context,Filter=filter};}
   double now=Now;local.Used=now;
   if(now>=local.Next){local.Next=now+1;
    if(Server){local.Snapshot=self.Snapshot(context,ZNet.GetUID(),filter);local.ReceivedAt=now;}
    else if(self.serverVersion==1&&Transport.Server!=0){local.Serial++;var p=new ZPackage();p.Write(key);p.Write(local.Serial);ApiWire.Context(p,context);p.Write(filter.HasValue);if(filter.HasValue)ApiWire.Key(p,filter.Value);Send(Transport.Server,"read",p);}
   }
   if(!Server&&Plugin.Enabled&&UnloadedNetworks.Requested&&self.serverVersion==0)return new ResourceSnapshot(ApiStatus.NotReady,self.session,recovery:local.Snapshot?.Recovery);
   if(!Server&&self.serverVersion!=0&&self.serverVersion!=1)return new ResourceSnapshot(ApiStatus.IncompatibleVersion,self.session,recovery:local.Snapshot?.Recovery);
   if(!Enabled)return new ResourceSnapshot(ApiStatus.FeatureDisabled,self.session,recovery:local.Snapshot?.Recovery);
   if(local.Snapshot==null)return new ResourceSnapshot(self.serverVersion>1?ApiStatus.IncompatibleVersion:self.serverVersion==1&&!self.serverEnabled?ApiStatus.FeatureDisabled:ApiStatus.NotReady,self.session);
   var snapshot=local.Snapshot;if(now-local.ReceivedAt>2)return new ResourceSnapshot(ApiStatus.Updating,self.session,network:snapshot.NetworkId,revision:snapshot.Revision,age:snapshot.Age+now-local.ReceivedAt,stale:true,resources:snapshot.Resources,recovery:snapshot.Recovery);
   return snapshot;
  }
  internal static ResourceAmountResult Amount(ApiContext context,ResourceKey resource){var snap=Read(context,resource);var found=snap.Resources.FirstOrDefault(r=>r.Resource.Equals(resource));return new ResourceAmountResult(snap,found?.ObservedAmount);}
  static ConsumeOperation Reject(ApiStatus status,string id="",ConsumptionOutcome? outcome=null)=>new ConsumeOperation{AdmissionCode=status,RequestId=id,State=ConsumptionState.Completed,IsFinal=true,Result=new ConsumeResult(id,"",outcome,status),RetryAfter=status==ApiStatus.Busy||status==ApiStatus.Throttled?1:0};
  internal static ConsumeOperation Consume(ApiContext context,ConsumeRequest request,Action<ConsumeResult> completed){
   if(ReferenceEquals(Instance,null))return Reject(ApiStatus.FeatureDisabled);if(!Main)return Reject(ApiStatus.WrongThread);
   if(!Valid(context)||!ApiRules.Normalize(request,out var normalized))return Reject(ApiStatus.InvalidRequest);
   var self=Instance;string key=Consumer(context),id=ApiRules.RequestId(key,normalized);
   if(self.world==null)return Reject(ApiStatus.Unavailable,id);
   if(self.localOperations.TryGetValue(key,out var old)){
    if(old.Request.SessionId==normalized.SessionId&&old.Request.Sequence==normalized.Sequence){if(!ApiRules.Same(old.Request,normalized))return Reject(ApiStatus.RequestConflict,id);if(!old.Handle.IsFinal||old.Handle.Result.Outcome.HasValue){old.Handle.Subscribe(completed);self.QueueCallback(old.Handle);return old.Handle;}}
    if(!old.Handle.IsFinal)return Reject(ApiStatus.Busy,id);
   }
   if(normalized.SessionId!=self.session)return Reject(ApiStatus.Unavailable,id,ConsumptionOutcome.OutcomeUnknown);
   if(!self.localOperations.ContainsKey(key)&&self.localOperations.Count>=4096)return Reject(ApiStatus.CapacityExceeded,id);
   var status=ApiWorld.Context(context,ZNet.GetUID(),Server,out _,out _);if(status!=ApiStatus.Ready)return Reject(status,id);
   var op=new LocalOperation{Context=context,Request=normalized,Handle=new ConsumeOperation{RequestId=id,AdmissionCode=ApiStatus.Ready,State=ConsumptionState.Pending}};op.Handle.Subscribe(completed);self.localOperations[key]=op;
   if(Server)self.ProcessLocal(op); // admission only; writing and callbacks always occur on later ticks.
   return op.Handle;
  }
  ApiStatus Admit(ApiContext context,long peer,ConsumeRequest request,out ApiLedger.Entry entry){
   entry=null;if(request.SessionId!=session)return ApiStatus.Unavailable;
   var status=ApiWorld.Context(context,peer,true,out _,out _);if(status!=ApiStatus.Ready)return status;
   string key=Consumer(context);var found=ledger.Lookup(key,request,out entry);if(found!=ApiStatus.NotReady)return found;
   status=ApiWorld.Context(context,peer,false,out _,out _);if(status!=ApiStatus.Ready)return status;
   if(!Enabled)return ApiStatus.FeatureDisabled;if(!UnloadedNetworks.ApiCatalogReady)return ApiStatus.NotReady;
   if(Transport.Instance.OccupiedOperations+ledger.Entries.Count(e=>!e.Released)>=256)return ApiStatus.Busy;
   foreach(var row in request.Resources)if(!Known(row.Resource))return ApiStatus.UnknownItem;
   if(!writes.TryGetValue(peer,out var bucket)){if(writes.Count>=128)return ApiStatus.CapacityExceeded;writes[peer]=bucket=new ApiBucket(4,4,Now);}
   if(!bucket.Take(Now)||!writeRate.Take(Now))return ApiStatus.Throttled;
   status=ledger.Admit(key,peer,request,Now,out entry);if(status==ApiStatus.Ready&&entry.Work==null)entry.Work=new ApiPayment(new ApiPaymentWorld(context,entry),Now,request.StartWithin);
   return status;
  }
  void ProcessLocal(LocalOperation op){var status=Admit(op.Context,ZNet.GetUID(),op.Request,out var entry);UpdateOperation(op,status,entry?.State??ConsumptionState.Pending,status==ApiStatus.Ready?entry.Result:null);}
  void UpdateOperation(LocalOperation local,ApiStatus admission,ConsumptionState state,ConsumeResult result){
   var handle=local.Handle;if(handle.IsFinal)return;
   handle.AdmissionCode=admission;handle.State=state;
   if(admission!=ApiStatus.Ready)handle.Complete(new ConsumeResult(handle.RequestId,"",admission==ApiStatus.Unavailable&&local.Request.SessionId!=session?(ConsumptionOutcome?)ConsumptionOutcome.OutcomeUnknown:null,admission));
   else if(result!=null)handle.Complete(result);QueueCallback(handle);
  }
  void NotifyLocal(ApiLedger.Entry entry){if(localOperations.TryGetValue(entry.Consumer,out var local)&&local.Request.Sequence==entry.Request.Sequence)UpdateOperation(local,ApiStatus.Ready,entry.State,entry.Result);}
  void QueueCallback(ConsumeOperation op){if(op.IsFinal&&!op.Delivered&&!op.Queued&&op.Handler!=null){op.Queued=true;callbacks.Enqueue(op);}}
  void DeliverCallbacks(){int count=callbacks.Count;for(int i=0;i<count;i++){var op=callbacks.Dequeue();op.Queued=false;if(op.Delivered)continue;op.Delivered=true;try{op.Handler?.Invoke(op.Result);}catch(Exception e){Diagnostic("callback",e.Message);}op.Handler=null;}}
  internal static void Send(long peer,string name,ZPackage p){
   var self=Instance;if(!self||peer==0)return;int bytes=p.Size();if(bytes>ApiRules.MaxPacketBytes)throw new InvalidOperationException("API packet limit");
   if(self.outgoing.Count>=4096||self.outgoingBytes+bytes>4*1024*1024){Diagnostic("backpressure","Outgoing API queue full; requests retain their IDs for retry.");return;}
   self.outgoing.Enqueue(new Outgoing{Peer=peer,Name=name,Data=p,Bytes=bytes});self.outgoingBytes+=bytes;
  }
  void Register(){foreach(string name in new[]{"hello","welcome","read","snapshot","consume","result","prepare","grant","ticket","release"}){string n=name;rpc.Register<ZPackage>(Prefix+n,(sender,p)=>Dispatch(n,sender,p));}}
  void Dispatch(string name,long sender,ZPackage p){
   try{if(p.Size()>ApiRules.MaxPacketBytes)return;p.SetPos(0);
    switch(name){
     case "hello":if(!Server)return;int version=p.ReadInt();if(version==1&&peers.Count<128)peers[sender]=Now;var welcome=new ZPackage();welcome.Write(1);welcome.Write(session);welcome.Write(Enabled);Send(sender,"welcome",welcome);break;
     case "welcome":if(sender!=Transport.Server||Server)return;int v=p.ReadInt();string current=ApiWire.Text(p,64);bool enabled=p.ReadBool();
      if(session!=""&&session!=current){foreach(var local in localOperations.Values)if(!local.Handle.IsFinal){local.Handle.Complete(new ConsumeResult(local.Handle.RequestId,"",ConsumptionOutcome.OutcomeUnknown,ApiStatus.Unavailable));QueueCallback(local.Handle);}localReads.Clear();}
      session=current;serverVersion=v;serverEnabled=enabled;break;
     case "read":if(Server&&RateRead(sender))ReceiveRead(sender,p);break;
     case "snapshot":if(!Server&&sender==Transport.Server)ReceiveSnapshot(p);break;
     case "consume":if(Server&&RateRead(sender,true)){var c=ApiWire.Context(p);var r=ApiWire.Request(p);var status=Admit(c,sender,r,out var entry);var reply=new ZPackage();ApiWire.Context(reply,c);reply.Write(r.SessionId);reply.Write(r.Sequence);reply.Write((int)status);reply.Write((int)(entry?.State??ConsumptionState.Pending));ApiWire.Result(reply,status==ApiStatus.Ready?entry.Result:null);Send(sender,"result",reply);}break;
     case "result":if(!Server&&sender==Transport.Server){var c=ApiWire.Context(p);string s=ApiWire.Text(p,64);long seq=p.ReadLong();var code=(ApiStatus)p.ReadInt();var state=(ConsumptionState)p.ReadInt();var result=ApiWire.Result(p);if(localOperations.TryGetValue(Consumer(c),out var local)&&local.Request.SessionId==s&&local.Request.Sequence==seq)UpdateOperation(local,code,state,result);}break;
     case "prepare":ApiOwnership.ReceivePrepare(sender,p);break;
     case "grant":ApiOwnership.ReceiveGrant(sender,p);break;
     case "ticket":if(Server&&RateRead(sender,true))ApiOwnership.ReceiveTicket(sender,p);break;
     case "release":ApiOwnership.ReceiveRelease(sender,p);break;
    }
   }catch(Exception e){Diagnostic("protocol",name+": "+e.Message);}
  }
  bool RateRead(long peer,bool control=false){if(!HasPeer(peer))return false;var table=control?controls:queries;if(!table.TryGetValue(peer,out var rate)){if(table.Count>=128)return false;table[peer]=rate=new ApiBucket(control?128:16,control?256:32,Now);}return rate.Take(Now);}
  void ReceiveRead(long peer,ZPackage p){
   string key=ApiWire.Text(p,512);long serial=p.ReadLong();var context=ApiWire.Context(p);ResourceKey? filter=p.ReadBool()?(ResourceKey?)ApiWire.Key(p):null;
   string interest=peer+"/"+Consumer(context);if(!reads.TryGetValue(interest,out var watch)){if(reads.Count>=128)return;reads[interest]=watch=new ReadInterest{Context=context,Peer=peer};}watch.Used=Now;
   var snap=Snapshot(context,peer,filter);int count=snap.Resources.Count;int pages=Math.Max(1,(count+63)/64);
   if(count>4096){snap=new ResourceSnapshot(ApiStatus.SnapshotLimit,session,recovery:snap.Recovery);pages=1;count=0;}
   for(int page=0;page<pages;page++){
    var reply=new ZPackage();reply.Write(key);reply.Write(serial);reply.Write(page);reply.Write(pages);reply.Write((int)snap.Status);reply.Write(snap.SessionId);reply.Write(snap.NetworkId??"");reply.Write(snap.Revision);reply.Write(snap.ObservedAt);reply.Write(snap.Age);reply.Write(snap.IsStale);reply.Write(snap.IsComplete);reply.Write(snap.UnknownSources);ApiWire.Recovery(reply,snap.Recovery);
    int start=page*64,n=Math.Min(64,count-start);reply.Write(n);for(int i=0;i<n;i++){var row=snap.Resources[start+i];ApiWire.Key(reply,row.Resource);reply.Write(row.ObservedAmount);}Send(peer,"snapshot",reply);
   }
  }
  void ReceiveSnapshot(ZPackage p){
   string key=ApiWire.Text(p,512);long serial=p.ReadLong();int page=p.ReadInt(),pages=p.ReadInt();if(!localReads.TryGetValue(key,out var local)||serial<local.ReceivedSerial||serial>local.Serial||pages<1||pages>64||page<0||page>=pages)return;
   if(page==0){local.ReceivedSerial=serial;local.NextPage=0;local.Expected=pages;local.Rows.Clear();local.Bytes=0;}
   if(serial!=local.ReceivedSerial||page!=local.NextPage||pages!=local.Expected)return;
   var status=(ApiStatus)p.ReadInt();string s=ApiWire.Text(p,64),network=p.ReadString();if(s!=session||network.Length>256)return;long revision=p.ReadLong();double observed=p.ReadDouble(),age=p.ReadDouble();bool stale=p.ReadBool(),complete=p.ReadBool();int unknown=p.ReadInt();var recovery=ApiWire.Recovery(p);int n=p.ReadInt();
   if(n<0||n>64)return;for(int i=0;i<n;i++)local.Rows.Add(new ResourceAmount(ApiWire.Key(p),p.ReadLong()));local.Bytes+=p.Size();if(local.Bytes>1024*1024){local.Rows.Clear();return;}local.NextPage++;
   if(local.NextPage==pages){if(localReads.Values.Sum(r=>r.Bytes)>16*1024*1024){local.Rows.Clear();local.Snapshot=new ResourceSnapshot(ApiStatus.SnapshotLimit,session,recovery:recovery);local.Bytes=0;}else local.Snapshot=new ResourceSnapshot(status,s,network,revision,observed,age,stale,complete,unknown,Array.AsReadOnly(local.Rows.ToArray()),recovery);local.ReceivedAt=Now;}
  }
 }
}
