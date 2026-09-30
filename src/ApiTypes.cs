using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RunicStorageNetwork.API {
 public enum ApiStatus { Ready, Partial, Updating, NotReady, NoNetwork, UnknownItem, FeatureDisabled, Unavailable, IncompatibleVersion, NotOwner, AccessDenied, UnsupportedConsumer, InvalidRequest, Busy, Throttled, CapacityExceeded, RecoveryCapacityExceeded, RequestConflict, ExpiredRequest, SourcesUnavailable, InsufficientResources, StartExpired, PlanLimitExceeded, SnapshotLimit, WrongThread }
 public enum ConsumptionState { Pending, Repairing, Recovering, Completed }
 public enum ConsumptionOutcome { Success, NoDebit, OutcomeUnknown }
 public enum ResourceMatchPolicy { Unspecified, AnyMatchingInstance }
 public readonly struct ResourceKey:IEquatable<ResourceKey> {
  public string PrefabName {get;} public int Quality {get;}
  public ResourceKey(string prefabName,int quality=1){PrefabName=prefabName;Quality=quality;}
  public bool Equals(ResourceKey other)=>Quality==other.Quality&&string.Equals(PrefabName,other.PrefabName,StringComparison.Ordinal);
  public override bool Equals(object other)=>other is ResourceKey key&&Equals(key);
  public override int GetHashCode()=>((PrefabName==null?0:StringComparer.Ordinal.GetHashCode(PrefabName))*397)^Quality;
  public override string ToString()=>PrefabName+"@"+Quality;
 }
 public sealed class ResourceAmount {
  public ResourceKey Resource {get;} public long ObservedAmount {get;}
  public ResourceAmount(ResourceKey resource,long observedAmount){Resource=resource;ObservedAmount=observedAmount;}
 }
 public sealed class ResourceRequirement {
  public ResourceKey Resource {get;} public int Amount {get;}
  public ResourceRequirement(ResourceKey resource,int amount){Resource=resource;Amount=amount;}
 }
 public sealed class ConsumeRequest {
  public string SessionId {get;} public long Sequence {get;} public string CycleRevision {get;}
  public ResourceMatchPolicy MatchPolicy {get;} public double StartWithin {get;}
  public IReadOnlyList<ResourceRequirement> Resources {get;}
  public ConsumeRequest(string sessionId,long sequence,string cycleRevision,IEnumerable<ResourceRequirement> resources,ResourceMatchPolicy matchPolicy=ResourceMatchPolicy.Unspecified,double startWithin=10){
   SessionId=sessionId;Sequence=sequence;CycleRevision=cycleRevision;MatchPolicy=matchPolicy;StartWithin=startWithin;
   Resources=Array.AsReadOnly((resources??Enumerable.Empty<ResourceRequirement>()).Take(33).ToArray());
  }
 }
 public sealed class ConsumeResult {
  public string RequestId {get;} public string NetworkId {get;} public ConsumptionOutcome? Outcome {get;} public ApiStatus Reason {get;}
  public IReadOnlyList<ResourceRequirement> Resources {get;}
  internal ConsumeResult(string id,string network,ConsumptionOutcome? outcome,ApiStatus reason,IEnumerable<ResourceRequirement> resources=null){
   RequestId=id;NetworkId=network??"";Outcome=outcome;Reason=reason;Resources=Array.AsReadOnly((resources??Enumerable.Empty<ResourceRequirement>()).ToArray());
  }
 }
 public sealed class ConsumeOperation {
  public string RequestId {get;internal set;} public ApiStatus AdmissionCode {get;internal set;}
  public ConsumptionState State {get;internal set;} public bool IsFinal {get;internal set;}
  public ConsumeResult Result {get;internal set;} public double RetryAfter {get;internal set;}
  internal Action<ConsumeResult> Handler;internal bool Delivered,Queued;
  internal void Subscribe(Action<ConsumeResult> callback){if(!Delivered&&Handler==null)Handler=callback;}
  internal void Complete(ConsumeResult value){if(IsFinal)return;Result=value;State=ConsumptionState.Completed;IsFinal=true;}
 }
 public sealed class ConsumerRecovery {
  public long OwnerEpoch {get;} public ConsumeRequest Request {get;} public ConsumeResult Result {get;} public ConsumptionState State {get;}
  internal ConsumerRecovery(long epoch,ConsumeRequest request=null,ConsumeResult result=null,ConsumptionState state=ConsumptionState.Pending){OwnerEpoch=epoch;Request=request;Result=result;State=state;}
 }
 public sealed class ResourceSnapshot {
  public ApiStatus Status {get;} public string SessionId {get;} public string NetworkId {get;} public long Revision {get;}
  public double ObservedAt {get;} public double Age {get;} public bool IsStale {get;} public bool IsComplete {get;}
  public int UnknownSources {get;} public ConsumerRecovery Recovery {get;}
  public IReadOnlyList<ResourceAmount> Resources {get;}
  internal ResourceSnapshot(ApiStatus status,string session="",string network="",long revision=0,double observedAt=0,double age=0,bool stale=false,bool complete=false,int unknown=0,IReadOnlyList<ResourceAmount> resources=null,ConsumerRecovery recovery=null){
   Status=status;SessionId=session;NetworkId=network;Revision=revision;ObservedAt=observedAt;Age=age;IsStale=stale;IsComplete=complete;UnknownSources=unknown;
   Resources=resources??Array.AsReadOnly(Array.Empty<ResourceAmount>());Recovery=recovery;
  }
 }
 public sealed class ResourceAmountResult {
  public ApiStatus Status {get;} public string SessionId {get;} public string NetworkId {get;} public long Revision {get;}
  public long? Amount {get;} public long? ObservedAmount {get;} public bool IsStale {get;} public double Age {get;} public ConsumerRecovery Recovery {get;}
  internal ResourceAmountResult(ResourceSnapshot snapshot,long? observed){Status=snapshot.Status;SessionId=snapshot.SessionId;NetworkId=snapshot.NetworkId;Revision=snapshot.Revision;ObservedAmount=observed;Amount=snapshot.IsComplete?observed:null;IsStale=snapshot.IsStale;Age=snapshot.Age;Recovery=snapshot.Recovery;}
 }
}
